# Note architetturali

Questo documento descrive lo stato del progetto all'approccio iniziale (containerizzazione dell'ambiente fornito, primo mockup di `GlobalSearchService` con cache Redis gia' completa), fermo intenzionalmente a questo punto come riferimento. L'implementazione reale della Global Search API — inclusa la logica di ricerca, le decisioni di caching definitive e la parte di presentazione visiva aggiuntiva — e' stata realizzata e consegnata sul branch `prototype/real-search`; vedi `docs/architecture.md` su quel branch per i dettagli.

## Struttura del repository

```
TechInterview/
├── docs/                  # questo documento e altre note
├── docker/                # docker-compose.yml, README con istruzioni, .dockerignore
├── src/
│   ├── TechInterview.AppHost/           # orchestratore Aspire (+ AddDockerComposeEnvironment, +Redis)
│   ├── TechInterview.ServiceDefaults/   # telemetry, health check, resilienza condivisi
│   ├── AirportsService/                 # REST API, CRUD aeroporti (Dockerfile incluso)
│   ├── FlightsService/                  # gRPC API, CRUD voli (Dockerfile incluso)
│   ├── GlobalSearchService/             # Global Search API — MOCKUP + cache Redis gia' completa (Dockerfile incluso)
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

  Ha già registrati (ma non usati per interrogare le fonti dati) un `HttpClient`
  verso `AirportsService` e un client gRPC verso `FlightsService`. Il risultato di
  base resta statico — vedi i TODO in
  `src/GlobalSearchService/Services/MockGlobalSearchService.cs`.
- **Cache Redis (bonus)**: `CachingGlobalSearchService` (decorator su
  `IGlobalSearchService`) aggiunge una cache Redis completa davanti al mockup,
  senza che quest'ultimo debba saperne nulla:
  - riuso dei risultati per **sottostringa**: se la query richiesta contiene come
    sottostringa una query già cacheata, si riusa il set di risultati già scaricato
    e lo si filtra ulteriormente in memoria, senza richiamare la fonte dati (la
    stessa proprietà copre anche il caso di hit esatto, e regala gratis lo
    short-circuit sui risultati vuoti: una query nota per non avere match rende
    automaticamente vuota anche ogni query più specifica);
  - un SET Redis (`globalsearch:known-queries`) traccia quali query sono
    attualmente cacheate, per poter fare lo scan delle sottostringhe; le entry
    scadute vengono ripulite pigramente dal set alla prima lettura fallita;
  - scadenza **sliding + tetto assoluto** (default 5 min / 30 min, configurabili
    in `appsettings.json` sotto `GlobalSearchCache`): una query molto richiesta
    resta in cache rinnovandosi ad ogni lettura, ma non oltre il tetto assoluto.

  Essendo un decorator, quando sostituirai `MockGlobalSearchService` con la
  ricerca reale la cache continuerà a funzionare senza modifiche — vedi la nota
  nel file su cosa verificare (il filtro di rifinitura sul superset cacheato oggi
  lavora sul campo `Description`, andrebbe riallineato agli stessi campi grezzi
  usati dalla ricerca reale).
- **AppHost.cs**: registra `globalsearchservice` con `WithReference`/`WaitFor` verso
  `airportsservice`, `flightsservice` e ora anche la risorsa Redis (`cache`);
  `webfrontend` referenzia anche `globalsearchservice`. Aggiunto
  `builder.AddDockerComposeEnvironment("env")`.
- **Docker Compose** (`docker/docker-compose.yml` + un `Dockerfile` per ciascun
  servizio containerizzabile): ambiente completo avviabile con `docker compose up
  --build` dalla cartella `docker/`, con Aspire Dashboard per log/traccia/metriche,
  Redis per la cache, e Redis Commander per ispezionare le chiavi cacheate a mano.
  Porte host parametrizzabili via `docker/.env`. Dettagli, porte e comandi di verifica in `docker/README.md`.
- **Reflection gRPC su FlightsService** (solo Development): consente a client generici
  (grpcurl, grpcui, Postman/Insomnia) di scoprire ed esplorare i servizi/metodi esposti
  senza importare a mano `Protos/flights.proto` — l'equivalente, per gRPC, di quello che
  Scalar/OpenAPI offrono alle REST API. Esempi in `docker/README.md`.

### Seguito di questo lavoro

Le voci di lavoro elencate in questa sezione in una versione precedente (logica di ricerca reale, strategia di paginazione, resilienza gRPC, connessione della UI alla ricerca vera, ecc.) sono state realizzate — con le decisioni definitive documentate in dettaglio — sul branch `prototype/real-search`. Vedi `docs/architecture.md` su quel branch.

## Decisioni prese

- **Global Search API**: realizzata come nuovo microservizio dedicato
  (`GlobalSearchService`) che aggrega dati da `AirportsService` (REST) e
  `FlightsService` (gRPC), invece di un'estensione di un progetto esistente.
- **Containerizzazione**: integrazione nativa di .NET Aspire per Docker Compose
  (`AddDockerComposeEnvironment` in `AppHost.cs`); il `docker-compose.yml` incluso è
  stato scritto a mano modellandolo sull'output atteso di `aspire publish`, da
  verificare/rigenerare con la Aspire CLI quando disponibile (vedi `docker/README.md`).
- **Caching (bonus)**: Redis con pattern decorator, riuso dei risultati per
  sottostringa e scadenza sliding+assoluta, invece di un semplice `IMemoryCache` —
  scelta guidata dalla possibilità di condividere la cache tra più repliche del
  servizio in uno scenario reale (con una sola istanza, come in questo esercizio,
  un `IMemoryCache` locale avrebbe lo stesso effetto pratico ma non sarebbe
  condiviso in caso di scaling orizzontale).
- **FlightsService, healthcheck vs gRPC (Docker)**: `flightsservice` è l'unico
  servizio puramente gRPC (nessuna rotta REST), quindi in chiaro (senza TLS/ALPN,
  come nei container Docker di questo esercizio) Kestrel sceglie di default
  HTTP/2 puro sulla porta principale — corretto per i client gRPC, ma incompatibile
  con l'healthcheck Docker (curl, HTTP/1.1). Il tentativo di "unificare" le due cose
  forzando `Http1AndHttp2` sulla stessa porta *sembra* funzionare (l'healthcheck
  passa) ma introduce errori intermittenti lato client gRPC
  (`HTTP_1_1_REQUIRED`), perché senza ALPN Kestrel non ha un modo affidabile di
  negoziare i due protocolli sulla stessa connessione cleartext. Soluzione adottata
  in `src/FlightsService/Program.cs`: porta gRPC principale (8080) resta HTTP/2
  puro; una seconda porta dedicata (8090, solo interna al container, non
  pubblicata sull'host) parla solo HTTP/1.1 ed è usata esclusivamente
  dall'healthcheck (`FLIGHTS_HEALTH_PORT` in `docker-compose.yml`). Attiva solo
  quando quella variabile d'ambiente è impostata, quindi l'esecuzione locale via
  Aspire (F5) non è toccata.
- **Paginazione oltre la fine dei risultati (robustezza)**: un `offset`/`limit` che
  punta oltre l'ultimo risultato disponibile e' fisiologico con la paginazione web
  (basta cliccare "successivo" abbastanza volte), non un errore dell'utente — quindi
  **non genera un errore HTTP**: si risponde comunque `200 OK` con `items: []`,
  `offset`/`limit` echeggiati cosi' come richiesti, e `count` = totale reale dei
  match su entrambe le fonti (non il conteggio della pagina corrente). E' coerente
  con tre cose gia' presenti nel codice: `AirportsRepository`/`FlightsServiceImpl`
  fanno gia' `.Skip(offset).Take(limit)`, che oltre la fine di una lista .NET
  restituisce una sequenza vuota senza eccezioni; `MockGlobalSearchService` usa gia'
  `Count = mockItems.Count` (totale, non conteggio pagina) sullo stesso principio di
  `totalCount` di Airports/Flights; e l'unico errore previsto dal contratto (400 per
  query < 3 caratteri) resta riservato a input davvero non valido, non a un input
  valido che semplicemente non produce risultati in quella posizione. Il client (la
  UI paginata) rileva la fine confrontando `offset` con `count`, senza bisogno di
  intercettare un errore. Nota anche sull'ordine: l'esempio nel README.md e lo stub
  `MockGlobalSearchService` mettono gli **Airports prima dei Flights** negli
  `items` — ordine da rispettare se ci si vuole attenere al prototipo fornito.
