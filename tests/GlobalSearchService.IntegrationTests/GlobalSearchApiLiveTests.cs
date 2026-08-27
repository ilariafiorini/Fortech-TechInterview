using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace GlobalSearchService.IntegrationTests;

/// <summary>
/// Test "dal vivo" contro la stack Docker Compose realmente avviata (vedi
/// docker/docker-compose.yml): nessun mock, nessun doppio di test — chiamano davvero
/// GlobalSearchService, che a sua volta chiama Airports/Flights reali e Redis reale.
///
/// PREREQUISITO: `docker compose up` deve essere gia' in esecuzione (cartella docker/,
/// vedi docker/README.md) prima di lanciare questi test.
///
/// Progetto VOLUTAMENTE non referenziato in TechInterview.sln: un `dotnet test` (o "Run
/// All" in Visual Studio) alla radice della solution non li tocca mai per sbaglio e non
/// fallisce se Docker e' spento. Per lanciarli esplicitamente:
///
///     dotnet test tests/GlobalSearchService.IntegrationTests/GlobalSearchService.IntegrationTests.csproj
///
/// Nota sui dati usati nelle asserzioni: AirportsService genera 300 aeroporti casuali una
/// sola volta all'avvio (dati stabili per tutta la vita del container, ma diversi ad ogni
/// riavvio), mentre FlightsService rigenera 1000 voli casuali ad ogni singola chiamata gRPC
/// (vedi docs/architecture.md, voce sul fan-out verso FlightsService, per il perche'). Per
/// questo qui sotto non si assume mai un contenuto preciso (es. "esiste il volo AZ178"),
/// solo proprieta' statisticamente certe (es. "tra 300 aeroporti e 1000 voli campionati
/// uniformemente su 12/10 citta' fisse, 'Milano' compare quasi di sicuro in entrambi") o
/// puramente strutturali (ordine, conteggio, comportamento su input non valido).
/// </summary>
public class GlobalSearchApiLiveTests
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri(TestConfig.GlobalSearchBaseUrl) };

    [Fact]
    public async Task HealthEndpoint_IsUp()
    {
        var response = await GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_QueryTooShort_ReturnsBadRequest()
    {
        var response = await GetAsync("/api/global-search?query=ab&offset=0&limit=10");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_KnownCity_ReturnsAirportsBeforeFlights()
    {
        // "Milano" e' una delle 12/10 citta' fisse usate rispettivamente da
        // AirportsRepository.cs e FlightsServiceImpl.cs per generare i dati mock: su 300
        // aeroporti e 1000 voli campionati uniformemente, la probabilita' che "Milano" non
        // compaia mai in nessuno dei due e' trascurabile (< 1 su 10^17 per gli aeroporti).
        // Non e' un dato imposto dal servizio, e' un fatto statistico sui suoi generatori.
        var response = await GetAsync("/api/global-search?query=Milano&offset=0&limit=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SearchResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Items);
        Assert.Contains(body.Items, i => i.ResourceType == "airport");

        var lastAirportIndex = body.Items.FindLastIndex(i => i.ResourceType == "airport");
        var firstFlightIndex = body.Items.FindIndex(i => i.ResourceType == "flight");

        if (firstFlightIndex >= 0)
        {
            Assert.True(lastAirportIndex < firstFlightIndex,
                "Tutti gli aeroporti devono precedere tutti i voli nell'elenco risultati.");
        }
    }

    [Fact]
    public async Task Search_OffsetBeyondResults_ReturnsEmptyItemsWithRealCount()
    {
        var response = await GetAsync("/api/global-search?query=Milano&offset=100000&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SearchResponseDto>();
        Assert.NotNull(body);
        Assert.Empty(body!.Items);
        Assert.True(body!.Count > 0, "Il totale reale deve restare positivo anche quando la pagina richiesta e' vuota.");
        Assert.Equal(100000, body.Offset);
    }

    [Fact]
    public async Task Search_ResourceTypeAirport_ReturnsOnlyAirports()
    {
        var response = await GetAsync("/api/global-search?query=Milano&offset=0&limit=50&resourceType=airport");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SearchResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Items);
        Assert.All(body.Items, i => Assert.Equal("airport", i.ResourceType));
    }

    [Fact]
    public async Task Search_ResourceTypeInvalid_ReturnsBadRequest()
    {
        var response = await GetAsync("/api/global-search?query=Milano&offset=0&limit=10&resourceType=treno");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_SecondIdenticalCall_IsMuchFasterThanFirst_ThanksToCaching()
    {
        // Stringa quasi certamente mai cercata prima (suffisso casuale): garantisce un
        // primo colpo a vuoto sulla cache, cosi' la differenza di tempo misura davvero
        // l'effetto della cache Redis e non un residuo di esecuzioni precedenti di questo
        // stesso test contro la stessa stack ancora avviata.
        var query = "zzq" + Guid.NewGuid().ToString("N")[..10];

        var first = await TimedGetAsync($"/api/global-search?query={query}&offset=0&limit=10");
        var second = await TimedGetAsync($"/api/global-search?query={query}&offset=0&limit=10");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // La seconda chiamata salta interamente il fan-out verso Airports/Flights: ci
        // aspettiamo un miglioramento netto, non solo "un po' meglio" (soglia 2x per
        // assorbire il normale rumore di rete/CPU su una singola chiamata).
        Assert.True(second.Elapsed < first.Elapsed / 2,
            "Attesa una seconda chiamata via cache almeno 2 volte piu' veloce della prima: " +
            $"prima={first.Elapsed.TotalMilliseconds:F0}ms, seconda={second.Elapsed.TotalMilliseconds:F0}ms");
    }

    private static async Task<HttpResponseMessage> GetAsync(string relativeUrl)
    {
        try
        {
            return await Client.GetAsync(relativeUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(
                $"Impossibile contattare {TestConfig.GlobalSearchBaseUrl}{relativeUrl}. " +
                "Docker Compose e' avviato? (cartella docker/: `docker compose up`)", ex);
        }
    }

    [Fact]
    public async Task Search_ThenGetCachedAirportDetail_MatchesTheRowFromTheList()
    {
        var searchResponse = await GetAsync("/api/global-search?query=Milano&offset=0&limit=50&resourceType=airport");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

        var searchBody = await searchResponse.Content.ReadFromJsonAsync<SearchResponseDto>();
        Assert.NotNull(searchBody);
        var row = searchBody!.Items.FirstOrDefault();
        Assert.NotNull(row);

        var detailResponse = await GetAsync($"/api/global-search/airports/{row!.Id}?query=Milano");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<AirportDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(row.Id, detail!.Id);

        // Stessa formula di proiezione usata da RealGlobalSearchService.ProjectAirport:
        // se il dettaglio corrisponde davvero alla riga, la Description della lista deve
        // essere ricostruibile esattamente dai campi del dettaglio.
        Assert.Equal($"{detail.Id} - {detail.Name} ({detail.Country})", row.Description);
    }

    [Fact]
    public async Task Search_ThenGetCachedFlightDetail_MatchesTheRowFromTheList_AndStaysStableOnRepeatedCalls()
    {
        var searchResponse = await GetAsync("/api/global-search?query=Milano&offset=0&limit=50&resourceType=flight");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

        var searchBody = await searchResponse.Content.ReadFromJsonAsync<SearchResponseDto>();
        Assert.NotNull(searchBody);
        var row = searchBody!.Items.FirstOrDefault();
        Assert.NotNull(row);

        var firstDetailResponse = await GetAsync($"/api/global-search/flights/{row!.Id}?query=Milano");
        Assert.Equal(HttpStatusCode.OK, firstDetailResponse.StatusCode);
        var firstDetail = await firstDetailResponse.Content.ReadFromJsonAsync<FlightDetailDto>();
        Assert.NotNull(firstDetail);
        Assert.Equal(row.Id, firstDetail!.Id);
        Assert.Equal($"{firstDetail.Id} - {firstDetail.DepartureCity} -> {firstDetail.ArrivalCity}", row.Description);

        // Il punto centrale del bug originale (vedi docs/architecture.md): FlightsService
        // rigenera dati casuali ad ogni chiamata gRPC diretta. Rileggendo invece dalla
        // stessa cache di ricerca (stessa query), due letture successive dello stesso id
        // devono restituire ESATTAMENTE lo stesso contenuto — non un'istantanea nuova ogni
        // volta, come accadeva prima di questa estensione.
        var secondDetailResponse = await GetAsync($"/api/global-search/flights/{row.Id}?query=Milano");
        Assert.Equal(HttpStatusCode.OK, secondDetailResponse.StatusCode);
        var secondDetail = await secondDetailResponse.Content.ReadFromJsonAsync<FlightDetailDto>();
        Assert.NotNull(secondDetail);

        Assert.Equal(firstDetail.AircraftNumber, secondDetail!.AircraftNumber);
        Assert.Equal(firstDetail.DepartureCity, secondDetail.DepartureCity);
        Assert.Equal(firstDetail.ArrivalCity, secondDetail.ArrivalCity);
        Assert.Equal(firstDetail.DepartureAirportCode, secondDetail.DepartureAirportCode);
        Assert.Equal(firstDetail.ArrivalAirportCode, secondDetail.ArrivalAirportCode);
        Assert.Equal(firstDetail.DepartureTime, secondDetail.DepartureTime);
        Assert.Equal(firstDetail.ArrivalTime, secondDetail.ArrivalTime);
    }

    [Fact]
    public async Task GetCachedAirportDetail_QueryMissing_ReturnsBadRequest()
    {
        var response = await GetAsync("/api/global-search/airports/AP0001");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCachedFlightDetail_UnknownIdForThatQuery_ReturnsNotFound()
    {
        // Un id chiaramente inventato non puo' comparire nell'elenco filtrato per "Milano",
        // qualunque esso sia in quel momento.
        var response = await GetAsync("/api/global-search/flights/NON-ESISTE-123?query=Milano");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(HttpStatusCode StatusCode, TimeSpan Elapsed)> TimedGetAsync(string relativeUrl)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await GetAsync(relativeUrl);
        stopwatch.Stop();
        return (response.StatusCode, stopwatch.Elapsed);
    }

    private class AirportDetailDto
    {
        public string Id { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string City { get; set; } = default!;
        public string Country { get; set; } = default!;
    }

    private class FlightDetailDto
    {
        public string Id { get; set; } = default!;
        public string AircraftNumber { get; set; } = default!;
        public string DepartureAirportCode { get; set; } = default!;
        public string ArrivalAirportCode { get; set; } = default!;
        public string DepartureCity { get; set; } = default!;
        public string ArrivalCity { get; set; } = default!;
        public string DepartureTime { get; set; } = default!;
        public string ArrivalTime { get; set; } = default!;
    }

    private class SearchResultDto
    {
        public string Id { get; set; } = default!;
        public string ResourceType { get; set; } = default!;
        public string Description { get; set; } = default!;
    }

    private class SearchResponseDto
    {
        public List<SearchResultDto> Items { get; set; } = new();
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int Count { get; set; }
    }
}
