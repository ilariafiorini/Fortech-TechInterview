using System.Text.Json;
using GlobalSearchService.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Flights = FlightsService.Grpc.Flights;

namespace GlobalSearchService.Services;

/// <summary>
/// Implementazione basata su Redis (IDistributedCache) di IFlightsSearchCache. Cache-aside
/// pura, chiavata sulla sola query: nessun riuso per sottostringa (a differenza di
/// Airports), perche' non c'e' alcuna garanzia che il superset dei voli resti coerente da
/// una ricerca all'altra — vedi la voce "Flights" sotto "Strategia di caching per fonte" in
/// docs/architecture.md.
/// </summary>
public class FlightsSearchCache : IFlightsSearchCache
{
    private const string ResultsKeyPrefix = "flightssearch:results:";

    private readonly Flights.FlightsClient _flightsClient;
    private readonly IDistributedCache _cache;
    private readonly GlobalSearchCacheOptions _cacheOptions;
    private readonly SearchFanOutOptions _fanOutOptions;

    public FlightsSearchCache(
        Flights.FlightsClient flightsClient,
        IDistributedCache cache,
        IOptions<GlobalSearchCacheOptions> cacheOptions,
        IOptions<SearchFanOutOptions> fanOutOptions)
    {
        _flightsClient = flightsClient;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
        _fanOutOptions = fanOutOptions.Value;
    }

    public async Task<IReadOnlyList<FlightDto>> GetMatchesAsync(string normalizedQuery, CancellationToken cancellationToken)
    {
        var cachedJson = await _cache.GetStringAsync(ResultsKeyPrefix + normalizedQuery, cancellationToken);
        if (cachedJson is not null)
        {
            // Stessa query di una ricerca gia' cristallizzata: e' navigazione (paginazione o
            // dettaglio), non una nuova ricerca. La lettura sopra ha gia' rinnovato la TTL.
            return Deserialize(cachedJson);
        }

        // Query mai vista (o scaduta): e' una nuova ricerca, si rifa' per intero lo sweep.
        var allFlights = await FetchAllFlightsAsync(cancellationToken);
        var matches = allFlights.Where(f => MatchesQuery(f, normalizedQuery)).ToList();

        var entryOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_cacheOptions.SlidingExpirationMinutes),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheOptions.AbsoluteExpirationMinutes)
        };
        await _cache.SetStringAsync(ResultsKeyPrefix + normalizedQuery, JsonSerializer.Serialize(matches), entryOptions, cancellationToken);

        return matches;
    }

    private static bool MatchesQuery(FlightDto flight, string normalizedQuery) =>
        Contains(flight.Id, normalizedQuery) ||
        Contains(flight.AircraftNumber, normalizedQuery) ||
        Contains(flight.DepartureCity, normalizedQuery) ||
        Contains(flight.ArrivalCity, normalizedQuery) ||
        Contains(flight.DepartureAirportCode, normalizedQuery) ||
        Contains(flight.ArrivalAirportCode, normalizedQuery);

    private static bool Contains(string? value, string normalizedQuery) =>
        value is not null && value.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);

    private static List<FlightDto> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<FlightDto>>(json) ?? [];

    /// <summary>
    /// Sweep completo di tutti i voli con piu' chiamate GetFlights in parallelo — vedi
    /// tools/Test-FlightsServiceLatency.ps1 e la voce "Ampiezza del fan-out" in
    /// docs/architecture.md per come e' stato scelto il limit di default.
    /// </summary>
    private async Task<List<FlightDto>> FetchAllFlightsAsync(CancellationToken cancellationToken)
    {
        var pageLimit = Math.Max(1, _fanOutOptions.PageLimit);

        var first = await _flightsClient.GetFlightsAsync(
            new FlightsService.Grpc.GetFlightsRequest { Offset = 0, Limit = pageLimit },
            cancellationToken: cancellationToken);

        var all = new List<FlightDto>(first.Flights.Select(FlightDto.FromGrpc));

        var remainingOffsets = new List<int>();
        for (var offset = pageLimit; offset < first.TotalCount; offset += pageLimit)
        {
            remainingOffsets.Add(offset);
        }

        if (remainingOffsets.Count > 0)
        {
            var calls = remainingOffsets.Select(offset => _flightsClient.GetFlightsAsync(
                new FlightsService.Grpc.GetFlightsRequest { Offset = offset, Limit = pageLimit },
                cancellationToken: cancellationToken).ResponseAsync);

            var pages = await Task.WhenAll(calls);

            foreach (var page in pages)
            {
                all.AddRange(page.Flights.Select(FlightDto.FromGrpc));
            }
        }

        return all;
    }
}
