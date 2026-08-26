using Moq;
using StackExchange.Redis;

namespace GlobalSearchService.Tests.TestDoubles;

/// <summary>
/// Costruisce un IConnectionMultiplexer finto che copre l'unico uso che ne fa
/// AirportsSearchCache: tenere il set Redis "known-keys" (SMEMBERS/SADD/SREM) sostenuto da
/// un HashSet&lt;string&gt; in memoria, cosi' nessun server Redis reale e' necessario nei
/// test. Il HashSet passato viene mutato dai metodi mockati esattamente come farebbe Redis,
/// quindi il test puo' ispezionarlo dopo la chiamata per verificare seeding/pulizia delle
/// chiavi note.
/// </summary>
internal static class RedisKnownKeysTestFactory
{
    public static IConnectionMultiplexer Create(HashSet<string> knownKeys)
    {
        var database = new Mock<IDatabase>();

        database
            .Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => knownKeys.Select(k => (RedisValue)k).ToArray());

        database
            .Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey _, RedisValue value, CommandFlags _) => knownKeys.Add(value.ToString()!));

        database
            .Setup(d => d.SetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey _, RedisValue value, CommandFlags _) => knownKeys.Remove(value.ToString()!));

        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        return multiplexer.Object;
    }
}
