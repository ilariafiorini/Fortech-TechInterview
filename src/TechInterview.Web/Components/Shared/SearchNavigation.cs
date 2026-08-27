using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace TechInterview.Web.Components.Shared;

/// <summary>
/// Centralizza lo schema URL condiviso dalle quattro pagine di ricerca (Search.razor,
/// SearchAll.razor, SearchAirports.razor, SearchFlights.razor) e dalla sidebar
/// (NavMenu.razor): un'unica query string con "query" + un offset e un limite di
/// paginazione indipendenti per ciascuna vista ("allOffset"/"allLimit",
/// "airportsOffset"/"airportsLimit", "flightsOffset"/"flightsLimit").
///
/// Perche' un offset per vista invece di uno solo condiviso: passando da una scheda
/// all'altra tramite i sottopulsanti della sidebar si vuole ritrovare l'ultima pagina
/// vista IN QUELLA scheda, non ripartire da zero (che invece succede solo quando si
/// esegue una NUOVA ricerca da Search.razor, che azzera tutti e tre gli offset insieme).
/// Stessa logica per il limite: e' stato reso indipendente per scheda (invece che un
/// singolo valore condiviso) proprio per evitare che cambiarlo in una scheda alteri
/// silenziosamente cosa significa un offset gia' salvato nelle altre due.
/// Tenerli tutti nella query string, invece che in uno stato lato server, mantiene
/// ogni pagina interamente ricostruibile da un refresh del browser o da un link
/// condiviso, coerentemente con come sono gia' scritte Airports.razor/Flights.razor.
///
/// Ogni pagina legge lo stato intero con ReadFrom, modifica solo il proprio campo di
/// interesse (di solito con `state = state with { ... }`) e ripropaga il resto invariato
/// costruendo il prossimo URL con BuildUrl — cosi' gli altri campi "viaggiano" intatti
/// anche quando non sono quelli attivi in quel momento.
/// </summary>
public static class SearchNavigation
{
    public const string SearchPath = "/search";
    public const string AllPath = "/search/all";
    public const string AirportsPath = "/search/airports";
    public const string FlightsPath = "/search/flights";

    public sealed record State(
        string Query,
        int AllLimit,
        int AirportsLimit,
        int FlightsLimit,
        int AllOffset,
        int AirportsOffset,
        int FlightsOffset)
    {
        public bool HasQuery => Query.Trim().Length >= 3;
    }

    public static State ReadFrom(NavigationManager nav)
    {
        var uri = nav.ToAbsoluteUri(nav.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);

        return new State(
            Query: query.TryGetValue("query", out var q) ? q.ToString() : string.Empty,
            AllLimit: ReadInt(query, "allLimit", 20),
            AirportsLimit: ReadInt(query, "airportsLimit", 20),
            FlightsLimit: ReadInt(query, "flightsLimit", 20),
            AllOffset: ReadInt(query, "allOffset", 0),
            AirportsOffset: ReadInt(query, "airportsOffset", 0),
            FlightsOffset: ReadInt(query, "flightsOffset", 0));
    }

    public static string BuildUrl(string path, State state) =>
        $"{path}?query={Uri.EscapeDataString(state.Query.Trim())}" +
        $"&allLimit={state.AllLimit}" +
        $"&airportsLimit={state.AirportsLimit}" +
        $"&flightsLimit={state.FlightsLimit}" +
        $"&allOffset={state.AllOffset}" +
        $"&airportsOffset={state.AirportsOffset}" +
        $"&flightsOffset={state.FlightsOffset}";

    private static int ReadInt(Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key, int fallback) =>
        query.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) ? value : fallback;
}
