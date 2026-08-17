namespace GlobalSearchService.Models;

/// <summary>
/// Risultato eterogeneo della ricerca globale (puo rappresentare un aeroporto o un volo).
/// </summary>
public class SearchResultItem
{
    public string Id { get; set; } = default!;

    /// <summary>"airport" oppure "flight".</summary>
    public string ResourceType { get; set; } = default!;

    public string Description { get; set; } = default!;
}
