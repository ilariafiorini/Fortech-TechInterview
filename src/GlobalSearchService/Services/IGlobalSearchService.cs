using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

public interface IGlobalSearchService
{
    /// <summary>
    /// <paramref name="resourceType"/>: null/vuoto per la ricerca globale (aeroporti e voli
    /// insieme, ordine Airports-poi-Flights), "airport" o "flight" per limitarsi a una sola
    /// fonte — usato dalla UI per le schede Globale/Voli/Aeroporti (vedi Search.razor e
    /// docs/architecture.md). La validazione del valore (solo questi tre casi ammessi)
    /// avviene nel controller, non qui.
    /// </summary>
    Task<GlobalSearchResponse> SearchAsync(string query, int offset, int limit, string? resourceType, CancellationToken cancellationToken);
}
