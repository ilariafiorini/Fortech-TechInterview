using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

/// <summary>
/// PROTOTIPO DI STUDIO — implementazione reale della Global Search API, per il branch
/// prototype/real-search (vedi docs/architecture.md per tutte le decisioni di design che
/// la motivano). Non e' pensata per essere la consegna finale cosi' com'e': serve da
/// riferimento concreto di come le decisioni discusse si traducono in codice, da
/// riscrivere/adattare a mano sul serio.
///
/// Aggrega Airports (REST, cache con riuso per sottostringa) e Flights (gRPC, cache
/// "crystallize-per-ricerca"), nell'ordine Airports-poi-Flights indicato dal prototipo in
/// README.md, con paginazione stitchata sull'elenco concatenato. Un offset/limit oltre la
/// fine dei risultati non e' un errore: restituisce semplicemente items vuoto e count sul
/// totale reale (vedi "Paginazione oltre la fine dei risultati" in docs/architecture.md) —
/// e' il comportamento naturale di Skip/Take, nessun codice speciale necessario.
///
/// <paramref name="resourceType"/> (null/"airport"/"flight", validato a monte dal
/// controller) permette di limitarsi a una sola fonte — usato dalle schede Voli/Aeroporti
/// della UI (vedi Search.razor). Quando una fonte e' esclusa, il suo fetch viene saltato
/// del tutto (non solo filtrato a valle): niente chiamata inutile ad Airports/FlightsService
/// per dati che verrebbero comunque scartati.
/// </summary>
public class RealGlobalSearchService : IGlobalSearchService
{
    private readonly IAirportsSearchCache _airportsCache;
    private readonly IFlightsSearchCache _flightsCache;

    public RealGlobalSearchService(IAirportsSearchCache airportsCache, IFlightsSearchCache flightsCache)
    {
        _airportsCache = airportsCache;
        _flightsCache = flightsCache;
    }

    public async Task<GlobalSearchResponse> SearchAsync(string query, int offset, int limit, string? resourceType, CancellationToken cancellationToken)
    {
        // Difensivo: ci si aspetta di ricevere gia' una query normalizzata (trim+lowercase)
        // da CachingGlobalSearchService, ma normalizzare di nuovo qui e' innocuo e rende
        // questa classe corretta anche se richiamata in altri modi in futuro.
        var normalizedQuery = query.Trim().ToLowerInvariant();

        var includeAirports = resourceType is null or "airport";
        var includeFlights = resourceType is null or "flight";

        var airportsTask = includeAirports
            ? _airportsCache.GetMatchesAsync(normalizedQuery, cancellationToken)
            : Task.FromResult<IReadOnlyList<AirportDto>>(Array.Empty<AirportDto>());

        var flightsTask = includeFlights
            ? _flightsCache.GetMatchesAsync(normalizedQuery, cancellationToken)
            : Task.FromResult<IReadOnlyList<FlightDto>>(Array.Empty<FlightDto>());

        await Task.WhenAll(airportsTask, flightsTask);

        var items = airportsTask.Result.Select(ProjectAirport)
            .Concat(flightsTask.Result.Select(ProjectFlight))
            .ToList();

        return new GlobalSearchResponse
        {
            Items = items.Skip(offset).Take(limit).ToList(),
            Offset = offset,
            Limit = limit,
            Count = items.Count
        };
    }

    private static SearchResultItem ProjectAirport(AirportDto a) => new()
    {
        Id = a.Id,
        ResourceType = "airport",
        Description = $"{a.Id} - {a.Name} ({a.Country})"
    };

    private static SearchResultItem ProjectFlight(FlightDto f) => new()
    {
        Id = f.Id,
        ResourceType = "flight",
        Description = $"{f.Id} - {f.DepartureCity} -> {f.ArrivalCity}"
    };
}
