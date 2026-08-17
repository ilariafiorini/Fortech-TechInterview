using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

public interface IGlobalSearchService
{
    Task<GlobalSearchResponse> SearchAsync(string query, int offset, int limit, CancellationToken cancellationToken);
}
