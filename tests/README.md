# Test automatici

Cartella riservata ai progetti di test (xUnit) della solution, uno per ogni servizio, ad es.:

- `AirportsService.Tests`
- `FlightsService.Tests`
- `GlobalSearchService.Tests`

Ogni progetto di test va referenziato anche in `TechInterview.sln` una volta creato (es. `dotnet new xunit -o tests/GlobalSearchService.Tests` seguito da `dotnet sln add`).
