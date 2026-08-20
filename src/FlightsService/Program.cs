using FlightsService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// In esecuzione locale (Aspire/F5) non tocchiamo la configurazione di Kestrel: il
// comportamento di default (HTTP/2 puro sull'endpoint cleartext, dato che il servizio
// registra solo AddGrpc()) e' esattamente quello che serve ai client gRPC.
//
// In Docker impostiamo invece FLIGHTS_HEALTH_PORT (vedi docker-compose.yml) per avere
// una SECONDA porta, dedicata e separata, che parla solo HTTP/1.1 ed e' usata
// esclusivamente dall'healthcheck Docker (curl). Le due cose non possono convivere sulla
// stessa porta in chiaro: senza TLS non c'e' ALPN, quindi Kestrel non ha un modo
// affidabile di negoziare HTTP/1.1 e HTTP/2 sulla stessa connessione. Impostare
// Http1AndHttp2 sull'endpoint gRPC (come in un tentativo precedente) "risolve" il 400
// dell'healthcheck ma introduce errori intermittenti lato client gRPC
// (HTTP_1_1_REQUIRED) su GlobalSearchService/TechInterview.Web: da qui la porta separata.
//
// Quando definiamo esplicitamente un endpoint via Listen/ListenAnyIP, Kestrel smette di
// applicare automaticamente ASPNETCORE_URLS/ASPNETCORE_HTTP_PORTS: per questo, se
// FLIGHTS_HEALTH_PORT e' impostata, ridichiariamo esplicitamente anche la porta gRPC
// principale (leggendo ASPNETCORE_HTTP_PORTS, con fallback a 8080).
var healthPort = builder.Configuration["FLIGHTS_HEALTH_PORT"];
if (!string.IsNullOrWhiteSpace(healthPort) && int.TryParse(healthPort, out var parsedHealthPort))
{
    var grpcPort = builder.Configuration.GetValue<int?>("ASPNETCORE_HTTP_PORTS") ?? 8080;

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(grpcPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        options.ListenAnyIP(parsedHealthPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
    });
}

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddGrpc();

// Reflection (solo in Development, vedi sotto): consente a client generici come grpcurl,
// grpcui o Postman/Insomnia di scoprire ed esplorare i servizi/metodi esposti senza dover
// importare a mano Protos\flights.proto — l'equivalente, per gRPC, di quello che
// Scalar/OpenAPI offrono per le REST API di AirportsService/GlobalSearchService.
builder.Services.AddGrpcReflection();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
app.MapGrpcService<FlightsServiceImpl>();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
