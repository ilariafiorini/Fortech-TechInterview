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

- [x] **Logica di ricerca reale in `GlobalSearchService`**: implementata come
  PROTOTIPO DI STUDIO sul branch `prototype/real-search` (`RealGlobalSearchService`,
  `AirportsSearchCache`, `FlightsSearchCache` — vedi la sezione dedicata in fondo a
  questo file). Su `main` resta `MockGlobalSearchService`: la riscrittura a mano,
  vera consegna dell'esercizio, resta da fare.
- [ ] Decidere e documentare la strategia di paginazione (sulle fonti vs
  sull'aggregato) — vedi i TODO nel mockup per i trade-off, tenendo conto che
  `CachingGlobalSearchService` chiama il metodo con un `limit` molto ampio per
  ottenere l'insieme "completo" da cachare.
- [ ] Allineare il filtro di rifinitura usato dalla cache (oggi su `Description`)
  agli stessi campi grezzi usati dalla ricerca reale, se necessario.
- [x] Collegare la pagina `TechInterview.Web/Components/Pages/Search.razor` (oggi
  un placeholder con dati finti) alla vera Global Search API — fatto anch'esso solo
  sul branch `prototype/real-search`, stesso discorso del punto sopra.
- [ ] Bonus facoltativi non ancora affrontati: test automatici (cartella `tests/`
  pronta ma vuota), logging strutturato applicativo.
- [x] Rendere configurabile l'ampiezza del fan-out con cui `SearchAsync` interroga
  `FlightsService` e `AirportsService` (numero di chiamate parallele necessarie a
  coprire l'intero dataset di una fonte) invece di un valore fisso in codice — stesso
  pattern gia' usato per `GlobalSearchCache` in `appsettings.json`. Fatto sul branch
  `prototype/real-search` come `SearchFanOutOptions` / sezione `SearchFanOut` in
  `appsettings.json` (default `PageLimit: 20`).
- [ ] **Resilienza gRPC su FlightsService**: lo `AddStandardResilienceHandler()` di
  `TechInterview.ServiceDefaults` copre solo i fallimenti di trasporto (connessione
  rifiutata, timeout di rete), perche' decide se ritentare guardando lo status code
  HTTP — ma un errore applicativo gRPC viaggia in una risposta HTTP 200 con l'esito
  vero nei trailer (`grpc-status`), invisibili a quella logica. Da valutare una retry
  policy nativa sul canale gRPC (`GrpcChannelOptions.ServiceConfig` con
  `MethodConfig`/`RetryPolicy`, che capiscono `UNAVAILABLE`/`DEADLINE_EXCEEDED` ecc.).
- [ ] Implementare per Airports il seeding lazy/auto-riparante della entry cache a
  chiave stringa vuota (l'intera tabella di 300 aeroporti, stessa scadenza
  sliding+assoluta delle altre entry) e l'algoritmo di ricerca della sottostringa
  cacheata piu' lunga compatibile con la nuova query (fallback su "") — vedi il
  dettaglio nella voce "Strategia di caching per fonte" qui sotto. Da riusare per il
  fetch completo lo stesso schema a piu' chiamate parallele gia' scelto per Flights,
  visto che anche `AirportsService` limita `limit<=100` per pagina.
- [ ] Decidere se, a ogni nuova ricerca, eliminare esplicitamente dalla cache l'elenco filtrato della ricerca precedente oppure lasciarlo scadere da solo via TTL — vedi la nota in "Decisioni prese" qui sotto (Strategia di caching per fonte, Airports vs Flights).
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

## Prototipo di studio: branch `prototype/real-search`

Su richiesta esplicita, questo branch (staccato da `main`, non pensato per essere
mergiato cosi' com'e') contiene una implementazione completa della vera logica di
ricerca, seguendo tutte le decisioni discusse e documentate in questo file. Serve da
riferimento concreto per lo studio, non da consegna: l'idea e' riscrivere a mano la
logica reale su `main`, usando questo codice come termine di paragone quando serve
controllare come una decisione si traduce in codice.

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

Semplificazioni consapevoli, non affrontate in questo prototipo:

- Nessun lock distribuito contro il "cache stampede" su una chiave fredda richiesta da
  piu' utenti nello stesso istante (discusso in chat: probabilmente non necessario alla
  scala di questo esercizio, ma e' un limite noto, non un errore).
- Il filtro di rifinitura di `CachingGlobalSearchService` sul superset cacheato lavora
  ancora solo sul campo `Description` (vedi il TODO gia' presente in quel file): non
  l'ho toccato, resta un affinamento possibile ma fuori dallo scopo di questo prototipo.
- Non e' stato possibile validare la compilazione in modo automatico (nessun SDK .NET
  disponibile nell'ambiente in cui e' stato scritto questo codice): va verificata con
  `dotnet build` sulla tua macchina prima di fidarsene.
