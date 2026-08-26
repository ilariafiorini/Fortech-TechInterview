using System.Text.Json;
using FlightsService.Grpc;
using GlobalSearchService.Models;
using GlobalSearchService.Services;
using GlobalSearchService.Tests.TestDoubles;
using Grpc.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Flights = FlightsService.Grpc.Flights;

namespace GlobalSearchService.Tests.Services;

/// <summary>
/// Verifica FlightsSearchCache: cache-aside pura chiavata sulla sola query (nessun riuso
/// per sottostringa, a differenza di Airports — vedi docs/architecture.md). Il client gRPC
/// generato viene mockato secondo il pattern documentato da Microsoft per testare client
/// gRPC (Mock&lt;TClient&gt; + AsyncUnaryCall costruita a mano): nessun FlightsService reale
/// necessario.
/// </summary>
public class FlightsSearchCacheTests
{
    private static readonly GlobalSearchCacheOptions CacheOptions = new();

    private static List<Flight> SampleFlights() =>
    [
        new Flight { Id = "AZ100", AircraftNumber = "N1", DepartureAirportCode = "MXP", ArrivalAirportCode = "JFK", DepartureCity = "Milano", ArrivalCity = "New York" },
        new Flight { Id = "AZ200", AircraftNumber = "N2", DepartureAirportCode = "FCO", ArrivalAirportCode = "JFK", DepartureCity = "Roma", ArrivalCity = "New York" },
        new Flight { Id = "LH300", AircraftNumber = "N3", DepartureAirportCode = "MUC", ArrivalAirportCode = "MXP", DepartureCity = "Monaco", ArrivalCity = "Milano" }
    ];

    private static GetFlightsResponse BuildPage(IReadOnlyList<Flight> all, int offset, int limit)
    {
        var response = new GetFlightsResponse { Offset = offset, Limit = limit, TotalCount = all.Count };
        response.Flights.AddRange(all.Skip(offset).Take(limit));
        return response;
    }

    private static AsyncUnaryCall<TResponse> CreateAsyncUnaryCall<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static Mock<Flights.FlightsClient> CreateMockClient(IReadOnlyList<Flight> allFlights)
    {
        var mockClient = new Mock<Flights.FlightsClient>();

        mockClient
            .Setup(c => c.GetFlightsAsync(It.IsAny<GetFlightsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns((GetFlightsRequest request, Metadata? _, DateTime? _, CancellationToken _) =>
                CreateAsyncUnaryCall(BuildPage(allFlights, request.Offset, request.Limit)));

        return mockClient;
    }

    private static FlightsSearchCache CreateSut(Mock<Flights.FlightsClient> mockClient, FakeDistributedCache cache, int pageLimit) =>
        new(mockClient.Object, cache, Options.Create(CacheOptions), Options.Create(new SearchFanOutOptions { PageLimit = pageLimit }));

    [Fact]
    public async Task GetMatchesAsync_CacheHit_DoesNotCallGrpc()
    {
        var allFlights = SampleFlights();
        var mockClient = CreateMockClient(allFlights);
        var cache = new FakeDistributedCache();
        var cachedFlight = new List<FlightDto> { FlightDto.FromGrpc(allFlights[0]) };
        await cache.SetStringAsync("flightssearch:results:az100", JsonSerializer.Serialize(cachedFlight), CancellationToken.None);

        var sut = CreateSut(mockClient, cache, pageLimit: 2);

        var result = await sut.GetMatchesAsync("az100", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("AZ100", result[0].Id);
        mockClient.Verify(c => c.GetFlightsAsync(It.IsAny<GetFlightsRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMatchesAsync_CacheMiss_SweepsAllPagesFiltersAndCaches()
    {
        var allFlights = SampleFlights(); // 3 voli, pageLimit=2 -> 2 chiamate gRPC
        var mockClient = CreateMockClient(allFlights);
        var cache = new FakeDistributedCache();

        var sut = CreateSut(mockClient, cache, pageLimit: 2);

        var result = await sut.GetMatchesAsync("milano", CancellationToken.None);

        // AZ100 (parte da Milano) e LH300 (arriva a Milano) contengono "Milano" in una citta'
        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.Id == "AZ100");
        Assert.Contains(result, f => f.Id == "LH300");

        mockClient.Verify(c => c.GetFlightsAsync(It.IsAny<GetFlightsRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));

        var cachedJson = await cache.GetStringAsync("flightssearch:results:milano", CancellationToken.None);
        Assert.NotNull(cachedJson);
    }

    [Fact]
    public async Task GetMatchesAsync_MatchesAcrossAllSearchableFields()
    {
        var allFlights = SampleFlights();
        var mockClient = CreateMockClient(allFlights);
        var cache = new FakeDistributedCache();
        var sut = CreateSut(mockClient, cache, pageLimit: 10); // 1 sola chiamata copre tutto

        var byArrivalCode = await sut.GetMatchesAsync("jfk", CancellationToken.None);
        Assert.Equal(2, byArrivalCode.Count); // AZ100 e AZ200 arrivano entrambi a JFK

        var byAircraft = await sut.GetMatchesAsync("n3", CancellationToken.None);
        Assert.Single(byAircraft);
        Assert.Equal("LH300", byAircraft[0].Id);
    }
}
