using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Ambiente Docker Compose: "aspire publish" genera un docker-compose.yml a partire
// da questo stesso grafo di risorse (vedi la cartella docker/ per un equivalente
// gia predisposto, utile finche non si esegue aspire publish in prima persona).
builder.AddDockerComposeEnvironment("env");

var airports = builder.AddProject<Projects.AirportsService>("airportsservice").WithUrlForEndpoint("scalar", url =>
{
    url.Url = "/scalar"; // Appends to the existing host and port
    url.DisplayText = "Scalar UI (HTTPS)";
});

var flights = builder.AddProject<Projects.FlightsService>("flightsservice");

// GlobalSearchService e' per ora un MOCKUP: espone gia' il contratto richiesto
// dalla consegna ma risponde con dati statici. I riferimenti verso airports/flights
// sono gia' cablati qui cosi' che, quando implementerai la ricerca reale, il servizio
// abbia gia' a disposizione la service discovery verso le due fonti dati.
var globalSearch = builder.AddProject<Projects.GlobalSearchService>("globalsearchservice")
    .WithReference(airports)
    .WaitFor(airports)
    .WithReference(flights)
    .WaitFor(flights);

builder.AddProject<Projects.TechInterview_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(flights)
    .WaitFor(flights)
    .WithReference(airports)
    .WaitFor(airports)
    .WithReference(globalSearch)
    .WaitFor(globalSearch);

builder.Build().Run();