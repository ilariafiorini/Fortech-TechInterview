using System.Text.Json;
using GlobalSearchService.Models;
using GlobalSearchService.Services;
using GlobalSearchService.Tests.TestDoubles;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GlobalSearchService.Tests.Services;

/// <summary>
/// Verifica l'algoritmo di caching lazy/self-healing descritto in docs/architecture.md
/// (voce "Airports" sotto "Strategia di caching per fonte"): seeding lazy di "", riuso
/// della sottostringa nota piu' lunga, e ripristino quando il set known-keys punta a una
/// entry ormai assente in cache. HTTP e Redis sono entrambi sostituiti da doppi di test:
/// nessuna chiamata di rete reale, nessun bisogno di Docker.
/// </summary>
public class AirportsSearchCacheTests
{
    private static readonly GlobalSearchCacheOptions CacheOptions = new();

    private static List<AirportDto> SampleAirports() =>
    [
        new() { Id = "MXP", Name = "Malpensa", City = "Milano", Country = "Italy" },
        new() { Id = "LIN", Name = "Linate", City = "Milano", Country = "Italy" },
        new() { Id = "FCO", Name = "Fiumicino", City = "Roma", Country = "Italy" }
    ];

    private static IHttpClientFactory CreateHttpClientFactory(FakeAirportsHttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://airports.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("airports")).Returns(client);
        return factory.Object;
    }

    private static AirportsSearchCache CreateSut(
        FakeAirportsHttpMessageHandler handler,
        FakeDistributedCache cache,
        HashSet<string> knownKeys,
        int pageLimit = 2) =>
        new(
            CreateHttpClientFactory(handler),
            cache,
            RedisKnownKeysTestFactory.Create(knownKeys),
            Options.Create(CacheOptions),
            Options.Create(new SearchFanOutOptions { PageLimit = pageLimit }));

    [Fact]
    public async Task GetMatchesAsync_ExactCacheHit_DoesNotCallHttp()
    {
        var handler = new FakeAirportsHttpMessageHandler(SampleAirports());
        var cache = new FakeDistributedCache();
        var expected = new List<AirportDto> { SampleAirports()[0] };
        await cache.SetStringAsync("airportssearch:results:mxp", JsonSerializer.Serialize(expected), CancellationToken.None);

        var sut = CreateSut(handler, cache, knownKeys: []);

        var result = await sut.GetMatchesAsync("mxp", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("MXP", result[0].Id);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetMatchesAsync_NoKnownKeys_FetchesAllAirportsAndSeedsEmptyKeyAlongsideQuery()
    {
        var airports = SampleAirports(); // 3 aeroporti, pageLimit=2 -> 2 chiamate HTTP
        var handler = new FakeAirportsHttpMessageHandler(airports);
        var cache = new FakeDistributedCache();
        var knownKeys = new HashSet<string>();

        var sut = CreateSut(handler, cache, knownKeys, pageLimit: 2);

        var result = await sut.GetMatchesAsync("milano", CancellationToken.None);

        Assert.Equal(2, handler.CallCount); // offset 0 e offset 2
        Assert.Equal(2, result.Count); // MXP e LIN sono a Milano
        Assert.True(knownKeys.Contains(string.Empty)); // "" seminata come superset
        Assert.True(knownKeys.Contains("milano"));

        var seededSuperset = await cache.GetStringAsync("airportssearch:results:", CancellationToken.None);
        Assert.NotNull(seededSuperset);
    }

    [Fact]
    public async Task GetMatchesAsync_LongestKnownSubstringIsReused_NoHttpCall()
    {
        var airports = SampleAirports();
        var handler = new FakeAirportsHttpMessageHandler(airports);
        var cache = new FakeDistributedCache();

        // "" contiene l'intero superset, "mi" contiene solo gli aeroporti di Milano: la
        // query "milano" deve preferire "mi" (piu' lunga) e non deve rifare l'HTTP.
        await cache.SetStringAsync("airportssearch:results:", JsonSerializer.Serialize(airports), CancellationToken.None);
        var milanAirports = airports.Where(a => a.City == "Milano").ToList();
        await cache.SetStringAsync("airportssearch:results:mi", JsonSerializer.Serialize(milanAirports), CancellationToken.None);
        var knownKeys = new HashSet<string> { "", "mi" };

        var sut = CreateSut(handler, cache, knownKeys);

        var result = await sut.GetMatchesAsync("milano", CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal("Milano", a.City));
    }

    [Fact]
    public async Task GetMatchesAsync_KnownKeyPointsToExpiredEntry_RefetchesAndSelfHeals()
    {
        var airports = SampleAirports();
        var handler = new FakeAirportsHttpMessageHandler(airports);
        var cache = new FakeDistributedCache();

        // "mi" e' nel set known-keys ma la entry corrispondente non e' (piu') in cache:
        // simula una TTL scaduta su Redis senza che il set known-keys lo sapesse ancora.
        var knownKeys = new HashSet<string> { "mi" };

        var sut = CreateSut(handler, cache, knownKeys, pageLimit: 2);

        var result = await sut.GetMatchesAsync("milano", CancellationToken.None);

        Assert.Equal(2, handler.CallCount); // fetch completo di ripristino
        Assert.False(knownKeys.Contains("mi")); // auto-pulizia della chiave morta
        Assert.True(knownKeys.Contains(string.Empty)); // reseeding di ""
        Assert.Equal(2, result.Count);
    }
}
