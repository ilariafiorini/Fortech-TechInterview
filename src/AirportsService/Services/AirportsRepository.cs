using AirportsService.Models;

namespace AirportsService.Services;

public interface IAirportsRepository
{
    Task<PagedList<Airport>> GetPagedAsync(int offset, int limit);
    Task<Airport?> GetAsync(string id);
}

public class AirportsRepository : IAirportsRepository
{
    private readonly List<Airport> _airports;
    private static readonly Random Rand = new();

    public AirportsRepository()
    {
        _airports = GenerateMockAirports(300);
    }

    public async Task<PagedList<Airport>> GetPagedAsync(int offset, int limit)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100 + 50 * limit));
        return new PagedList<Airport>(_airports
            .Skip(offset)
            .Take(limit), _airports.Count);
    }

    public Task<Airport?> GetAsync(string id)
        => Task.FromResult(_airports.SingleOrDefault(x=>string.Equals(x.Id, id, StringComparison.CurrentCultureIgnoreCase)));
    
    private static List<Airport> GenerateMockAirports(int count)
    {
        string[] cities = { "Milano", "Roma", "Firenze", "Parigi", "Londra", "Madrid", "Berlino", "New York", "Singapore", "Tokyo", "Los Angeles", "Chicago" };
        string[] countries = { "Italy", "France", "UK", "Spain", "Germany", "USA", "Singapore", "Japan" };

        var list = new List<Airport>();

        for (int i = 0; i < count; i++)
        {
            var city = cities[Rand.Next(cities.Length)];
            var country = countries[Rand.Next(countries.Length)];

            list.Add(new Airport
            {
                Id = $"AP{i:D4}",
                Name = $"{city} International Airport {i}",
                City = city,
                Country = country
            });
        }

        return list;
    }
}