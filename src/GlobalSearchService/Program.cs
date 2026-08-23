using GlobalSearchService.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using StackExchange.Redis;
using Flights = FlightsService.Grpc.Flights;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();

// Client REST verso AirportsService — usato da AirportsSearchCache per il fan-out.
// La risoluzione dell'host "airportsservice" avviene via service discovery di Aspire in
// sviluppo, o tramite le variabili d'ambiente "Services__airportsservice__..." quando
// containerizzato.
builder.Services.AddHttpClient("airports", client =>
{
    client.BaseAddress = new Uri("https+http://airportsservice");
});

// Client gRPC verso FlightsService — usato da FlightsSearchCache per lo sweep completo.
builder.Services.AddGrpcClient<Flights.FlightsClient>("flights-grpc", o =>
{
    o.Address = new("http://flightsservice");
});

// Cache Redis: registra sia IConnectionMultiplexer (per operazioni "raw", es. i SET usati
// per tracciare le chiavi/query note) sia IDistributedCache (per i valori con scadenza
// sliding+assoluta). La stringa di connessione "cache" arriva da Aspire in sviluppo
// (WithReference nell'AppHost) o dalla variabile d'ambiente ConnectionStrings__cache
// quando containerizzato (vedi docker-compose.yml).
builder.AddRedisClient("cache");
builder.AddRedisDistributedCache("cache");
builder.Services.Configure<GlobalSearchCacheOptions>(builder.Configuration.GetSection("GlobalSearchCache"));
builder.Services.Configure<SearchFanOutOptions>(builder.Configuration.GetSection("SearchFanOut"));

// PROTOTIPO DI STUDIO (branch prototype/real-search): cache interne dedicate per fonte —
// vedi "Strategia di caching per fonte" in docs/architecture.md. Airports fa riuso per
// sottostringa con seeding lazy del superset ("" ); Flights fa crystallize-per-ricerca,
// nessun riuso per sottostringa. Usate solo da RealGlobalSearchService qui sotto.
builder.Services.AddSingleton<IAirportsSearchCache, AirportsSearchCache>();
builder.Services.AddSingleton<IFlightsSearchCache, FlightsSearchCache>();

// MockGlobalSearchService resta nel progetto come riferimento storico del contratto (vedi
// i suoi TODO): non e' piu' l'implementazione usata. RealGlobalSearchService la sostituisce
// con la logica vera; CachingGlobalSearchService continua ad avvolgerla senza modifiche,
// esattamente come previsto fin dall'inizio.
builder.Services.AddSingleton<RealGlobalSearchService>();
builder.Services.AddSingleton<IGlobalSearchService>(sp => new CachingGlobalSearchService(
    sp.GetRequiredService<RealGlobalSearchService>(),
    sp.GetRequiredService<IDistributedCache>(),
    sp.GetRequiredService<IConnectionMultiplexer>(),
    sp.GetRequiredService<IOptions<GlobalSearchCacheOptions>>()));

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
