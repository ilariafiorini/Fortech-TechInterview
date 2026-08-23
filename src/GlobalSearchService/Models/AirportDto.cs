namespace GlobalSearchService.Models;

/// <summary>
/// Copia locale del modello Airport esposto da AirportsService (REST). GlobalSearchService
/// non referenzia il progetto AirportsService: replica qui solo i campi richiesti dalle
/// specifiche per la ricerca (codice, nome, citta', nazione).
/// </summary>
public class AirportDto
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string City { get; set; } = default!;
    public string Country { get; set; } = default!;
}
