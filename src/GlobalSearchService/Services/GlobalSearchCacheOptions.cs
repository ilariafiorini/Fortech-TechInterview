namespace GlobalSearchService.Services;

/// <summary>
/// Configurazione della cache Redis davanti alla Global Search API (vedi appsettings.json,
/// sezione "GlobalSearchCache").
/// </summary>
public class GlobalSearchCacheOptions
{
    /// <summary>Ogni lettura andata a buon fine rinnova la scadenza di questa durata.</summary>
    public int SlidingExpirationMinutes { get; set; } = 5;

    /// <summary>Tetto massimo di vita di una entry, indipendentemente da quante volte viene letta.</summary>
    public int AbsoluteExpirationMinutes { get; set; } = 30;
}
