# Note architetturali

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

### Da fare (il vero compito della consegna)

- [ ] **Logica di ricerca reale in `GlobalSearchService`**: sostituire
  `MockGlobalSearchService` con un'implementazione che interroghi davvero
  `AirportsService` (REST) e `FlightsService` (gRPC), filtri sui campi richiesti e
  applichi la paginazione in modo corretto.
- [ ] Decidere e documentare la strategia di paginazione (sulle fonti vs
  sull'aggregato) — vedi i TODO nel mockup per i trade-off, tenendo conto che
  `CachingGlobalSearchService` chiama il metodo con un `limit` molto ampio per
  ottenere l'insieme "completo" da cachare.
- [ ] Allineare il filtro di rifinitura usato dalla cache (oggi su `Description`)
  agli stessi campi grezzi usati dalla ricerca reale, se necessario.
- [ ] Collegare la pagina `TechInterview.Web/Components/Pages/Search.razor` (oggi
  un placeholder con dati finti) alla vera Global Search API.
- [ ] Bonus facoltativi non ancora affrontati: test automatici (cartella `tests/`
  pronta ma vuota), logging strutturato applicativo.
- [ ] Rendere configurabile l'ampiezza del fan-out con cui `SearchAsync` interroga
  `FlightsService` (numero di chiamate `GetFlights` in parallelo necessarie a coprire
  tutti i voli) invece di un valore fisso in codice — stesso pattern gia' usato per
  `GlobalSearchCache` in `appsettings.json`. Utile perche' il valore ottimale dipende
  dalle risorse assegnate a Docker Desktop e dalla macchina, quindi puo' avere senso
  poterlo ritarare senza ricompilare — vedi il benchmark qui sotto.
- [ ] **Resilienza gRPC su FlightsService**: lo `AddStandardResilienceHandler()` di
  `TechInterview.ServiceDefaults` copre solo i fallimenti di trasporto (connessione
  rifiutata, timeout di rete), perche' decide se ritentare guardando lo status code
  HTTP — ma un errore applicativo gRPC viaggia in una risposta HTTP 200 con l'esito
  vero nei trailer (`grpc-status`), invisibili a quella logica. Da valutare una retry
  policy nativa sul canale gRPC (`GrpcChannelOptions.ServiceConfig` con
  `MethodConfig`/`RetryPolicy`, che capiscono `UNAVAILABLE`/`DEADLINE_EXCEEDED` ecc.).
- [x] Verificare che `docker compose up --build` funzioni: testato con successo.
  Durante il test emersi e risolti due problemi di negoziazione HTTP/1.1 vs HTTP/2
  su `flightsservice` (unico servizio puramente gRPC, quindi il più sensibile
  all'argomento) — vedi la nuova voce in "Decisioni prese" qui sotto.

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
- **Ampiezza del fan-out verso FlightsService (bonus, per la futura `SearchAsync`
  reale)**: `FlightsServiceImpl` rigenera 1000 voli casuali a ogni singola chiamata
  (non e' registrato come singleton, a differenza di `AirportsRepository` che lo e' —
  quindi i dati non persistono tra chiamate, solo gli ID sono stabili perche'
  posizionali). Questo significa che l'unica lettura "coerente" ottenibile e' quella
  di un solo ciclo di fetch: la strategia scelta e' interrogare tutti i 1000 voli in
  un'unica ondata di chiamate `GetFlights` in parallelo (limit massimo consentito dal
  server: 100), trattando il risultato riassemblato come il proprio "istante"
  autoritativo per quella ricerca, da cachare cosi' come viene.
  Quante chiamate parallele conviene fare e' stato misurato empiricamente con
  `tools/Test-FlightsServiceLatency.ps1` (via grpcurl) invece che dedotto solo dalla
  formula del delay artificiale (`100 + 50*limit` ms in `FlightsServiceImpl.GetFlights`):
  il tempo totale scende al ridursi di `limit` fino a un minimo intorno a
  `limit=10-20`, poi risale (a `limit=5`, 200 chiamate concorrenti, il container non
  regge la generazione concorrente di cosi' tante istanze di 1000 voli e il tempo
  totale peggiora, con varianza molto piu' alta tra le chiamate). Valore scelto:
  **`limit=20`** — pari in media a `limit=10` ma con scarto minimo/massimo tra le
  chiamate piu' stretto e stabile su piu' run ripetuti, quindi piu' margine dalla zona
  di instabilita' osservata verso `limit=5`. E' un valore tarato su questo ambiente
  (risorse Docker Desktop, macchina locale), non una costante universale: da
  rivalutare se cambia l'hardware o l'allocazione di risorse al container.
