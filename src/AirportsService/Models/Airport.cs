namespace AirportsService.Models
{
    public class Airport
    {
        public string Id { get; set; } = default!;        // Codice aeroporto es: MXP, JFK
        public string Name { get; set; } = default!;      // Nome aeroporto
        public string City { get; set; } = default!;      // Città
        public string Country { get; set; } = default!;   // Nazione
    }
}