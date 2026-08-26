using GlobalSearchService.Models;
using GlobalSearchService.Services;
using Moq;
using Xunit;

namespace GlobalSearchService.Tests.Services;

/// <summary>
/// Verifica RealGlobalSearchService in isolamento, mockando direttamente
/// IAirportsSearchCache/IFlightsSearchCache: qui non serve nessun doppio per HTTP/gRPC/
/// Redis, perche' l'aggregazione (ordine, paginazione, conteggio, normalizzazione della
/// query) e' interamente indipendente da come le due fonti recuperano i dati. Copre le tre
/// decisioni di design discusse in docs/architecture.md: ordine Airports-poi-Flights, count
/// come totale (non dimensione di pagina), e offset/limit oltre la fine dei risultati come
/// 200 con items vuoto anziche' un errore.
/// </summary>
public class RealGlobalSearchServiceTests
{
    private static AirportDto Airport(string id, string city = "Milano") =>
        new() { Id = id, Name = id, City = city, Country = "Italy" };

    private static FlightDto Flight(string id) => new()
    {
        Id = id,
        AircraftNumber = "N1",
        DepartureAirportCode = "MXP",
        ArrivalAirportCode = "JFK",
        DepartureCity = "Milano",
        ArrivalCity = "New York"
    };

    private static RealGlobalSearchService CreateSut(IReadOnlyList<AirportDto> airports, IReadOnlyList<FlightDto> flights)
    {
        var airportsCache = new Mock<IAirportsSearchCache>();
        airportsCache.Setup(c => c.GetMatchesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(airports);

        var flightsCache = new Mock<IFlightsSearchCache>();
        flightsCache.Setup(c => c.GetMatchesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(flights);

        return new RealGlobalSearchService(airportsCache.Object, flightsCache.Object);
    }

    [Fact]
    public async Task SearchAsync_OrdersAirportsBeforeFlights()
    {
        var sut = CreateSut([Airport("MXP")], [Flight("AZ100")]);

        var response = await sut.SearchAsync("mi", offset: 0, limit: 10, CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("airport", response.Items[0].ResourceType);
        Assert.Equal("flight", response.Items[1].ResourceType);
    }

    [Fact]
    public async Task SearchAsync_CountIsTotalNotPageSize()
    {
        var airports = Enumerable.Range(1, 5).Select(i => Airport($"A{i}")).ToList();
        var flights = Enumerable.Range(1, 5).Select(i => Flight($"F{i}")).ToList();
        var sut = CreateSut(airports, flights);

        var response = await sut.SearchAsync("x", offset: 0, limit: 3, CancellationToken.None);

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(10, response.Count);
    }

    [Fact]
    public async Task SearchAsync_OffsetBeyondResults_ReturnsEmptyItemsWithRealCount()
    {
        var sut = CreateSut([Airport("MXP")], [Flight("AZ100")]);

        var response = await sut.SearchAsync("mi", offset: 50, limit: 10, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(2, response.Count);
        Assert.Equal(50, response.Offset);
    }

    [Fact]
    public async Task SearchAsync_NormalizesQueryBeforeDelegatingToCaches()
    {
        var airportsCache = new Mock<IAirportsSearchCache>();
        airportsCache.Setup(c => c.GetMatchesAsync("mxp", It.IsAny<CancellationToken>())).ReturnsAsync(new List<AirportDto>());
        var flightsCache = new Mock<IFlightsSearchCache>();
        flightsCache.Setup(c => c.GetMatchesAsync("mxp", It.IsAny<CancellationToken>())).ReturnsAsync(new List<FlightDto>());

        var sut = new RealGlobalSearchService(airportsCache.Object, flightsCache.Object);

        await sut.SearchAsync("  MXP  ", offset: 0, limit: 10, CancellationToken.None);

        airportsCache.Verify(c => c.GetMatchesAsync("mxp", It.IsAny<CancellationToken>()), Times.Once);
        flightsCache.Verify(c => c.GetMatchesAsync("mxp", It.IsAny<CancellationToken>()), Times.Once);
    }
}
