using GlobalSearchService.Models;
using GlobalSearchService.Services;
using GlobalSearchService.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GlobalSearchService.Tests.Services;

/// <summary>
/// Test sul decorator di cache CachingGlobalSearchService, con particolare attenzione alla
/// separazione per bucket (resourceType) introdotta insieme all'omonimo parametro su
/// /api/global-search: prima di questi test nessun test copriva direttamente questo
/// livello — RealGlobalSearchServiceTests verifica il livello sottostante (che
/// RealGlobalSearchService salti la fonte esclusa), non il comportamento della cache
/// stessa. IGlobalSearchService "inner" e' mockato per simulare RealGlobalSearchService:
/// ogni test decide cosa restituirebbe una ricerca "vera" per una data combinazione
/// (query, resourceType), e verifica come CachingGlobalSearchService la cachea e la
/// riusa — in particolare, che due bucket diversi non si "vedano" a vicenda.
/// </summary>
public class CachingGlobalSearchServiceTests
{
    private static CachingGlobalSearchService CreateSut(
        Mock<IGlobalSearchService> inner,
        out Dictionary<string, HashSet<string>> redisSets)
    {
        var cache = new FakeDistributedCache();
        var redis = MultiKeyRedisTestFactory.Create(out redisSets);
        var options = Options.Create(new GlobalSearchCacheOptions());

        return new CachingGlobalSearchService(inner.Object, cache, redis, options);
    }

    private static GlobalSearchResponse MakeResponse(params SearchResultItem[] items) => new()
    {
        Items = items,
        Offset = 0,
        Limit = 2000,
        Count = items.Length
    };

    [Fact]
    public async Task SearchAsync_ReturnsPaginatedItemsAndTotalCount()
    {
        var inner = new Mock<IGlobalSearchService>();
        var items = Enumerable.Range(0, 5)
            .Select(i => new SearchResultItem { Id = $"A{i}", ResourceType = "airport", Description = $"desc {i}" })
            .ToArray();

        inner.Setup(x => x.SearchAsync("milano", 0, 2000, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse(items));

        var sut = CreateSut(inner, out _);

        var result = await sut.SearchAsync("Milano", offset: 2, limit: 2, resourceType: null, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("A2", result.Items[0].Id);
        Assert.Equal("A3", result.Items[1].Id);
    }

    [Fact]
    public async Task SearchAsync_DifferentBuckets_DoNotShareCachedResultsForSameQueryText()
    {
        var inner = new Mock<IGlobalSearchService>();

        var allItems = new[]
        {
            new SearchResultItem { Id = "AP01", ResourceType = "airport", Description = "AP01 - Milano (Italia)" },
            new SearchResultItem { Id = "FL01", ResourceType = "flight", Description = "FL01 - Milano -> Roma" }
        };
        var airportOnlyItems = new[]
        {
            new SearchResultItem { Id = "AP01", ResourceType = "airport", Description = "AP01 - Milano (Italia)" }
        };

        inner.Setup(x => x.SearchAsync("milano", 0, 2000, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse(allItems));
        inner.Setup(x => x.SearchAsync("milano", 0, 2000, "airport", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse(airportOnlyItems));

        var sut = CreateSut(inner, out _);

        var allResult = await sut.SearchAsync("milano", 0, 10, resourceType: null, CancellationToken.None);
        var airportResult = await sut.SearchAsync("milano", 0, 10, resourceType: "airport", CancellationToken.None);

        // Se i due bucket condividessero per errore la stessa chiave di cache, la seconda
        // chiamata riuserebbe il set gia' cacheato dalla prima (count=2) invece di
        // interrogare di nuovo "inner" per il proprio bucket (count=1).
        Assert.Equal(2, allResult.Count);
        Assert.Equal(1, airportResult.Count);

        inner.Verify(x => x.SearchAsync("milano", 0, 2000, null, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(x => x.SearchAsync("milano", 0, 2000, "airport", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_KnownQueriesSet_IsTrackedSeparatelyPerBucket()
    {
        var inner = new Mock<IGlobalSearchService>();

        inner.Setup(x => x.SearchAsync("milano", 0, 2000, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse(new SearchResultItem { Id = "AP01", ResourceType = "airport", Description = "AP01 - Milano (Italia)" }));
        inner.Setup(x => x.SearchAsync("milano", 0, 2000, "flight", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse());

        var sut = CreateSut(inner, out var redisSets);

        await sut.SearchAsync("milano", 0, 10, resourceType: null, CancellationToken.None);
        await sut.SearchAsync("milano", 0, 10, resourceType: "flight", CancellationToken.None);

        Assert.True(redisSets.TryGetValue("globalsearch:known-queries:all", out var allSet));
        Assert.Contains("milano", allSet!);

        Assert.True(redisSets.TryGetValue("globalsearch:known-queries:flight", out var flightSet));
        Assert.Contains("milano", flightSet!);

        Assert.True(redisSets.Count >= 2,
            "Ci si aspettano almeno due chiavi Redis distinte per i known-queries dei due bucket coinvolti.");
    }

    [Fact]
    public async Task SearchAsync_SubstringReuse_IsScopedToItsOwnBucket()
    {
        var inner = new Mock<IGlobalSearchService>();

        // Bucket "airport": cachiamo prima "milano".
        inner.Setup(x => x.SearchAsync("milano", 0, 2000, "airport", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse(new SearchResultItem { Id = "AP01", ResourceType = "airport", Description = "AP01 - Milano (Italia)" }));

        // Bucket "all": una query PIU' SPECIFICA che contiene "milano" come sottostringa.
        // Se il riuso per sottostringa non fosse correttamente confinato al proprio bucket,
        // questa chiamata troverebbe (per errore) il set gia' cacheato dal bucket "airport"
        // e non richiamerebbe affatto "inner" per il bucket "all".
        inner.Setup(x => x.SearchAsync("milano malpensa", 0, 2000, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResponse(new SearchResultItem { Id = "AP01", ResourceType = "airport", Description = "AP01 - Milano Malpensa (Italia)" }));

        var sut = CreateSut(inner, out _);

        await sut.SearchAsync("milano", 0, 10, resourceType: "airport", CancellationToken.None);
        await sut.SearchAsync("milano malpensa", 0, 10, resourceType: null, CancellationToken.None);

        inner.Verify(x => x.SearchAsync("milano malpensa", 0, 2000, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
