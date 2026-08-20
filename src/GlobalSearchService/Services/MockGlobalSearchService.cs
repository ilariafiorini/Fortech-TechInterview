using GlobalSearchService.Models;

namespace GlobalSearchService.Services;

/// <summary>
/// MOCKUP — implementazione segnaposto della Global Search API.
///
/// Restituisce dati statici indipendentemente dalla query, solo per validare il
/// contratto dell'endpoint end-to-end (shape della risposta, pipeline Docker/Aspire).
/// Non contiene alcuna logica di aggregazione reale: e' il pezzo da completare.
///
/// NOTA: la cache (Redis, con riuso per sottostringa e scadenza sliding+assoluta) e' gia'
/// gestita interamente dal decorator CachingGlobalSearchService, che avvolge questa classe.
/// Non serve implementare nessuna logica di cache qui dentro: quando sostituirai questa classe
/// con la ricerca reale, il decorator continuera' a funzionare senza modifiche.
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
///     NB: CachingGlobalSearchService chiama questo metodo con un limit molto ampio
///     (MaxUnderlyingFetch) per ottenere l'insieme "completo" dei match da cachare, quindi
///     questa implementazione deve gestire correttamente anche richieste con limit alto.
///  4. Gestire i casi limite: query gia' validata >= 3 caratteri nel controller, ma
///     vanno gestiti anche limit/offset fuori range e gli errori/timeout di una delle
///     due fonti dati (per il bonus "resilienza": AddStandardResilienceHandler e' gia'
///     attivo di default su tutti gli HttpClient via TechInterview.ServiceDefaults).
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
