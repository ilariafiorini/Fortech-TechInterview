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

    private static async Task<(HttpStatusCode StatusCode, TimeSpan Elapsed)> TimedGetAsync(string relativeUrl)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await GetAsync(relativeUrl);
        stopwatch.Stop();
        return (response.StatusCode, stopwatch.Elapsed);
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
