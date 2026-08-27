using Moq;
using StackExchange.Redis;

namespace GlobalSearchService.Tests.TestDoubles;

/// <summary>
/// Come RedisKnownKeysTestFactory, ma tiene un HashSet&lt;string&gt; DISTINTO per ogni
/// chiave Redis richiesta (invece di uno condiviso per tutte le chiavi, indifferente alla
/// RedisKey passata). Serve a CachingGlobalSearchServiceTests per verificare che i set
/// "known-queries" di bucket diversi (globalsearch:known-queries:all/airport/flight)
/// restino davvero separati: con RedisKnownKeysTestFactory (un solo HashSet condiviso,
/// giusto per AirportsSearchCache che usa una sola chiave known-keys) un eventuale bug di
/// separazione tra bucket passerebbe inosservato, perche' il doppio di test stesso non
/// distinguerebbe le chiavi.
/// </summary>
internal static class MultiKeyRedisTestFactory
{
    public static IConnectionMultiplexer Create(out Dictionary<string, HashSet<string>> sets)
    {
        var backing = new Dictionary<string, HashSet<string>>();
        sets = backing;

        HashSet<string> SetFor(RedisKey key)
        {
            var keyString = key.ToString()!;
            if (!backing.TryGetValue(keyString, out var set))
            {
                set = new HashSet<string>();
                backing[keyString] = set;
            }

            return set;
        }

        var database = new Mock<IDatabase>();

        database
            .Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) => SetFor(key).Select(v => (RedisValue)v).ToArray());

        database
            .Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, CommandFlags _) => SetFor(key).Add(value.ToString()!));

        database
            .Setup(d => d.SetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, CommandFlags _) => SetFor(key).Remove(value.ToString()!));

        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        return multiplexer.Object;
    }
}
