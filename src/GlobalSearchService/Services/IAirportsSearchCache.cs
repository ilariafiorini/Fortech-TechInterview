using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

/// <summary>
/// Cache interna dedicata ad Airports (vedi "Strategia di caching per fonte" in
/// docs/architecture.md): superset completo cacheato sotto la chiave "" popolata in modo
/// lazy/auto-riparante, riuso per sottostringa (la piu' lunga tra quelle gia' cacheate),
/// stessa scadenza sliding+assoluta di tutte le altre entry — nessun caso speciale a TTL
/// infinito.
/// </summary>
public interface IAirportsSearchCache
{
    /// <summary>
    /// Restituisce gli aeroporti il cui codice, nome, citta' o nazione contengono
    /// <paramref name="normalizedQuery"/> (case-insensitive). La query si assume gia'
    /// normalizzata (trim + lowercase) da chi chiama.
    /// </summary>
    Task<IReadOnlyList<AirportDto>> GetMatchesAsync(string normalizedQuery, CancellationToken cancellationToken);
}
