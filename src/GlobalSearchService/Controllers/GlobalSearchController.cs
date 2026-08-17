using GlobalSearchService.Services;
using Microsoft.AspNetCore.Mvc;

namespace GlobalSearchService.Controllers;

[ApiController]
[Route("api/global-search")]
public class GlobalSearchController : ControllerBase
{
    private readonly IGlobalSearchService _searchService;

    public GlobalSearchController(IGlobalSearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<ActionResult> Search(
        [FromQuery] string query,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            return BadRequest(new { error = "Il parametro 'query' deve contenere almeno 3 caratteri." });
        }

        offset = Math.Max(offset, 0);
        limit = limit <= 0 ? 10 : limit;
        limit = Math.Min(limit, 100);

        var result = await _searchService.SearchAsync(query.Trim(), offset, limit, cancellationToken);

        return Ok(result);
    }
}
