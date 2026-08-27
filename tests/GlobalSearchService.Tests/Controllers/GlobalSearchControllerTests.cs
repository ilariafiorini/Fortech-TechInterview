using GlobalSearchService.Controllers;
using GlobalSearchService.Models;
using GlobalSearchService.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace GlobalSearchService.Tests.Controllers;

/// <summary>
/// Test sulle due azioni di GlobalSearchController che leggono il dettaglio dalla cache di
/// ricerca (GetCachedAirportById/GetCachedFlightById), introdotte per far si' che il
/// dettaglio mostri esattamente la riga vista in lista invece di un'istantanea nuova presa
/// al momento del click (vedi docs/architecture.md). IAirportsSearchCache/
/// IFlightsSearchCache sono mockate direttamente: qui si verifica solo la logica del
/// controller (validazione della query, ricerca dell'id nell'elenco, 400/404/200), non il
/// comportamento delle cache stesse (gia' coperto da AirportsSearchCacheTests/
/// FlightsSearchCacheTests).
/// </summary>
public class GlobalSearchControllerTests
{
    private static GlobalSearchController CreateSut(
        out Mock<IAirportsSearchCache> airportsCache,
        out Mock<IFlightsSearchCache> flightsCache)
    {
        airportsCache = new Mock<IAirportsSearchCache>();
        flightsCache = new Mock<IFlightsSearchCache>();
        var searchService = new Mock<IGlobalSearchService>();

        return new GlobalSearchController(searchService.Object, airportsCache.Object, flightsCache.Object);
    }

    [Fact]
    public async Task GetCachedAirportById_QueryMissing_ReturnsBadRequest()
    {
        var sut = CreateSut(out _, out _);

        var result = await sut.GetCachedAirportById("AP0001", query: null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCachedAirportById_QueryTooShort_ReturnsBadRequest()
    {
        var sut = CreateSut(out _, out _);

        var result = await sut.GetCachedAirportById("AP0001", query: "ab", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCachedAirportById_IdNotAmongCachedMatches_ReturnsNotFound()
    {
        var sut = CreateSut(out var airportsCache, out _);
        airportsCache
            .Setup(c => c.GetMatchesAsync("milano", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AirportDto> { new() { Id = "AP0002", Name = "Linate", City = "Milano", Country = "Italia" } });

        var result = await sut.GetCachedAirportById("AP0001", query: "Milano", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCachedAirportById_IdFoundCaseInsensitive_ReturnsOkWithMatch()
    {
        var sut = CreateSut(out var airportsCache, out _);
        var expected = new AirportDto { Id = "AP0001", Name = "Malpensa", City = "Milano", Country = "Italia" };
        airportsCache
            .Setup(c => c.GetMatchesAsync("milano", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AirportDto> { expected });

        // L'id nella route arriva minuscolo: deve comunque combaciare (OrdinalIgnoreCase).
        var result = await sut.GetCachedAirportById("ap0001", query: "Milano", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task GetCachedFlightById_QueryMissing_ReturnsBadRequest()
    {
        var sut = CreateSut(out _, out _);

        var result = await sut.GetCachedFlightById("FL0001", query: null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCachedFlightById_QueryTooShort_ReturnsBadRequest()
    {
        var sut = CreateSut(out _, out _);

        var result = await sut.GetCachedFlightById("FL0001", query: "ab", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCachedFlightById_IdNotAmongCachedMatches_ReturnsNotFound()
    {
        var sut = CreateSut(out _, out var flightsCache);
        flightsCache
            .Setup(c => c.GetMatchesAsync("milano", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FlightDto>());

        var result = await sut.GetCachedFlightById("FL0001", query: "Milano", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCachedFlightById_IdFoundCaseInsensitive_ReturnsOkWithMatchIncludingTimes()
    {
        var sut = CreateSut(out _, out var flightsCache);
        var expected = new FlightDto
        {
            Id = "FL0001",
            AircraftNumber = "A320",
            DepartureAirportCode = "FLR",
            ArrivalAirportCode = "BER",
            DepartureCity = "Firenze",
            ArrivalCity = "Berlino",
            DepartureTime = "2026-09-05T15:53:03Z",
            ArrivalTime = "2026-09-13T05:43:03Z"
        };
        flightsCache
            .Setup(c => c.GetMatchesAsync("milano", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FlightDto> { expected });

        var result = await sut.GetCachedFlightById("fl0001", query: "Milano", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var match = Assert.IsType<FlightDto>(ok.Value);
        Assert.Equal(expected.DepartureTime, match.DepartureTime);
        Assert.Equal(expected.ArrivalTime, match.ArrivalTime);
    }
}
