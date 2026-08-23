namespace GlobalSearchService.Models;

/// <summary>
/// Copia locale (POCO semplice) dei campi di FlightsService.Grpc.Flight rilevanti per la
/// ricerca. Si converte subito dal messaggio Protobuf generato a questo DTO per evitare di
/// serializzare/deserializzare in cache il tipo gRPC direttamente (i messaggi Protobuf
/// generati hanno membri extra - Parser, Descriptor, ecc. - non pensati per
/// System.Text.Json).
/// </summary>
public class FlightDto
{
    public string Id { get; set; } = default!;
    public string AircraftNumber { get; set; } = default!;
    public string DepartureAirportCode { get; set; } = default!;
    public string ArrivalAirportCode { get; set; } = default!;
    public string DepartureCity { get; set; } = default!;
    public string ArrivalCity { get; set; } = default!;

    public static FlightDto FromGrpc(FlightsService.Grpc.Flight flight) => new()
    {
        Id = flight.Id,
        AircraftNumber = flight.AircraftNumber,
        DepartureAirportCode = flight.DepartureAirportCode,
        ArrivalAirportCode = flight.ArrivalAirportCode,
        DepartureCity = flight.DepartureCity,
        ArrivalCity = flight.ArrivalCity
    };
}
