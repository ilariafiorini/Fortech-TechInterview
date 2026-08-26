namespace GlobalSearchService.IntegrationTests;

/// <summary>
/// Indirizzi della stack Docker Compose contro cui girano questi test — di default quelli
/// definiti in docker/.env, sovrascrivibili con variabili d'ambiente se li cambi (es. per
/// evitare conflitti di porta sulla tua macchina).
/// </summary>
internal static class TestConfig
{
    public static readonly string GlobalSearchBaseUrl =
        Environment.GetEnvironmentVariable("GLOBALSEARCH_BASE_URL") ?? "http://localhost:8083";
}
