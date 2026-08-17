using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

/// <summary>
/// MOCKUP — implementazione segnaposto della Global Search API.
///
/// Restituisce dati statici indipendentemente dalla query, solo per validare il
/// contratto dell'endpoint end-to-end (shape della risposta, pipeline Docker/Aspire).
/// Non contiene alcuna logica di aggregazione reale: e' il pezzo da completare.
///
/// TODO per arrivare all'implementazione reale:
///  1. Iniettare un IHttpClientFactory (client registrato come "airports" in
///     Program.cs) per interrogare AirportsService via REST (GET /api/airports) e
///     filtrare sui campi richiesti: codice, nome, citta, nazione.
///  2. Iniettare Flights.FlightsClient (gRPC, client "flights-grpc", gia registrato
///     in Program.cs) per interrogare FlightsService e filtrare su: codice volo,
///     numero aeromobile, citta/aeroporto di partenza e arrivo.
///  3. Decidere la strategia di paginazione: sulle singole fonti (piu efficiente ma
///     complica l'unione degli offset) oppure sul risultato aggregato (piu semplice,
///     ma richiede di scaricare piu dati dalle fonti di quanti ne servano davvero).
///  4. Gestire i casi limite: query gia validata >= 3 caratteri nel controller, ma
///     vanno gestiti anche limit/offset fuori range e gli errori/timeout di una delle
///     due fonti dati (per il bonus "resilienza": AddStandardResilienceHandler e' gia
///     attivo di default su tutti gli HttpClient via TechInterview.ServiceDefaults).
///  5. Valutare un layer di caching/indicizzazione locale per il bonus performance.
/// </summary>
public class MockGlobalSearchService : IGlobalSearchService
{
    public Task<GlobalSearchResponse> SearchAsync(string query, int offset, int limit, CancellationToken cancellationToken)
    {
        var mockItems = new List<SearchResultItem>
        {
            new() { Id = "MXP", ResourceType = "airport", Description = "MXP - Malpensa (Italy)" },
            new() { Id = "AZ178", ResourceType = "flight", Description = "AZ178 - MXP -> JFK" },
        };

        var response = new GlobalSearchResponse
        {
            Items = mockItems.Take(limit).ToList(),
            Offset = offset,
            Limit = limit,
            Count = mockItems.Count
        };

        return Task.FromResult(response);
    }
}
