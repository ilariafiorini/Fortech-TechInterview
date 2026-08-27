namespace TechInterview.Web.Components.Shared;

/// <summary>
/// DTO condivisi tra Search.razor e SearchResultsTable.razor per il contratto di
/// GET /api/global-search (GlobalSearchService) — estratti in un file a parte (invece che
/// duplicati nel @code di ciascuna pagina, come fatto altrove in questo progetto per
/// Airports/Flights) perche' qui sono davvero condivisi tra piu' componenti, non solo
/// duplicati per convenienza.
/// </summary>
public class SearchResultDto
{
    public string Id { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string Description { get; set; } = "";
}

public class SearchResponseDto
{
    public List<SearchResultDto> Items { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int Count { get; set; }
}
