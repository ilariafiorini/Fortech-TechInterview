using System.Net.Http.Json;
using System.Text.Json;
using GlobalSearchService.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GlobalSearchService.Services;

/// <summary>
/// Implementazione Redis di IAirportsSearchCache. Vedi la voce "Airports" sotto
/// "Strategia di caching per fonte" in docs/architecture.md per l'algoritmo completo:
/// seeding lazy della chiave "" (l'intero superset), esplorazione della sottostringa
/// cacheata piu' lunga compatibile con la query, rinnovo della TTL solo della entry
/// sorgente effettivamente usata (gratis: IDistributedCache su Redis rinnova la scadenza
/// sliding ad ogni lettura andata a buon fine, stesso comportamento gia' sfruttato da
/// CachingGlobalSearchService).
///
/// Semplificazione nota (accettabile per un prototipo): se tra due chiamate una entry
/// sorgente diversa da "" scade esattamente nella finestra tra la scansione delle chiavi
/// note e la lettura del suo valore, si ricade su un fetch completo da AirportsService
/// invece di provare la successiva candidata piu' corta. Corretto ma non ottimale in
/// quel caso raro; vedi anche la discussione sul "cache stampede" per query concorrenti
/// sulla stessa chiave, non affrontata qui con un lock distribuito.
/// </summary>
public class AirportsSearchCache : IAirportsSearchCache
{
    private const string ResultsKeyPrefix = "airportssearch:results:";
    private const string KnownKeysSetKey = "airportssearch:known-keys";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly GlobalSearchCacheOptions _cacheOptions;
    private readonly SearchFanOutOptions _fanOutOptions;

    public AirportsSearchCache(
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        IConnectionMultiplexer redis,
        IOptions<GlobalSearchCacheOptions> cacheOptions,
        IOptions<SearchFanOutOptions> fanOutOptions)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _redis = redis;
        _cacheOptions = cacheOptions.Value;
        _fanOutOptions = fanOutOptions.Value;
    }

    public async Task<IReadOnlyList<AirportDto>> GetMatchesAsync(string normalizedQuery, CancellationToken cancellationToken)
    {
        // Percorso veloce: hit esatto su questa query, gia' cacheata in precedenza.
        var exact = await _cache.GetStringAsync(ResultsKeyPrefix + normalizedQuery, cancellationToken);
        if (exact is not null)
        {
            return Deserialize(exact);
        }

        var db = _redis.GetDatabase();
        var knownKeys = await db.SetMembersAsync(KnownKeysSetKey);

        // Tra le chiavi gia' cacheate che sono sottostringa della query richiesta (compresa
        // "" se gia' seminata — essendo vuota e' sempre sottostringa di qualunque cosa),
        // scegliamo la piu' lunga: e' il sottogruppo gia' pronto piu' vicino a quanto serve.
        var sourceKey = knownKeys
            .Select(v => v.ToString())
            .Where(known => normalizedQuery.Contains(known, StringComparison.Ordinal))
            .OrderByDescending(known => known.Length)
            .FirstOrDefault();

        List<AirportDto> sourceList;

        if (sourceKey is null)
        {
            // Nessuna chiave utilizzabile, nemmeno "": primo utilizzo, oppure "" e' scaduta
            // per inattivita' prolungata. Seminiamo "" con l'intero superset in un solo
            // fetch, riusato subito anche per rispondere alla query corrente.
            sourceList = await FetchAllAirportsAsync(cancellationToken);
            await StoreAsync(string.Empty, sourceList, cancellationToken);
        }
        else
        {
            var cachedJson = await _cache.GetStringAsync(ResultsKeyPrefix + sourceKey, cancellationToken);
            if (cachedJson is null)
            {
                // TTL scaduta su Redis ma il set known-keys non lo sapeva ancora: puliamo e
                // ricadiamo sul fetch completo (vedi nota sulla semplificazione in testa al file).
                await db.SetRemoveAsync(KnownKeysSetKey, sourceKey);
                sourceList = await FetchAllAirportsAsync(cancellationToken);
                await StoreAsync(string.Empty, sourceList, cancellationToken);
            }
            else
            {
                // La lettura sopra ha gia' rinnovato la TTL sliding della entry sorgente:
                // nessun codice dedicato necessario per "rinnova solo la entry usata".
                sourceList = Deserialize(cachedJson);
            }
        }

        var matches = sourceList.Where(a => MatchesQuery(a, normalizedQuery)).ToList();
        await StoreAsync(normalizedQuery, matches, cancellationToken);

        return matches;
    }

    private static bool MatchesQuery(AirportDto airport, string normalizedQuery) =>
        Contains(airport.Id, normalizedQuery) ||
        Contains(airport.Name, normalizedQuery) ||
        Contains(airport.City, normalizedQuery) ||
        Contains(airport.Country, normalizedQuery);

    private static bool Contains(string? value, string normalizedQuery) =>
        value is not null && value.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);

    private async Task StoreAsync(string key, List<AirportDto> items, CancellationToken cancellationToken)
    {
        var entryOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_cacheOptions.SlidingExpirationMinutes),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheOptions.AbsoluteExpirationMinutes)
        };

        await _cache.SetStringAsync(ResultsKeyPrefix + key, JsonSerializer.Serialize(items), entryOptions, cancellationToken);

        var db = _redis.GetDatabase();
        await db.SetAddAsync(KnownKeysSetKey, key);
    }

    private static List<AirportDto> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<AirportDto>>(json) ?? [];

    /// <summary>
    /// Scarica l'intero elenco aeroporti da AirportsService con piu' chiamate GetAirports in
    /// parallelo (stesso schema usato per FlightsService — vedi SearchFanOutOptions per il
    /// limit). Il totale non e' hardcoded: la prima chiamata rivela totalCount, da cui si
    /// calcolano le pagine restanti.
    /// </summary>
    private async Task<List<AirportDto>> FetchAllAirportsAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("airports");
        var pageLimit = Math.Max(1, _fanOutOptions.PageLimit);

        var first = await client.GetFromJsonAsync<AirportsPageResponse>(
            $"/api/airports?offset=0&limit={pageLimit}", cancellationToken) ?? new AirportsPageResponse();

        var all = new List<AirportDto>(first.Items);

        var remainingOffsets = new List<int>();
        for (var offset = pageLimit; offset < first.TotalCount; offset += pageLimit)
        {
            remainingOffsets.Add(offset);
        }

        if (remainingOffsets.Count > 0)
        {
            var pages = await Task.WhenAll(remainingOffsets.Select(offset =>
                client.GetFromJsonAsync<AirportsPageResponse>($"/api/airports?offset={offset}&limit={pageLimit}", cancellationToken)));

            foreach (var page in pages)
            {
                if (page is not null)
                {
                    all.AddRange(page.Items);
                }
            }
        }

        return all;
    }
}
