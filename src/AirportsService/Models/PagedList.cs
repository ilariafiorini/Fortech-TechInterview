namespace AirportsService.Models;

public class PagedList<T> : List<T>
{
    public PagedList(int totalCount)
    {
        TotalCount = totalCount;
    }

    public PagedList(IEnumerable<T> collection, int totalCount) : base(collection)
    {
        TotalCount = totalCount;
    }

    public PagedList(int capacity, int totalCount) : base(capacity)
    {
        TotalCount = totalCount;
    }

    public int TotalCount { get; set; }
    
}