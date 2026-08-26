using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace GlobalSearchService.Tests.TestDoubles;

/// <summary>
/// Sostituto in memoria di IDistributedCache: nessuna scadenza reale (le
/// DistributedCacheEntryOptions passate vengono ignorate), solo lettura/scrittura per
/// chiave, cosi' i test su AirportsSearchCache/FlightsSearchCache non hanno bisogno di un
/// Redis vero. Le estensioni GetStringAsync/SetStringAsync usate dal codice di produzione
/// (e comodamente anche dai test per pre-seminare la cache) passano attraverso i metodi
/// byte[] qui sotto, esattamente come farebbero con l'implementazione Redis reale.
/// </summary>
internal sealed class FakeDistributedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public byte[]? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        Task.FromResult(Get(key));

    public void Refresh(string key)
    {
        // Nessuna scadenza simulata: non c'e' nulla da rinfrescare.
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _store.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }
}
