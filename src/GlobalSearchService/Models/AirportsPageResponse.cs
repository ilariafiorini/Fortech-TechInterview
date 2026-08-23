namespace GlobalSearchService.Models;

/// <summary>
/// Forma della risposta di GET /api/airports su AirportsService (vedi
/// AirportsController.cs): { items, offset, limit, totalCount }.
/// </summary>
public class AirportsPageResponse
{
    public List<AirportDto> Items { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalCount { get; set; }
}
