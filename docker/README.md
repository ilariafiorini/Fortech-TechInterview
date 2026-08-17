# Ambiente Docker

Questa cartella contiene tutto il necessario per eseguire l'intera solution
containerizzata in Docker Desktop, senza bisogno dell'SDK .NET installato in locale.

## Servizi

| Servizio            | Porta host | Descrizione                                       |
|----------------------|-----------|----------------------------------------------------|
| webfrontend           | 8080      | Frontend Blazor Server                              |
| airportsservice        | 8081      | REST API — CRUD aeroporti                           |
| flightsservice         | 8082      | gRPC API — CRUD voli                                |
| globalsearchservice    | 8083      | Global Search API — **attualmente un MOCKUP**       |
| aspire-dashboard       | 18888     | Dashboard OpenTelemetry (log/traccia/metriche)      |

## Come avviarlo

Dalla cartella `docker/`:

```
docker compose up --build
```

Al termine dello startup:

- http://localhost:8080 — frontend
- http://localhost:8081/scalar — Scalar UI per AirportsService
- http://localhost:8083/api/global-search?query=mxp — endpoint mockup della Global Search API
- http://localhost:18888 — Aspire Dashboard (log/traccia/metriche di tutti i servizi)

Per fermare tutto:

```
docker compose down
```

## Perche' i container girano in modalita' Development

Gli endpoint di health-check (`/health`, `/alive`) esposti da
`TechInterview.ServiceDefaults` sono mappati solo quando
`ASPNETCORE_ENVIRONMENT=Development` (vedi `MapDefaultEndpoints` in
`src/TechInterview.ServiceDefaults/Extensions.cs`). Per questo motivo tutti i
servizi in questo compose girano in Development anziche' in Production: serve
sia a far funzionare gli `healthcheck:`/`depends_on: condition: service_healthy`,
sia ad avere le UI Scalar/OpenAPI disponibili per testare gli endpoint a mano.

## Relazione con .NET Aspire

Il file `docker-compose.yml` in questa cartella e' scritto a mano, ma modellato
sull'output che genera nativamente il comando `aspire publish` a partire
dall'ambiente dichiarato in `AppHost.cs` tramite:

```csharp
builder.AddDockerComposeEnvironment("env");
```

Se hai la Aspire CLI installata puoi provare a farlo generare/rigenerare da Aspire
stesso:

```
cd ../src/TechInterview.AppHost
aspire publish
```

e confrontare l'output con questo file (creato manualmente perche' in questo
ambiente non è stato possibile eseguire l'SDK .NET/la Aspire CLI per generarlo e
validarlo automaticamente — vale la pena verificarlo con una build reale prima di
fare troppo affidamento su di esso).

## Cosa e' un MOCKUP e cosa no

- `airportsservice`, `flightsservice`, `webfrontend`: codice gia' fornito con il
  test, containerizzato cosi' com'e'.
- `globalsearchservice`: **solo lo scheletro**. Espone gia' il contratto richiesto
  dalla consegna (`GET /api/global-search?query=&offset=&limit=`, validazione della
  query >= 3 caratteri, paginazione, forma della risposta), ha gia' pronti (ma non
  usati) i client verso `airportsservice` (REST) e `flightsservice` (gRPC), ma
  risponde sempre con dati statici. La logica di aggregazione/ricerca vera e propria
  e' quella da implementare — vedi i TODO in
  `src/GlobalSearchService/Services/MockGlobalSearchService.cs`.
