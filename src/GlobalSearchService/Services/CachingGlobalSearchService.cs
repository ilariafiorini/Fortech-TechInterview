using System.Text.Json;
using GlobalSearchService.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GlobalSearchService.Services;

/// <summary>
/// Decorator che aggiunge una cache Redis davanti a un'implementazione di IGlobalSearchService,
/// senza che quest'ultima debba sapere nulla della cache (vedi anche docs/architecture.md).
///
/// Strategia:
///  - Ogni query viene normalizzata (trim + lowercase) e usata come chiave.
///  - Ogni valore di resourceType (null/"airport"/"flight") ha il proprio "bucket" cache
///    indipendente (vedi ResultsKey/KnownQueriesSetKey): una ricerca globale e una filtrata
///    sulla stessa query testuale non condividono ne' i risultati cacheati ne' il set delle
///    query note, perche' rappresentano insiemi diversi (e quindi conteggi/paginazioni
///    diversi) — indispensabile perche' le tre schede Globale/Voli/Aeroporti della UI
///    (Search.razor) restituiscano ciascuna un count corretto sul proprio sottoinsieme.
///  - Se la query richiesta CONTIENE come sottostringa una query gia' cacheata NELLO STESSO
///    bucket, i risultati della query nuova sono per costruzione un sottoinsieme di quelli
///    gia' cacheati (il matching e' per "contiene"): si riusa quel set completo gia'
///    scaricato, filtrando ulteriormente in memoria, senza richiamare il livello sottostante
///    (AirportsService/FlightsService). Questo copre anche il caso di hit esatto: una
///    stringa contiene sempre se stessa.
///  - Se nessuna query cacheata nel bucket e' una sottostringa di quella richiesta, si
///    interroga il livello sottostante una sola volta chiedendo un limit molto ampio (vedi
///    MaxUnderlyingFetch) per ottenere l'insieme "completo" dei match, che viene poi
///    cacheato e paginato in memoria.
///  - Un SET Redis per bucket (KnownQueriesSetKey) tiene traccia di quali query sono
///    attualmente cacheate in quel bucket, per poter fare lo scan delle sottostringhe. Se
///    una entry e' scaduta (TTL naturale, gestito da Redis) la rimuoviamo pigramente dal
///    set alla prima lettura fallita: nessun processo di pulizia dedicato, la coerenza si
///    ristabilisce da sola nel tempo.
///  - Scadenza: sliding (si rinnova a ogni lettura, gestito automaticamente da IDistributedCache
///    per l'implementazione Redis) con un tetto assoluto — vedi GlobalSearchCacheOptions.
///  - Effetto collaterale utile e non gestito con codice dedicato: una query gia' nota per avere
///    ZERO risultati produce automaticamente zero risultati anche per ogni query piu' specifica
///    che la contiene, senza interrogare nulla a valle (nello stesso bucket).
///
/// NOTA: il filtro applicato quando si riusa un set cacheato (vedi TryGetFromCachedSupersetAsync)
/// controlla solo il campo Description, perche' e' l'unico campo testuale che SearchResultItem
/// espone oggi — invariato rispetto a prima dell'introduzione di resourceType.
///
/// NOTA sulla compatibilita': introdurre il bucket cambia il formato delle chiavi Redis
/// (da "globalsearch:results:&lt;query&gt;" a "globalsearch:results:&lt;bucket&gt;:&lt;query&gt;"):
/// le entry scritte da versioni precedenti restano semplicemente orfane fino a scadere per
/// TTL naturale, nessuna migrazione necessaria per un ambiente di sviluppo/prototipo.
/// </summary>
public class CachingGlobalSearchService : IGlobalSearchService
{
    private const string ResultsKeyPrefix = "globalsearch:results:";
    private const string KnownQueriesSetKeyPrefix = "globalsearch:known-queries:";

    // Limite "pratico" usato per approssimare una query "senza paginazione" verso il livello
    // sottostante. Per il dataset di questo esercizio (centinaia/poche migliaia di elementi) e'
    // piu' che sufficiente. Un'implementazione con dataset molto piu' grandi dovrebbe sostituirlo
    // con un vero metodo di fetch completo, non un limit enorme sul metodo paginato pubblico.
    private const int MaxUnderlyingFetch = 2000;

    private readonly IGlobalSearchService _inner;
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly GlobalSearchCacheOptions _options;

    public CachingGlobalSearchService(
        IGlobalSearchService inner,
        IDistributedCache cache,
        IConnectionMultiplexer redis,
        IOptions<GlobalSearchCacheOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _redis = redis;
        _options = options.Value;
    }

    public async Task<GlobalSearchResponse> SearchAsync(string query, int offset, int limit, string? resourceType, CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim().ToLowerInvariant();
        var bucket = string.IsNullOrEmpty(resourceType) ? "all" : resourceType;

        var fullSet = await TryGetFromCachedSupersetAsync(bucket, normalizedQuery, cancellationToken)
                      ?? await FetchFullSetAsync(normalizedQuery, resourceType, cancellationToken);

        // Cachiamo sempre anche sotto la query attuale: se il risultato arriva gia' da un
        // superset cacheato, questo permette a query future ancora piu' specifiche (o identiche)
        // di trovare un hit diretto senza dover ripetere il filtro sul superset.
        await CacheFullSetAsync(bucket, normalizedQuery, fullSet, cancellationToken);

        return Paginate(fullSet, offset, limit);
    }

    private async Task<List<SearchResultItem>?> TryGetFromCachedSupersetAsync(string bucket, string normalizedQuery, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var knownQueriesKey = KnownQueriesSetKey(bucket);
        var knownQueries = await db.SetMembersAsync(knownQueriesKey);

        // Tra tutte le query gia' cacheate (nello stesso bucket) che sono sottostringa di
        // quella richiesta, proviamo prima la piu' lunga: e' quella che lascia meno elementi
        // da ri-filtrare in memoria.
        var candidates = knownQueries
            .Select(v => v.ToString())
            .Where(known => known.Length > 0 && normalizedQuery.Contains(known, StringComparison.Ordinal))
            .OrderByDescending(known => known.Length);

        foreach (var candidate in candidates)
        {
            var cachedJson = await _cache.GetStringAsync(ResultsKey(bucket, candidate), ct);
            if (cachedJson is null)
            {
                // TTL scaduta su Redis, ma il set "known queries" non lo sapeva ancora: puliamo.
                await db.SetRemoveAsync(knownQueriesKey, candidate);
                continue;
            }

            var cachedItems = JsonSerializer.Deserialize<List<SearchResultItem>>(cachedJson) ?? [];

            return cachedItems
                .Where(item => item.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return null;
    }

    private async Task<List<SearchResultItem>> FetchFullSetAsync(string normalizedQuery, string? resourceType, CancellationToken ct)
    {
        var response = await _inner.SearchAsync(normalizedQuery, offset: 0, limit: MaxUnderlyingFetch, resourceType, ct);
        return response.Items.ToList();
    }

    private async Task CacheFullSetAsync(string bucket, string normalizedQuery, List<SearchResultItem> fullSet, CancellationToken ct)
    {
        var entryOptions = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_options.SlidingExpirationMinutes),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.AbsoluteExpirationMinutes)
        };

        await _cache.SetStringAsync(ResultsKey(bucket, normalizedQuery), JsonSerializer.Serialize(fullSet), entryOptions, ct);

        var db = _redis.GetDatabase();
        await db.SetAddAsync(KnownQueriesSetKey(bucket), normalizedQuery);
    }

    private static string ResultsKey(string bucket, string query) => $"{ResultsKeyPrefix}{bucket}:{query}";

    private static string KnownQueriesSetKey(string bucket) => $"{KnownQueriesSetKeyPrefix}{bucket}";

    private static GlobalSearchResponse Paginate(List<SearchResultItem> fullSet, int offset, int limit)
    {
        return new GlobalSearchResponse
        {
            Items = fullSet.Skip(offset).Take(limit).ToList(),
            Offset = offset,
            Limit = limit,
            Count = fullSet.Count
        };
    }
}
