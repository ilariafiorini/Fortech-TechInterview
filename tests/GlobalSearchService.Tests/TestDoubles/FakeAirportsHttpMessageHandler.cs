using System.Net;
using System.Net.Http.Json;
using GlobalSearchService.Models;

namespace GlobalSearchService.Tests.TestDoubles;

/// <summary>
/// Simula GET /api/airports di AirportsService affettando in pagine una lista di aeroporti
/// tenuta in memoria, cosi' i test su AirportsSearchCache possono verificare il fan-out
/// (multi-pagina, offset/limit, totalCount) e contare le chiamate HTTP effettuate senza
/// Docker ne' rete reale. CallCount permette di verificare, ad es., che una query servita
/// dalla cache non generi alcuna chiamata HTTP.
/// </summary>
internal sealed class FakeAirportsHttpMessageHandler : HttpMessageHandler
{
    private readonly IReadOnlyList<AirportDto> _allAirports;

    public int CallCount { get; private set; }

    public FakeAirportsHttpMessageHandler(IReadOnlyList<AirportDto> allAirports)
    {
        _allAirports = allAirports;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;

        var query = ParseQuery(request.RequestUri!.Query);
        var offset = int.Parse(query["offset"]);
        var limit = int.Parse(query["limit"]);

        var page = new AirportsPageResponse
        {
            Items = _allAirports.Skip(offset).Take(limit).ToList(),
            Offset = offset,
            Limit = limit,
            TotalCount = _allAirports.Count
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(page)
        };

        return Task.FromResult(response);
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
}
