namespace GlobalSearchService.Models;

public class GlobalSearchResponse
{
    public IReadOnlyList<SearchResultItem> Items { get; set; } = Array.Empty<SearchResultItem>();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Count { get; set; }
}
