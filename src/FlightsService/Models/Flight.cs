namespace FlightsService.Models
{
    public class Flight
    {
        public string Id { get; set; } = default!;                 // Codice volo es: "AZ178"
        public string AircraftNumber { get; set; } = default!;     // Numero aeromobile
        public string DepartureAirportCode { get; set; } = default!;
        public string ArrivalAirportCode { get; set; } = default!;

        public string DepartureCity { get; set; } = default!;
        public string ArrivalCity { get; set; } = default!;

        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
    }
}