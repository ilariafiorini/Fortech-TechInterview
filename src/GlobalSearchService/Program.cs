using GlobalSearchService.Services;
using Microsoft.Extensions.Caching.Distributed;
using Scalar.AspNetCore;
using StackExchange.Redis;
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

// Cache Redis: registra sia IConnectionMultiplexer (per operazioni "raw", es. i SET usati per
// tracciare le query note) sia IDistributedCache (per i valori con scadenza sliding+assoluta).
// La stringa di connessione "cache" arriva da Aspire in sviluppo (WithReference nell'AppHost) o
// dalla variabile d'ambiente ConnectionStrings__cache quando containerizzato (vedi docker-compose.yml).
builder.AddRedisClient("cache");
builder.AddRedisDistributedCache("cache");
builder.Services.Configure<GlobalSearchCacheOptions>(builder.Configuration.GetSection("GlobalSearchCache"));

// MOCKUP: MockGlobalSearchService e' il segnaposto da sostituire con la ricerca reale (vedi i
// TODO al suo interno). CachingGlobalSearchService lo avvolge aggiungendo la cache Redis senza
// che l'implementazione sottostante debba saperne nulla: quando sostituirai MockGlobalSearchService
// con la logica vera, la cache continuera' a funzionare senza modifiche.
builder.Services.AddSingleton<MockGlobalSearchService>();
builder.Services.AddSingleton<IGlobalSearchService>(sp => new CachingGlobalSearchService(
    sp.GetRequiredService<MockGlobalSearchService>(),
    sp.GetRequiredService<IDistributedCache>(),
    sp.GetRequiredService<IConnectionMultiplexer>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSearchCacheOptions>>()));

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
