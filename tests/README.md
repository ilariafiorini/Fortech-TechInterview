# Test automatici

## Unit test (per servizio)

Un progetto xUnit per servizio, con HTTP/gRPC/Redis sostituiti da doppi di test — nessun
Docker richiesto, girano con un normale `dotnet test`:

- `GlobalSearchService.Tests` — creato (branch `prototype/real-search`). Copre la logica
  isolabile (`AirportsSearchCache`, `FlightsSearchCache`, `RealGlobalSearchService`,
  `CachingGlobalSearchService` — inclusa la separazione per bucket/resourceType — e le
  due azioni di `GlobalSearchController` che leggono il dettaglio dalla cache di ricerca).
  Vedi la sezione "Implementazione della Global Search API reale" in `docs/architecture.md` per il dettaglio.
- `AirportsService.Tests` — non ancora creato.
- `FlightsService.Tests` — non ancora creato.

Ogni progetto di test va referenziato anche in `TechInterview.sln` una volta creato (es.
`dotnet new xunit -o tests/NomeServizio.Tests` seguito da `dotnet sln add`; per
`GlobalSearchService.Tests` il riferimento e' gia' presente nella sln).

```
dotnet test tests/GlobalSearchService.Tests/GlobalSearchService.Tests.csproj
```

oppure, dalla root della solution, `dotnet test` per eseguire tutti i progetti di test
referenziati in `TechInterview.sln` (solo unit test, vedi sotto).

## Test di integrazione ("dal vivo", contro Docker)

- `GlobalSearchService.IntegrationTests` — creato (branch `prototype/real-search`). Test
  black-box contro la stack Docker Compose realmente avviata (nessun mock): richiede
  `docker compose up` gia' avviato in `docker/`. **Volutamente non referenziato** in
  `TechInterview.sln`, cosi' il `dotnet test` di cui sopra non lo tocca mai per sbaglio.
  Vedi `GlobalSearchService.IntegrationTests/README.md` per come lanciarlo.

```
dotnet test tests/GlobalSearchService.IntegrationTests/GlobalSearchService.IntegrationTests.csproj
```
