namespace GlobalSearchService.Services;

/// <summary>
/// Ampiezza del fan-out con cui SearchAsync interroga AirportsService (REST) e
/// FlightsService (gRPC) — quante chiamate GetAirports/GetFlights in parallelo servono
/// per coprire l'intero dataset di una fonte. Vedi le voci "Ampiezza del fan-out" e
/// "Strategia di caching per fonte" in docs/architecture.md per come e' stato scelto il
/// valore di default (misurato empiricamente con gli script in tools/).
/// </summary>
public class SearchFanOutOptions
{
    /// <summary>
    /// Limit usato per ciascuna chiamata GetAirports/GetFlights durante il fan-out.
    /// Sia AirportsService sia FlightsService limitano comunque a un massimo di 100
    /// lato server, indipendentemente da questo valore.
    /// </summary>
    public int PageLimit { get; set; } = 20;
}
