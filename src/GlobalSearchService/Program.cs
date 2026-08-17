using GlobalSearchService.Services;
using Scalar.AspNetCore;
using Flights = FlightsService.Grpc.Flights;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();

// Client REST verso AirportsService — gia pronto per essere usato nell'implementazione
// reale (vedi i TODO in Services/MockGlobalSearchService.cs). La risoluzione dell'host
// "airportsservice" avviene via service discovery di Aspire in sviluppo, o tramite le
// variabili d'ambiente "Services__airportsservice__..." quando containerizzato.
builder.Services.AddHttpClient("airports", client =>
{
    client.BaseAddress = new Uri("https+http://airportsservice");
});

// Client gRPC verso FlightsService — idem, gia pronto per essere iniettato.
builder.Services.AddGrpcClient<Flights.FlightsClient>("flights-grpc", o =>
{
    o.Address = new("http://flightsservice");
});

// MOCKUP: sostituire con l'implementazione reale una volta pronta.
builder.Services.AddSingleton<IGlobalSearchService, MockGlobalSearchService>();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar"));
}

app.Run();
