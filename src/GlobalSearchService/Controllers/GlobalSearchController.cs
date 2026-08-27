using GlobalSearchService.Models;
using GlobalSearchService.Services;
using Microsoft.AspNetCore.Mvc;

namespace GlobalSearchService.Controllers;

[ApiController]
[Route("api/global-search")]
public class GlobalSearchController : ControllerBase
{
    private readonly IGlobalSearchService _searchService;
    private readonly IAirportsSearchCache _airportsCache;
    private readonly IFlightsSearchCache _flightsCache;

    public GlobalSearchController(
        IGlobalSearchService searchService,
        IAirportsSearchCache airportsCache,
        IFlightsSearchCache flightsCache)
    {
        _searchService = searchService;
        _airportsCache = airportsCache;
        _flightsCache = flightsCache;
    }

    [HttpGet]
    public async Task<ActionResult> Search(
        [FromQuery] string query,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 10,
        [FromQuery] string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            return BadRequest(new { error = "Il parametro 'query' deve contenere almeno 3 caratteri." });
        }

        // resourceType: null/assente = ricerca globale (comportamento invariato); "airport"
        // o "flight" limitano il risultato a una sola fonte, con paginazione/conteggio
        // corretti su quel sottoinsieme — usato dalle schede Voli/Aeroporti della UI (vedi
        // Search.razor). Qualunque altro valore e' un errore del chiamante, non un caso da
        // silenziare: meglio un 400 esplicito che un comportamento ambiguo.
        string? normalizedResourceType = null;
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            normalizedResourceType = resourceType.Trim().ToLowerInvariant();
            if (normalizedResourceType is not ("airport" or "flight"))
            {
                return BadRequest(new { error = "Il parametro 'resourceType', se presente, deve essere 'airport' oppure 'flight'." });
            }
        }

        offset = Math.Max(offset, 0);
        limit = limit <= 0 ? 10 : limit;
        limit = Math.Min(limit, 100);

        var result = await _searchService.SearchAsync(query.Trim(), offset, limit, normalizedResourceType, cancellationToken);

        return Ok(result);
    }

    // Le due azioni seguenti servono la pagina di dettaglio (AirportDetail.razor /
    // FlightDetail.razor) quando ci si arriva da una ricerca: invece di interrogare di
    // nuovo AirportsService/FlightsService dal vivo, rileggono la riga dalla STESSA cache
    // per query gia' popolata da una ricerca precedente (IAirportsSearchCache/
    // IFlightsSearchCache.GetMatchesAsync — quasi sempre un hit, essendo la stessa query
    // appena servita per la lista). Questo garantisce che il dettaglio mostri esattamente
    // cio' che la lista aveva mostrato, invece di un'istantanea nuova presa al momento del
    // click — indispensabile per Flights (il mock rigenera dati random ad ogni chiamata
    // gRPC), utile anche per Airports per non fare affidamento su quanto a lungo
    // AirportsService tenga i propri dati stabili, cosa che dall'esterno (senza guardarne
    // il codice sorgente) non si puo' dare per garantita a tempo indeterminato — vedi
    // docs/architecture.md.
    //
    // 'query' e' obbligatorio qui (a differenza del parametro opzionale in Search sopra):
    // senza di esso non c'e' alcuna cache da interrogare. Il chiamante (i due componenti
    // Blazor) lo invoca solo quando dispone di un contesto di ricerca, e ricade sulla
    // chiamata dal vivo al microservizio proprietario in ogni altro caso (accesso diretto o
    // da preferiti, oppure un 404 qui sotto se la query non conteneva quell'id).

    [HttpGet("airports/{id}")]
    public async Task<ActionResult<AirportDto>> GetCachedAirportById(
        string id,
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            return BadRequest(new { error = "Il parametro 'query' e' obbligatorio e deve contenere almeno 3 caratteri." });
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var matches = await _airportsCache.GetMatchesAsync(normalizedQuery, cancellationToken);
        var match = matches.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return NotFound(new { error = $"Nessun aeroporto con id '{id}' tra i risultati cacheati per questa query." });
        }

        return Ok(match);
    }

    [HttpGet("flights/{id}")]
    public async Task<ActionResult<FlightDto>> GetCachedFlightById(
        string id,
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            return BadRequest(new { error = "Il parametro 'query' e' obbligatorio e deve contenere almeno 3 caratteri." });
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var matches = await _flightsCache.GetMatchesAsync(normalizedQuery, cancellationToken);
        var match = matches.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return NotFound(new { error = $"Nessun volo con id '{id}' tra i risultati cacheati per questa query." });
        }

        return Ok(match);
    }
}
