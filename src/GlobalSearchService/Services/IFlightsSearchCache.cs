using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

/// <summary>
/// Cache interna dedicata a Flights (vedi "Strategia di caching per fonte" in
/// docs/architecture.md): nessuna garanzia di coerenza tra chiamate diverse a
/// FlightsService, quindi ogni nuova ricerca rifa' per intero lo sweep+filtro; il
/// risultato filtrato viene cacheato SOLO sotto la query stessa (mai il superset grezzo),
/// cosi' che paginazione e dettaglio della STESSA ricerca (stessa query, offset diversi)
/// riusino il risultato gia' calcolato invece di rifare lo sweep ad ogni richiesta.
/// </summary>
public interface IFlightsSearchCache
{
    /// <summary>
    /// Restituisce i voli il cui codice, aeromobile, citta' o aeroporto di
    /// partenza/arrivo contengono <paramref name="normalizedQuery"/> (case-insensitive).
    /// </summary>
    Task<IReadOnlyList<FlightDto>> GetMatchesAsync(string normalizedQuery, CancellationToken cancellationToken);
}
