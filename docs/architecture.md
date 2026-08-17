# Note architetturali

## Struttura del repository

```
TechInterview/
├── docs/                  # questo documento e altre note
├── docker/                # docker-compose.yml, README con istruzioni, .dockerignore
├── src/
│   ├── TechInterview.AppHost/           # orchestratore Aspire (+ AddDockerComposeEnvironment)
│   ├── TechInterview.ServiceDefaults/   # telemetry, health check, resilienza condivisi
│   ├── AirportsService/                 # REST API, CRUD aeroporti (Dockerfile incluso)
│   ├── FlightsService/                  # gRPC API, CRUD voli (Dockerfile incluso)
│   ├── GlobalSearchService/             # Global Search API — ATTUALMENTE UN MOCKUP (Dockerfile incluso)
│   └── TechInterview.Web/               # frontend Blazor Server (Dockerfile incluso)
├── tests/                 # progetti di test automatici (ancora vuota)
└── TechInterview.sln
```

## Stato di avanzamento

### Fatto

- **Riorganizzazione repo** in `src/` `docs/` `docker/` `tests/`.
- **GlobalSearchService (mockup)**: nuovo progetto, aggiunto a `TechInterview.sln` e
  come `ProjectReference` in `TechInterview.AppHost`. Espone già il contratto
  richiesto dalla consegna:
  - `GET /api/global-search?query=&offset=&limit=`
  - validazione `query` ≥ 3 caratteri (400 altrimenti)
  - clamping di `offset`/`limit`
  - risposta nella forma `{ items, offset, limit, count }`

  Ha già registrati (ma non usati) un `HttpClient` verso `AirportsService` e un
  client gRPC verso `FlightsService`. Risponde però sempre con dati statici — vedi
  i TODO in `src/GlobalSearchService/Services/MockGlobalSearchService.cs`.
- **AppHost.cs**: registra `globalsearchservice` con `WithReference`/`WaitFor` verso
  `airportsservice` e `flightsservice`; `webfrontend` referenzia anche
  `globalsearchservice`. Aggiunto `builder.AddDockerComposeEnvironment("env")`.
- **Docker Compose** (`docker/docker-compose.yml` + un `Dockerfile` per ciascun
  servizio containerizzabile): ambiente completo avviabile con `docker compose up
  --build` dalla cartella `docker/`, con Aspire Dashboard incluso per
  log/traccia/metriche. Dettagli e porte in `docker/README.md`.

### Da fare (il vero compito della consegna)

- [ ] **Logica di ricerca reale in `GlobalSearchService`**: sostituire
  `MockGlobalSearchService` con un'implementazione che interroghi davvero
  `AirportsService` (REST) e `FlightsService` (gRPC), filtri sui campi richiesti e
  applichi la paginazione in modo corretto.
- [ ] Decidere e documentare la strategia di paginazione (sulle fonti vs
  sull'aggregato) — vedi i TODO nel mockup per i trade-off.
- [ ] Collegare la pagina `TechInterview.Web/Components/Pages/Search.razor` (oggi
  un placeholder con dati finti) alla vera Global Search API.
- [ ] Bonus facoltativi non ancora affrontati: test automatici (cartella `tests/`
  pronta ma vuota), logging strutturato applicativo, eventuale caching/indicizzazione.
- [ ] Verificare che la solution compili (`dotnet build`) e che
  `docker compose up --build` funzioni: non è stato possibile validarlo in modo
  automatico in questa fase (ambiente di lavoro senza SDK .NET) — vedi
  `docker/README.md` per i dettagli.

## Decisioni prese

- **Global Search API**: realizzata come nuovo microservizio dedicato
  (`GlobalSearchService`) che aggrega dati da `AirportsService` (REST) e
  `FlightsService` (gRPC), invece di un'estensione di un progetto esistente.
- **Containerizzazione**: integrazione nativa di .NET Aspire per Docker Compose
  (`AddDockerComposeEnvironment` in `AppHost.cs`); il `docker-compose.yml` incluso è
  stato scritto a mano modellandolo sull'output atteso di `aspire publish`, da
  verificare/rigenerare con la Aspire CLI quando disponibile (vedi `docker/README.md`).
