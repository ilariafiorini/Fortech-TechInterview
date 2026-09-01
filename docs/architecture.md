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
│   ├── GlobalSearchService/             # Global Search API — implementazione reale + cache Redis (Dockerfile incluso)
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

### Limitazioni note e possibili sviluppi futuri

Il branch `prototype/real-search` è la versione consegnata: quanto segue non è un
elenco di cose ancora necessarie per completare la consegna, ma un memo dei punti
lasciati consapevolmente aperti, con la ragione per cui non sono stati affrontati.

- **Filtro di rifinitura della cache limitato a `Description`**:
  `CachingGlobalSearchService` filtra il superset cacheato solo su questo campo
  (vedi il TODO nel file stesso); allinearlo agli stessi campi grezzi usati dalla
  ricerca reale è un affinamento possibile, non necessario alla scala di questo
  esercizio.
- **Nessun logging strutturato applicativo**: i servizi si appoggiano solo alla
  telemetria di default di `TechInterview.ServiceDefaults` (OpenTelemetry/health
  check), senza `ILogger` applicativo dedicato nei punti di business logic.
- **Resilienza gRPC solo a livello di trasporto**: `AddStandardResilienceHandler()`
  copre i fallimenti di connessione/timeout di rete, ma non gli errori applicativi
  gRPC che arrivano in una risposta HTTP 200 con l'esito vero nei trailer
  (`grpc-status`) — servirebbe una retry policy nativa sul canale
  (`GrpcChannelOptions.ServiceConfig`) per coprire anche quel caso.
- **Invalidazione della cache ad ogni nuova ricerca**: rimasta una scelta
  esplicitamente aperta (vedi "Strategia di caching per fonte" in "Decisioni
  prese") tra cancellazione esplicita ed expiry via TTL; oggi ci si affida al TTL,
  gia' costruito e sufficiente alla scala di questo esercizio.

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
- **Strategia di caching per fonte, Airports vs Flights (bonus)**: decisione finale
  su cosa cachare e per quanto tempo, distinta per fonte in base a quanto si sa della
  loro reale persistenza. Le due politiche sono cosi' diverse (nessun riuso per
  sottostringa vs riuso con TTL infinito sul superset, refresh completo a ogni ricerca
  vs seeding una tantum) che si e' scelto di modellarle come **due componenti di
  caching interni dedicati e distinti** (uno per Flights, uno per Airports), invece di
  forzarle dentro un'unica astrazione generica — entrambi interni alla futura
  implementazione reale di `SearchAsync`, e concettualmente separati dal decorator
  `CachingGlobalSearchService` gia' esistente (che lavora un livello sopra, sui
  risultati gia' mergiati delle due fonti).
  - **Flights**: nessuna garanzia nelle specifiche che i dati restino coerenti nel
    tempo (vedi la voce sul fan-out qui sopra), quindi ogni nuova ricerca (nuova
    stringa + "cerca") ripete da zero l'intero ciclo: sweep completo dei 1000 voli in
    parallelo (`limit=20`), filtro sulla stringa cercata, e si cachea **solo l'elenco
    dei risultati filtrati** nella sua interezza — non il superset grezzo dei 1000.
    Paginazione e apertura della scheda di dettaglio del singolo volo lavorano
    esclusivamente su questo elenco cacheato, sullo stesso pattern gia' visto nella
    pagina di esempio Flights. Nota: questo significa che per Flights il riuso per
    sottostringa di `CachingGlobalSearchService` non ha materiale su cui lavorare a
    valle di questa cache interna (non tenendo il superset grezzo, una ricerca piu'
    specifica non puo' rifinire quella precedente e deve rifare il giro completo) —
    scelta consapevole, coerente col non voler assumere una finestra di coerenza che
    le specifiche non garantiscono.
  - **Airports**: essendo statici per tutta la vita del servizio (`AirportsRepository`
    e' singleton, vedi sopra), l'intera tabella dei 300 aeroporti viene cacheata sotto
    la chiave **stringa vuota ("")** — ma con la **stessa scadenza sliding + tetto
    assoluto** di tutte le altre entry, non un caso speciale a TTL infinito — anche
    lei deve poter scadere se nessuno la usa piu' per un po', coerentemente col resto
    della cache. Il seeding di "" e' **lazy e auto-riparante**: ad ogni
    ricerca si controlla se "" e' gia' in cache; se manca (primo avvio, oppure e'
    scaduta per inattivita' prolungata) la si crea li' per li', durante lo stesso
    fetch che serve comunque alla ricerca corrente: si scarica l'intera lista dei 300
    aeroporti una volta sola e la si usa per popolare **sia** la entry "" (il
    superset completo) **sia** la entry della query effettivamente cercata (il
    sottoinsieme filtrato), evitando una doppia chiamata ad `AirportsService`. Se ""
    esiste gia', si salta il fetch completo e si passa direttamente all'esplorazione
    delle chiavi cacheate.
    Per ogni nuova query si esplora l'insieme delle chiavi gia' cacheate e si sceglie
    quella **piu' lunga tra quelle che sono sottostringa della query richiesta** (il
    sottogruppo gia' cacheato piu' vicino/specifico a quanto serve, quindi il piu'
    piccolo da filtrare ulteriormente); "" e' sempre sottostringa di qualunque query,
    quindi funge da fallback universale quando non esiste ancora nulla di piu'
    specifico. Si filtra quel sottogruppo sorgente per ottenere il nuovo elenco, lo si
    cachea come nuova entry (con la propria TTL sliding+assoluta) e si rinnova la TTL
    **solo della entry sorgente effettivamente usata** per derivarlo — non di tutte le
    altre entry cacheate. Paginazione e dettaglio lavorano solo sul nuovo sottogruppo.
    Nota: questa stessa regola di rinnovo copre automaticamente anche "" ogni volta
    che e' lei la entry sorgente usata (es. nessuna sottostringa piu' specifica gia'
    cacheata) — non serve alcuna logica dedicata per lei: e' un caso normale
    dell'algoritmo generale, non un'eccezione. Questo elimina sia il caso speciale del
    TTL infinito sia la necessita' di un seeding eager all'avvio del servizio (con la
    relativa gestione di `AirportsService` non ancora pronto) discussi in precedenza:
    l'unico costo residuo e' che, se "" e' scaduta per lunga inattivita', la ricerca
    successiva paga una volta il fetch completo — accettabile, dato che i dati non
    cambiano comunque nel frattempo.
    Punti da tenere presenti quando si implementa:
    - **confermato**: anche l'endpoint REST di `AirportsService`
      (`GET /api/airports?offset=&limit=`) impone lo stesso contratto di
      `FlightsService` — `limit<=0` diventa 50, `Math.Min(limit,100)` come tetto,
      stesso ritardo artificiale `100 + 50*limit` ms (vedi `AirportsController.cs` /
      `AirportsRepository.GetPagedAsync`) — quindi serve comunque un fetch a piu'
      pagine in parallelo per ottenere tutti i 300 record (minimo 3 chiamate a
      `limit=100`), riusando lo stesso schema gia' scelto per Flights.
      A differenza di Flights pero' `AirportsRepository` e' singleton: i 300
      aeroporti sono generati **una sola volta all'avvio del container**, non ad ogni
      chiamata, quindi il ritardo per-chiamata e' un puro artificio di latenza e non
      nasconde alcun costo di rigenerazione concorrente — l'instabilita' vista nel
      benchmark di Flights (contesa a `limit` molto basso, tanti processi che
      rigenerano 1000 voli in parallelo) non dovrebbe presentarsi qui allo stesso
      modo — confermato empiricamente con `tools/Test-AirportsServiceLatency.ps1`
      (HttpClient nativo invece di grpcurl, dato che qui non serve un tool esterno):
      su piu' run ripetuti, il tempo totale dello sweep scende in modo **monotono e
      pulito** al ridursi di `limit`, nessuna zona di instabilita' — `limit=100` (3
      chiamate) ~5.1s, `limit=75` ~3.86s, `limit=50` ~2.61s, `limit=25` ~1.36s,
      `limit=10` (30 chiamate) ~0.63s, zero fallite in ogni run. I numeri coincidono
      quasi esattamente con la sola formula `100 + 50*limit` (es. `limit=10` ->
      600ms teorici contro ~615-620ms osservati), a conferma che in parallelo il
      collo di bottiglia e' solo quel ritardo artificiale per-chiamata, non ce n'e'
      un altro nascosto. Dato che il seeding di Airports avviene raramente (una
      tantum, o dopo una lunga inattivita' — non a ogni ricerca come per Flights),
      la scelta del `limit` qui pesa molto meno sull'esperienza complessiva: si puo'
      tranquillamente scendere sotto `limit=10` per spremere ulteriormente il tempo
      di seeding (la formula suggerisce che si continuerebbe a scendere verso il
      minimo teorico di 100ms, anche se con overhead reale di connessione/thread non
      catturato dalla formula, mai misurato oltre `limit=10` in questo test), oppure
      restare su `limit=100` per semplicita' di codice (solo 3 chiamate, nessuna
      logica di fan-out da scrivere) accettando i ~5s una tantum.
      **Decisione presa: `limit=20`**, lo stesso valore gia' scelto per Flights — non
      il piu' veloce possibile ne' il piu' semplice, ma evita di avere due valori di
      fan-out diversi tra le due fonti senza un vero motivo. E' un valore sicuro:
      cade tra `limit=25` e `limit=10`, entrambi testati con zero fallite e nessun
      segno di instabilita' nel benchmark sopra (15 chiamate, ~1.1s attesi dalla
      formula).
    - questa cache di sottostringhe lavora sui dati grezzi di una sola fonte (il
      superset dei 300 aeroporti): e' concettualmente un livello piu' interno e
      distinto rispetto a `CachingGlobalSearchService`, che invece cachea/riusa per
      sottostringa i risultati gia' **mergiati** (Airports+Flights) del Global Search
      finale. I due livelli possono convivere, ma vale la pena tenerli concettualmente
      separati per non confonderli in fase di debug.
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
  - **Aperto**: se, a ogni nuova ricerca, eliminare esplicitamente dalla cache l'elenco
    filtrato della ricerca precedente oppure lasciare che scada da solo via TTL. Nota:
    la cache attuale e' condivisa e chiave per testo della query, non per sessione
    utente — quindi non esiste un vero e proprio "elenco della ricerca precedente di
    questo utente" da cancellare in modo pulito, a meno di introdurre chiavi di cache
    scoped per sessione. Finche' resta condivisa, lasciar scadere via TTL sembra il
    meccanismo piu' naturale (gia' costruito), mentre una cancellazione esplicita
    avrebbe senso solo per limitare attivamente la crescita della cache, a
    prescindere dal concetto di "ricerca precedente".

## Implementazione della Global Search API reale (questo branch)

Questo branch, staccato da `main`, contiene l'implementazione completa e consegnata
della vera logica di ricerca, secondo tutte le decisioni discusse e documentate in
questo file. `main` resta invariato come riferimento per discutere l'approccio
iniziale (containerizzazione dell'ambiente fornito, primo mockup funzionante con
cache Redis); l'implementazione reale della Global Search API, descritta qui sotto,
e' quanto viene presentato come consegna.

### Oltre il quesito richiesto: la parte di presentazione visiva

Il quesito dell'esercizio richiede un singolo endpoint (`GET /api/global-search`)
che restituisca risultati eterogenei paginati. Questa consegna va oltre quel
perimetro: include anche un frontend Blazor Server (quattro pagine di ricerca — All/
Airports/Flights, con filtro per tipo e paginazione indipendente per scheda) e una
gestione del dettaglio dei singoli risultati che resta coerente e persistente
rispetto alla ricerca che li ha prodotti, invece di essere ricostruita a ogni
apertura da una nuova interrogazione ai servizi sorgente.

Questa parte non era richiesta dal quesito essenziale: nasce dalla curiosità di
spingere oltre sia l'esperienza di sviluppo con Claude (prima volta con accesso
diretto al codice, non solo prompt-e-incolla), sia lo sfruttamento della cache Redis
già costruita per il solo endpoint — gli stessi dati cacheati per rispondere alla
ricerca si prestano naturalmente a una visualizzazione per parti (le tre schede
separate) e persistente (il dettaglio letto dalla stessa riga cacheata, non da una
nuova chiamata dal vivo). La tabella qui sotto separa i file essenziali alla sola
risoluzione del quesito da quelli dedicati a questa parte aggiuntiva.

### Elenco dei file, per categoria

**Materiale originale della sfida** (fornito con il test, non modificato nella sua
logica):
`src/AirportsService/`, `src/FlightsService/`, `src/TechInterview.Web/Components/
Pages/{Home,Counter,Error,Airports,Flights}.razor`, `App.razor`, `Routes.razor`,
`_Imports.razor`, `MainLayout.razor(.css)`.

**File essenziali per l'endpoint Global Search richiesto dall'esercizio**:

- `src/GlobalSearchService/Controllers/GlobalSearchController.cs` — solo l'azione
  `GetGlobalSearch`; le altre due azioni di dettaglio cacheato appartengono alla
  categoria successiva.
- `src/GlobalSearchService/Services/{RealGlobalSearchService,AirportsSearchCache,
  IAirportsSearchCache,FlightsSearchCache,IFlightsSearchCache,
  CachingGlobalSearchService,IGlobalSearchService,MockGlobalSearchService,
  GlobalSearchCacheOptions,SearchFanOutOptions}.cs`
- `src/GlobalSearchService/Models/*.cs`, `Program.cs`, `GlobalSearchService.csproj`,
  `Dockerfile`, `Protos/flights.proto`, `appsettings*.json`
- `src/TechInterview.AppHost/`, `src/TechInterview.ServiceDefaults/`
  (orchestrazione/infrastruttura condivisa)
- `tests/GlobalSearchService.Tests/Services/*.cs`, gran parte di `TestDoubles/`
- `tests/GlobalSearchService.IntegrationTests/` (per gli scenari sull'endpoint di
  ricerca)

**File per la parte di presentazione visiva aggiuntiva** (bonus, non richiesto dal
quesito essenziale):

- `src/TechInterview.Web/Components/Pages/{Search,SearchAll,SearchAirports,
  SearchFlights}.razor`
- `src/TechInterview.Web/Components/Shared/{SearchResultsTable.razor,
  SearchNavigation.cs,SearchDtos.cs}`
- `src/TechInterview.Web/Components/Pages/{AirportDetail,FlightDetail}.razor`
  (riscritte per leggere dal dettaglio cacheato invece che dal vivo)
- `src/TechInterview.Web/Components/Layout/NavMenu.razor(.css)`
- Le due azioni di dettaglio cacheato in `GlobalSearchController.cs`
  (`GetCachedAirportById`, `GetCachedFlightById`)
- `tests/GlobalSearchService.Tests/Controllers/GlobalSearchControllerTests.cs`
  (per la parte relativa al dettaglio cacheato)
- Alcuni scenari di `tests/GlobalSearchService.IntegrationTests/` (stabilità del
  dettaglio su letture ripetute)
- `docker-compose.yml`/`.env`: i servizi `grpcui` e `redis-commander` (esplorazione
  gRPC e ispezione Redis, entrambi non richiesti dal quesito)

Cosa contiene, rispetto a `main`:

- `Services/AirportsSearchCache.cs` (+ `IAirportsSearchCache.cs`): cache dedicata ad
  Airports. Seeding lazy/auto-riparante della chiave `""` (l'intero superset), scelta
  della sottostringa cacheata piu' lunga compatibile con la query richiesta, fallback su
  `""` quando non c'e' nulla di piu' specifico, rinnovo della TTL sfruttando il
  comportamento nativo di `IDistributedCache` su Redis (una lettura andata a buon fine
  rinnova gia' la scadenza sliding della entry letta, quindi "rinnova solo la entry
  sorgente usata" non richiede codice dedicato).
- `Services/FlightsSearchCache.cs` (+ `IFlightsSearchCache.cs`): cache dedicata a
  Flights. Cache-aside pura chiavata sulla query: su un hit si riusa il risultato gia'
  cristallizzato (navigazione della stessa ricerca); su un miss si rifa' per intero lo
  sweep parallelo su `FlightsService` e si cachea solo l'elenco filtrato, mai il superset
  grezzo.
- `Services/RealGlobalSearchService.cs`: aggrega le due cache sopra, ordine
  Airports-poi-Flights (vedi il prototipo di risposta in `README.md`), paginazione con
  `Skip`/`Take` sull'elenco concatenato — un offset oltre la fine restituisce
  naturalmente un elenco vuoto, nessun errore dedicato.
- `Services/SearchFanOutOptions.cs`: `PageLimit` (default 20) configurabile da
  `appsettings.json`, sezione `SearchFanOut`, usato da entrambe le cache per il fan-out
  verso le rispettive fonti.
- `Models/AirportDto.cs`, `Models/AirportsPageResponse.cs`, `Models/FlightDto.cs`: DTO
  locali per non serializzare in cache ne' i tipi generati da Protobuf ne' i modelli
  dei progetti sorgente (`GlobalSearchService` non li referenzia direttamente).
- `Program.cs` (GlobalSearchService): registra le due cache e sostituisce
  `MockGlobalSearchService` con `RealGlobalSearchService` come implementazione avvolta da
  `CachingGlobalSearchService` — quest'ultimo resta invariato, esattamente come previsto.
- `Components/Pages/Search.razor` (TechInterview.Web): riscritta per interrogare
  `/api/global-search` sul serio, con paginazione previous/next sullo stesso pattern di
  `Airports.razor`/`Flights.razor`, e link alle pagine di dettaglio esistenti
  (`/airports/{id}`, `/flights/{id}`) in base a `resourceType`.
- `tests/GlobalSearchService.Tests/`: test unitari (xUnit + Moq) sulla logica isolabile —
  `AirportsSearchCache`, `FlightsSearchCache`, `RealGlobalSearchService`,
  `CachingGlobalSearchService`, e le due azioni di `GlobalSearchController` che leggono
  il dettaglio dalla cache di ricerca (`GetCachedAirportById`/`GetCachedFlightById`) — con
  HTTP/gRPC/Redis sostituiti da doppi di test in `TestDoubles/` (un finto
  `HttpMessageHandler` per Airports, un `IConnectionMultiplexer` mockato per il set
  known-keys — con una variante, `MultiKeyRedisTestFactory`, che tiene un set Redis
  DISTINTO per ogni chiave anziche' uno condiviso, indispensabile per verificare che i
  bucket di `CachingGlobalSearchService` restino davvero separati — un `IDistributedCache`
  in memoria, un client gRPC mockato secondo il pattern documentato da Microsoft), quindi
  eseguibili senza Docker. Copre il seeding lazy/self-healing di Airports, il cache-aside
  puro di Flights, le tre decisioni di aggregazione di `RealGlobalSearchService` (ordine
  Airports-poi-Flights, count come totale, offset oltre la fine), che i bucket di
  `CachingGlobalSearchService` non condividano cache ne' known-queries tra loro (nemmeno
  quando il riuso per sottostringa potrebbe far "trapelare" un bucket nell'altro), e la
  validazione/ricerca-per-id delle due azioni di dettaglio cacheato.
- `tests/GlobalSearchService.IntegrationTests/`: test "dal vivo" contro la stack
  Docker Compose realmente avviata (nessun mock, nessun riferimento ai tipi interni:
  black-box sul contratto HTTP), progetto separato e volutamente non referenziato in
  `TechInterview.sln` cosi' un `dotnet test` normale non lo tocca mai per sbaglio.
  Copre cose che gli unit test, mockando Redis/HTTP/gRPC, non possono vedere: che lo
  stack si assembli davvero (health check), la validazione end-to-end, l'ordinamento
  e la paginazione oltre la fine sui dati reali, che la cache Redis produca un guadagno
  di velocita' misurabile sulla stessa query ripetuta, e — end-to-end, cercando davvero e
  poi aprendo il dettaglio della riga trovata — che il dettaglio cacheato di un aeroporto/
  volo coincida esattamente con la riga della lista che lo ha generato, restando stabile
  su letture ripetute (il comportamento che ha risolto il bug del dettaglio voli non
  persistente). Vedi il README del progetto per il dettaglio e per come i test evitano di
  assumere un contenuto preciso dei dati mock (Flights li rigenera casuali ad ogni
  chiamata).

### Estensione: ricerca multi-pagina con filtro per tipo (`resourceType`)

Su richiesta esplicita, il frontend e' stato ampliato per offrire tre viste distinte del
risultato di una ricerca (Globale, Aeroporti, Voli), ciascuna in una pagina intera e
paginata separatamente, invece dell'unica tabella mista che c'era prima. La discussione
che ha preceduto la scrittura del codice (niente popup, quattro pagine invece di
schede/tab in un'unica pagina, un endpoint solo con parametro opzionale invece di nuovi
microservizi, stato in query string invece che lato server) e' riportata qui perche' le
alternative scartate sono altrettanto importanti della scelta finale.

**Backend — `resourceType` opzionale su `/api/global-search`.** L'endpoint accetta ora
un parametro facoltativo `resourceType` (`null` | `"airport"` | `"flight"`), validato dal
controller (400 su qualunque altro valore) e propagato fino a `RealGlobalSearchService`,
che salta del tutto l'interrogazione della cache esclusa invece di interrogarla comunque
e filtrare dopo (nessuno spreco di lavoro sulla fonte non richiesta). E' stata
scartata l'idea di due nuovi microservizi dedicati (uno per la paginazione dei soli
aeroporti, uno per i soli voli): avrebbero duplicato la logica di aggregazione e di
cache gia' presente in `GlobalSearchService` senza alcun beneficio, per un requisito che
si esaurisce nell'aggiungere un filtro a valle. E' stata scartata anche l'idea di un
secondo endpoint parallelo, a favore di un unico endpoint retrocompatibile: omettendo il
parametro il comportamento resta identico a prima, quindi il vincolo della consegna (un
solo endpoint di ricerca globale) resta rispettato.

Questa scelta ha una conseguenza sulla cache: `CachingGlobalSearchService` ora tiene
elenchi e known-queries separati per "bucket" (`all` | `airport` | `flight`), quindi le
chiavi Redis sono cambiate da `globalsearch:results:<query>` a
`globalsearch:results:<bucket>:<query>` (stesso discorso per `known-queries`). E' una
modifica di formato che rompe la compatibilita' con le chiavi gia' in cache, accettabile
qui perche' si tratta di una cache di sviluppo con TTL, senza alcuna necessita' di
migrazione: le vecchie chiavi scadranno da sole e non verranno piu' lette.

**Frontend — quattro pagine invece di una.** `Search.razor` e' stata ridotta al solo
modulo di ricerca (campo + pulsante "Cerca"); i risultati vivono in tre pagine nuove,
`SearchAll.razor` (`/search/all`), `SearchAirports.razor` (`/search/airports`,
`resourceType=airport`) e `SearchFlights.razor` (`/search/flights`,
`resourceType=flight`), raggiungibili dai sottopulsanti della sidebar. E' stata
scartata l'alternativa di tre schede/tab dentro un'unica pagina (la richiesta iniziale),
sia perche' un riesame del frontend esistente ha mostrato che il pattern di navigazione
gia' in uso ovunque nel progetto e' la pagina intera, non il popup/tab, sia perche' su
richiesta esplicita si e' scelto di restare coerenti con quel pattern piuttosto che
introdurne uno nuovo. Le tre pagine condividono lo stesso componente di tabella +
paginazione (`Components/Shared/SearchResultsTable.razor`) e collegano ogni riga alla
pagina di dettaglio gia' esistente (`/airports/{id}`, `/flights/{id}`), mai a un popup.

**Stato condiviso nella query string (`SearchNavigation.cs`).** Le quattro pagine
condividono un unico schema di query string — `query`, `allLimit`, `airportsLimit`,
`flightsLimit`, `allOffset`, `airportsOffset`, `flightsOffset` — incapsulato in
`Components/Shared/SearchNavigation.cs` (un `record` `State` con gli helper statici
`ReadFrom(NavigationManager)` e `BuildUrl(path, state)`). Le regole concordate:

- una nuova ricerca da `Search.razor` azzera tutti e tre gli offset e apre sempre la
  vista "All";
- passare da una scheda all'altra tramite i sottopulsanti della sidebar (senza una nuova
  ricerca) NON azzera nulla: ciascuna scheda ricorda il proprio ultimo offset in modo
  indipendente, cosi' tornandoci sopra si ritrova l'ultima pagina vista;
- l'ultima query digitata resta sempre precompilata quando si torna sulla pagina di
  ricerca;
- una pagina di risultati raggiunta senza query nell'URL (es. link diretto o refresh
  dopo aver svuotato la barra degli indirizzi) non interroga il backend: mostra un
  messaggio "nessuna ricerca in corso" con un link per tornare alla ricerca.

E' stata valutata e scartata l'alternativa di un servizio di stato lato server
(`Scoped`, vivo per la sessione del circuito Blazor): avrebbe funzionato altrettanto
bene finche' la pagina resta aperta, ma si sarebbe perso tutto a un refresh o
condividendo un link, perche' quello stato non lo si sarebbe piu' saputo ricostruire.
La query string, gia' usata da `Airports.razor`/`Flights.razor` per lo stesso motivo, e'
stata preferita per coerenza con quella convenzione: ogni pagina e' interamente
ricostruibile dal solo URL.

**`returnUrl` sulle pagine di dettaglio.** `AirportDetail.razor` e `FlightDetail.razor`
accettano ora un parametro opzionale `returnUrl` in query string: se presente (ed e' un
path locale valido — vedi `IsSafeLocalUrl`, che rifiuta URL assoluti e protocol-relative
`//host/...` per evitare un open redirect), il pulsante "Back" ci naviga sopra invece che
tornare sempre alla lista semplice (`/airports` o `/flights`). Ogni link generato da
`SearchResultsTable.razor` porta con se' un `returnUrl` che punta esattamente alla
scheda e alla pagina di risultati corrente (query + tutti e tre gli offset), cosi' "Back"
da un dettaglio aperto da una ricerca riporta l'utente sulla stessa scheda e sulla stessa
pagina paginata da cui era partito, non genericamente all'inizio della lista.

**Il dettaglio legge dalla cache di ricerca, non dal microservizio dal vivo.** Durante il
test manuale e' emerso che il dettaglio di un volo cambiava a ogni click: `FlightsService`
rigenera 1000 voli casuali a ogni singola chiamata gRPC (nessun lifetime esplicito su
`FlightsServiceImpl`, quindi ASP.NET ne crea un'istanza nuova per ogni chiamata), e
`FlightDetail.razor` interrogava quel servizio dal vivo a ogni apertura — bypassando del
tutto la cache che invece "congela" i risultati di una ricerca. Il dettaglio, quindi, non
era affatto lo screenshot della riga cliccata: era una fotografia nuova, presa nel momento
del click, di un mondo gia' cambiato nel frattempo.

La soluzione adottata riusa la cache di ricerca gia' esistente invece di introdurne una
nuova o di toccare `FlightsServiceImpl`/`AirportsRepository` (scaffolding di base,
volutamente lasciato intatto): `IFlightsSearchCache`/`IAirportsSearchCache.GetMatchesAsync`
gia' salvano, sotto la chiave esatta della query normalizzata, l'intero elenco filtrato
usato per costruire la lista — è la stessa richiesta che ha appena renderizzato la riga
cliccata, quindi una lettura immediatamente successiva e' quasi sempre un cache hit
sull'identico dato. Due nuove azioni su `GlobalSearchController`
(`GET api/global-search/airports/{id}?query=...` e `GET api/global-search/flights/{id}?query=...`)
espongono questa lettura: normalizzano la query (obbligatoria qui, a differenza del
parametro opzionale di `Search`, perche' senza di essa non c'e' alcuna cache da
interrogare), richiamano la cache della fonte richiesta, e restituiscono la riga con
quell'id (404 se quella query non la conteneva). `FlightDto` e' stato esteso con
`DepartureTime`/`ArrivalTime` (mancavano: servivano solo al confronto testuale di
`MatchesQuery`, non al dettaglio) — `AirportDto` aveva gia' tutti i campi necessari,
nessuna estensione richiesta li'.

`SearchResultsTable.razor` porta ora anche la query corrente (oltre al `returnUrl` gia'
discusso sopra) in ogni link di dettaglio generato. `AirportDetail.razor` e
`FlightDetail.razor` la leggono e, se presente, provano prima questo nuovo endpoint;
solo se manca (accesso diretto o da preferiti, senza alcun contesto di ricerca) o se la
query non conteneva quell'id, ricadono sulla chiamata dal vivo al microservizio
proprietario — lo stesso comportamento, invariato, di prima di questa estensione.
`FlightDetail.razor` unifica le due fonti possibili (la riga cacheata e la risposta gRPC
dal vivo) in un piccolo modello di vista locale (`FlightViewModel`), cosi' il markup non
deve sapere da quale delle due arrivano i dati.

Perche' questa soluzione e non le alternative discusse (una cache dedicata per-id con
TTL, oppure incorporare l'intera riga nel link stesso): una cache per-id condivisa da
tutte le richieste avrebbe introdotto un'incoerenza piu' subdola di quella originale — a
parita' di id, "vince" il primo sweep che la popola, quindi due ricerche diverse che
intercettano lo stesso id in momenti diversi potrebbero mostrare, per lo stesso click,
contenuti diversi da quelli della propria lista, in modo silenzioso e imprevedibile.
Riusare la cache-per-query gia' esistente da' invece la stessa garanzia forte
dell'incorporare i dati nel link (il dettaglio corrisponde sempre esattamente alla riga
di quella specifica lista), senza appesantire i link e senza inventare alcuna
infrastruttura di caching nuova.

Estendere questo trattamento anche ad Airports (dove, guardando il codice sorgente di
`AirportsService`, il problema di per se' non esiste: `AirportsRepository` e' un
singleton che genera i dati una sola volta) e' stata una scelta deliberata: il vincolo
del test era di dedurre il comportamento dei due servizi in modo empirico/black-box, non
di leggerne il codice sorgente. Da un punto di vista black-box, gli aeroporti *sembrano*
stabili quanto osservato finora, ma nessun contratto lo garantisce per l'intera durata di
una sessione — trattarli allo stesso modo dei voli evita di fondare una scelta di design
su un dettaglio implementativo che, a rigore, non si sarebbe dovuto usare come base.

Perche' i due nuovi endpoint vivono su `GlobalSearchService` e non su due nuovi
microservizi dedicati: non possiedono alcun dato nuovo, leggono soltanto una riga da una
cache che `GlobalSearchService` gia' costruisce e gia' sa interpretare (chiavi Redis,
formato serializzato, normalizzazione della query). Un microservizio dedicato dovrebbe
duplicare quella stessa logica di accesso alla cache, oppure richiamare a sua volta
`GlobalSearchService`/Redis direttamente — un salto di rete in piu' senza alcun
beneficio. Un nuovo microservizio ha senso quando la capability rappresenta un dominio
dati distinto con una propria fonte di verita' (come `AirportsService`/`FlightsService`
stessi, rispetto a `GlobalSearchService`): non e' questo il caso.


**`NavMenu.razor` diventa reattivo.** La sidebar vive nel layout (`MainLayout.razor`) e
non riceve automaticamente un nuovo rendering ad ogni navigazione tra le pagine di
ricerca. E' stata quindi marcata `@rendermode InteractiveServer` esplicitamente (prima
non lo era) e si iscrive a `NavigationManager.LocationChanged` (con la relativa pulizia
in `Dispose`, tramite `IDisposable`) per ricalcolare lo stato a ogni cambio di URL: i tre
sottopulsanti "All Results"/"Airport Results"/"Flight Results" compaiono solo quando
esiste una query di almeno 3 caratteri, e puntano sempre alla ricerca corrente con il
proprio ultimo offset.

**Evidenziazione della voce attiva calcolata a mano, non con `NavLink`.** Durante il test
manuale e' emerso che aprire un dettaglio raggiunto da una ricerca (es. dalla scheda
"Airport Results") accendeva nella sidebar il bottone originale "Airports", non il
sottopulsante di ricerca da cui si era partiti. La causa: `<NavLink>`, senza un `Match`
esplicito, si accende per semplice prefisso dell'URL corrente — e `/airports/AP0001`
inizia comunque per `airports`, indipendentemente dal fatto che ci si sia arrivati dalla
lista semplice o da una ricerca; i tre sottopulsanti di ricerca, puntando a
`/search/airports` ecc., non sono mai un prefisso di quell'URL e restano quindi spenti a
prescindere. `NavMenu.razor` ora calcola esplicitamente una "sezione attiva"
(`ComputeActiveSection`) invece di affidarsi al matching automatico: per una pagina di
dettaglio, guarda il suo `returnUrl` (quando presente) per risalire a quale delle tre
pagine di risultati l'ha generata, e ricade sul bottone "Airports"/"Flights" originale
solo in assenza di quel contesto (navigazione diretta o dalla lista semplice, dove
restare su quella sezione e' corretto). La classe CSS `active` viene quindi applicata a
mano (`NavClass`) su normali tag `<a>` al posto di `<NavLink>`, per avere pieno controllo
sulla logica invece di combinare il matching automatico del framework con un'eccezione
scritta a mano — sarebbe stata una fonte di incoerenze piu' difficile da mantenere.


**Numero di risultati per pagina, indipendente per scheda.** Le tre schede di ricerca
mostravano tutte una pagina fissa di 20 righe, senza possibilita' di cambiarla; su
richiesta esplicita e' stata resa variabile, ma non come un unico valore condiviso fra le
tre schede — cambiarlo su una non deve alterare le altre due, per lo stesso motivo gia'
visto per gli offset. `SearchNavigation.State` porta quindi tre campi separati
(`AllLimit`/`AirportsLimit`/`FlightsLimit`, tutti di default 20) al posto dell'unico
`Limit` precedente: la query string resta la fonte di verita' (`allLimit`/
`airportsLimit`/`flightsLimit`), cosi' la scelta sopravvive a un refresh o a un link
condiviso, e non viene toccata da una nuova ricerca (che azzera solo gli offset — vedi
`Search.razor.OnSearchClicked`).

`SearchResultsTable.razor` espone un selettore (`<select>`, valori 10/20/50/100) sopra i
pulsanti Previous/Next, e un nuovo parametro `EventCallback<int> OnLimitChanged`
invocato alla scelta. La responsabilita' di reagire al cambiamento resta della pagina
chiamante, non del componente condiviso: ciascuna delle tre pagine, nel proprio gestore,
aggiorna il proprio campo di `_state`, azzera il proprio offset (un offset calcolato con
il vecchio limite non indica piu' nulla di sensato una volta cambiata la dimensione di
pagina) e ricarica i risultati. Le opzioni del selettore si fermano a 100 perche' e' lo
stesso tetto gia' imposto dal backend (`GlobalSearchController`: `limit =
Math.Min(limit, 100)`) — un'opzione oltre quella soglia verrebbe comunque troncata
silenziosamente lato server, quindi non viene nemmeno proposta.

Questa modifica riguarda solo le tre pagine di risultati di ricerca (`SearchAll`/
`SearchAirports`/`SearchFlights.razor`); le pagine di sfoglio originali
(`Airports.razor`/`Flights.razor`), esplicitamente fuori dal perimetro concordato per
questa modifica, continuano a usare il proprio limite fisso di 20, invariato.

Semplificazioni consapevoli, non affrontate in questo prototipo:

- Nessun lock distribuito contro il "cache stampede" su una chiave fredda richiesta da
  piu' utenti nello stesso istante (discusso in chat: probabilmente non necessario alla
  scala di questo esercizio, ma e' un limite noto, non un errore).
- Il filtro di rifinitura di `CachingGlobalSearchService` sul superset cacheato lavora
  ancora solo sul campo `Description` (vedi il TODO gia' presente in quel file): non
  l'ho toccato, resta un affinamento possibile ma fuori dallo scopo di questo prototipo.
- Non e' stato possibile validare la compilazione ne' l'esecuzione dei nuovi test in
  modo automatico (nessun SDK .NET disponibile nell'ambiente in cui e' stato scritto
  questo codice): vanno verificate con `dotnet build` e `dotnet test` sulla tua
  macchina prima di fidartene.
