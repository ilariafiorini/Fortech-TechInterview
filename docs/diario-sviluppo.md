# Diario del processo di sviluppo — Tech Interview: Global Search API Challenge

**Branch di riferimento:** `prototype/real-search` (prototipo di studio, non destinato di per sé alla consegna finale — vedi le note conclusive su questo punto)
**Periodo coperto in dettaglio verbatim:** 27 agosto 2026, 13:40–23:10 (una singola giornata di lavoro molto densa)
**Autrice dei prompt:** Ilaria Fiorini

## Nota metodologica

Questo documento nasce da una richiesta esplicita: trasformare la conversazione avuta con l'assistente in una testimonianza del processo di elaborazione del progetto, che tracci i prompt dell'autrice e riassuma per sommi capi le reazioni e le azioni conseguenti, mettendo in luce l'evoluzione dell'approccio adottato.

Ha un limite di fedeltà che è onesto dichiarare subito: l'accesso verbatim ai prompt copre solo la sessione di lavoro qui documentata (dal bug di persistenza del dettaglio voli fino alla richiesta di questo stesso documento). La fase precedente — mockup iniziale della Global Search API, sua trasformazione in implementazione reale, primo ridisegno multi-pagina della ricerca, prima infarinatura di test — è nota solo attraverso una sintesi già condensata prodotta in una sessione precedente, non attraverso i prompt originali. Per questo il documento è diviso in due parti con status diverso: la Parte I è una ricostruzione dichiarata, la Parte II è un registro puntuale, prompt per prompt, con citazioni pressoché letterali (incluse le imperfezioni di battitura, lasciate come sono state scritte).

---

## Parte I — Prima di questa sessione (ricostruzione sintetica)

Il progetto parte da un ambiente a microservizi già fornito (`AirportsService`, `FlightsService`, frontend Blazor Server) a cui va aggiunto `GlobalSearchService`, inizialmente consegnato come mockup con cache Redis funzionante ma aggregazione dati finta. In una prima fase di lavoro (sessioni precedenti a questa, non coperte verbatim) sono stati completati, in ordine:

La containerizzazione dell'intero ambiente via Docker Compose (modellata sull'output di `aspire publish`), con la decisione — poi rispettata con costanza per tutto il resto del progetto — di rendere ogni porta esposta sull'host parametrica tramite `docker/.env`, mai hardcoded.

L'implementazione reale di `GlobalSearchService` (commit `14688c2`, "Prototipo di studio: implementa la Global Search API reale su questo branch"): sostituzione del mockup con l'aggregazione vera su `AirportsService`/`FlightsService`, dietro la cache Redis già predisposta.

Una prima estensione della ricerca con filtro opzionale per tipo (`resourceType`) e il ridisegno del frontend da un'unica tabella mista a quattro pagine distinte (`Search`, `SearchAll`, `SearchAirports`, `SearchFlights`), con lo stato di navigazione (query, offset per scheda) tenuto nella query string tramite `SearchNavigation.cs` — una decisione motivata esplicitamente dalla necessità che ogni pagina fosse ricostruibile da un refresh o da un link condiviso, senza stato lato server.

Una prima infarinatura di test unitari e di integrazione (commit `d18e287`, "Aggiunge test unitari e di integrazione per il prototipo di studio"), a copertura di questa prima implementazione.

Da qui in avanti, il documento riporta la sessione di lavoro del 27 agosto in modo puntuale.

---

## Parte II — Registro dettagliato della sessione del 27 agosto 2026

### Fase A — Un bug osservato, e una discussione che rifiuta la scorciatoia più ovvia

**Prompt (13:40):**
> "testando mi sono resa conto che il dettaglio dei voli cercati non persiste: suppongo che venga chiamato ogni volta con una ricerca per id su flightservice, ma così facendo non si ha consistenza e persistenza del dettaglio rispetto allo "screenshot" fatto al momento della ricerca [...] il dettaglio cambia ad ogni click"

Segnala un difetto trovato testando manualmente l'applicazione, corredato di uno screenshot testuale che mostra lo stesso `FL0001` con aereo e orari diversi a ogni apertura, e formula già un'ipotesi plausibile sulla causa.

**Reazione/azione:** conferma dell'ipotesi tramite lettura diretta del codice sorgente (`FlightsServiceImpl` rigenera 1000 voli casuali a ogni chiamata gRPC, senza lifetime esplicito) e proposta di tre alternative attraverso una domanda a scelta multipla: singleton su `FlightsServiceImpl`, cache per-id lato dettaglio, snapshot dei dati nell'URL.

**Prompt:** risposta alla domanda — rifiuta categoricamente la prima opzione: *"Non ti so ancora dare una risposta, ma sicuramente tutto l'impianto dato all'inizio dell'ambiente di test va mantenuto intatto"*. Un vincolo di metodo dichiarato esplicitamente, non solo una preferenza tecnica.

**Reazione/azione:** approfondimento della distinzione tra le due opzioni rimaste con un'ulteriore domanda mirata su cosa debba davvero garantire la "coerenza" del dettaglio (corrispondenza esatta alla riga cliccata, o solo stabilità per un certo tempo), a cui risponde *"Non lo so ancora, discutiamone di più"* — la discussione prosegue prima di ogni scelta.

### Fase B — L'autrice stessa trova la soluzione migliore

**Prompt (13:54):**
> "però ho appena notato che il dettaglio del volo rispecchia ed è composto da tutti i dati di una riga che noi già mettiamo in cache (ogni riga cacheata ha già tutte le info necessarie [...]) se ho cacheato al momento della ricerca tutti i dati, ho già tutto quello che mi serve per ricostruire il dettaglio, giusto?"

Questo è il punto di svolta della fase: invece di scegliere tra le opzioni proposte, propone una quarta strada, migliore di tutte quelle discusse, basata sull'osservazione diretta del comportamento del sistema (il payload di `GetFlights` già contiene tutti i campi necessari al dettaglio).

**Reazione/azione:** conferma e validazione dell'intuizione — la cache di ricerca (`IFlightsSearchCache`) già salva l'elenco completo sotto la query normalizzata, quindi una lettura successiva della stessa query è quasi sempre un cache hit sull'identico dato. Emerge però una lacuna reale nel modello dati: `FlightDto` non portava ancora `DepartureTime`/`ArrivalTime`, necessari al dettaglio ma non al solo confronto testuale per cui erano stati pensati.

**Prompt (14:00):**
> "vorrei approfondire un altra cosa: come viene costruita la scheda di dettaglio degli aeroporti? Il dettaglio viene richiesto ad airportsservice o è costruito sulla riga cacheata, come vogliamo fare per flights?"

Prima di procedere con l'implementazione, allarga la domanda per capire se lo stesso problema esista anche per gli aeroporti, invece di limitarsi a correggere solo il caso segnalato.

**Reazione/azione:** verifica diretta nel codice — il dettaglio aeroporti fa già la stessa cosa (chiamata dal vivo), ma il problema non si manifesta perché `AirportsRepository` è registrato come singleton: una stabilità di fatto, non garantita per contratto.

**Prompt (14:06):** riporta il chiarimento ricevuto da chi l'ha ingaggiata per il test — la valutazione va condotta deducendo il comportamento dei due servizi in modo empirico/black-box, senza fondarsi sulla lettura del codice sorgente — e ne trae da sola la conseguenza architetturale corretta: *"Quindi in definitiva avrebbe senso partire anche per i dettagli degli aeroporti dai dati cacheati"*. Questo è un momento significativo dal punto di vista del metodo: riconosce che la propria analisi (basata sulla lettura del codice) era tecnicamente fondata ma metodologicamente fuori dal perimetro concordato con l'interlocutore del test, e corregge la rotta da sola, arrivando comunque alla scelta di design più solida (trattare aeroporti e voli allo stesso modo).

**Reazione/azione:** validazione del ragionamento su due livelli — di metodo (la deduzione black-box è quella corretta per questo esercizio) e di sostanza (dal punto di vista del solo contratto API osservabile, nulla garantisce la stabilità degli aeroporti per l'intera durata di una sessione) — e allineamento della soluzione a entrambi i tipi di dato.

### Fase C — Una domanda di architettura, prima del via libera al codice

**Prompt (14:13):** una domanda tecnica in tre parti — se l'estensione consista nell'aggiungere endpoint a `GlobalSearchService` piuttosto che nuovi microservizi dedicati, quali criteri generali guidino questa scelta, e chi gestisca a livello architetturale l'interazione con Redis. Nessuna richiesta di codice: una richiesta di comprensione prima di procedere.

**Reazione/azione:** spiegazione basata sul criterio del *bounded context* (un nuovo microservizio ha senso per un dominio dati distinto con una propria fonte di verità, non per esporre una lettura su dati che un altro servizio già possiede e cachea) e chiarimento che Redis è gestito direttamente da `GlobalSearchService`, senza intermediari.

**Prompt (14:25):**
> "ok, direi che per adesso sono soddisfatta delle lezioni di teoria (grazie :-) ) e penso sia il momento di passare alla stesura del codice delle ultime modifiche concordate, che volentieri ti affido"

Il primo via libera esplicito alla scrittura di codice in questa sessione, arrivato solo dopo aver esaurito i propri dubbi teorici.

**Reazione/azione:** implementazione completa del disegno concordato — `FlightDto` esteso con `DepartureTime`/`ArrivalTime`; due nuove azioni su `GlobalSearchController` (`GetCachedAirportById`, `GetCachedFlightById`) che leggono dalla cache di ricerca esistente invece di interrogare di nuovo i microservizi dal vivo; `SearchResultsTable.razor` esteso per portare la query nei link di dettaglio; `AirportDetail.razor`/`FlightDetail.razor` riscritti per tentare prima la lettura cacheata e ricadere sulla chiamata dal vivo solo in assenza di contesto di ricerca; aggiornamento di `docs/architecture.md` con tutta la motivazione, comprese le alternative scartate.

**Prompt (14:34):** `dotnet build`/`dotnet test andati a buon fine :-)` — la prima delle numerose verifiche indipendenti che l'autrice esegue di persona sul proprio ambiente prima di procedere, un pattern che si ripete costantemente per tutto il resto della sessione.

### Fase D — Due difetti trovati testando, e una domanda linguistica

**Prompt (14:45):**
> "sembra che la logica di recupero dettagli dai dati in cache funzioni bene :-) ho però notato che quando si clicca sui dettagli cambia il tasto della sidebar evidenziato [...] Inoltre forse anderebbero sostituiti i nomi dei vari sottotasti del tasto di ricerca; Found All, Found Airports, Found Flights (se found è una buona traduzione di "trovati")"

Due osservazioni indipendenti nate dallo stesso giro di test manuale: un difetto funzionale e una questione di qualità linguistica dell'interfaccia (non dà per scontata la propria proposta di traduzione, la sottopone a verifica).

**Reazione/azione:** diagnosi precisa del difetto (`NavLink` senza `Match` esplicito si accende per prefisso dell'URL, quindi `/airports/AP0001` accende sempre il bottone "Airports" originale, a prescindere dalla provenienza) e proposta di due soluzioni alternative tramite domanda a scelta multipla, accompagnata da un suggerimento linguistico alternativo a "Found" (poi confluito in "All/Airport/Flight Results").

**Risposta:** *"Logica basata su returnUrl (consigliato)"* e *"All Results / Airport Results / Flight Results"*.

**Reazione/azione:** riscrittura di `NavMenu.razor` con un calcolo esplicito della sezione attiva (`ComputeActiveSection`/`ResolveDetailSection`) che, per le pagine di dettaglio, risale al `returnUrl` per determinare la scheda di provenienza; sostituzione dei `<NavLink>` con normali tag `<a>` a controllo manuale; rinomina dei sottopulsanti; aggiornamento della documentazione.

**Prompt (14:55):** `dotnet build`/`dotnet test eseguiti correttamente` — seconda verifica indipendente.

### Fase E — Un controllo che nessuno le aveva chiesto di fare

**Prompt (14:56):**
> "prima di chiederti di committare tutto volevo soffermarmi sui test: la lista è aggiornata a comprendere tutti i test utili e necessari anche considerando le ultime integrazioni che hai fatto?"

Prima di dare il via libera al commit, introduce di propria iniziativa un passaggio di verifica che va oltre il "funziona" empirico già confermato: chiede un audit esplicito della copertura di test rispetto a tutto ciò che è stato aggiunto.

**Reazione/azione:** audit onesto, non consolatorio — la risposta è "no, non è ancora aggiornata" — con individuazione precisa di due lacune (nessun test su `CachingGlobalSearchService` per la separazione dei bucket `all`/`airport`/`flight`; nessun test sui due nuovi endpoint di dettaglio cacheato) oltre a una lacuna strutturale più ampia, la totale assenza di copertura sul frontend, proposte tutte tramite una domanda a scelta multipla.

**Risposta:** *"Test su CachingGlobalSearchService (bucket), Test sui due endpoint di dettaglio cacheato"* — sceglie le due lacune più critiche, lasciando fuori la copertura frontend e l'opzione "nessun test".

**Reazione/azione:** scrittura di un nuovo test double (`MultiKeyRedisTestFactory`, necessario perché quello esistente non isolava le chiavi Redis per bucket), quattro nuovi test unitari sul servizio di caching, otto nuovi test sul controller, quattro nuovi test di integrazione end-to-end (incluso un test che chiama l'endpoint di dettaglio due volte di seguito per verificare esplicitamente la stabilità — il guardrail diretto contro il bug di partenza), e aggiornamento di `tests/README.md` e `docs/architecture.md`.

### Fase F — Primo commit della giornata, e un metodo di lavoro che emerge con chiarezza

**Prompt (15:20):**
> "ok, tutto bene, possiamo passare al commit. finito commit e push (te lo dico adesso per tenerlo come memo dei prossimi argomenti di discussione di cui tienimi traccia) vorrei discutere della possibilità: di variare il numero di righe visualizzate nelle varie schede dei dettagli delle ricerche [...] poi vorrei valutare di integrare nell'ambiente dockerizzato anche una versione dockerizzata di grpcui"

Un singolo messaggio che combina tre cose distinte: l'approvazione finale al commit, un'istruzione esplicita di "tenere traccia" per la discussione successiva (un uso consapevole dell'assistente come memoria di progetto), e l'apertura di due nuovi filoni di lavoro senza però darne mandato immediato.

**Reazione/azione:** primo tentativo di commit, bloccato da un lock di git residuo (problema ricorrente di questo ambiente, dovuto al bridge verso la macchina dell'autrice) — richiesta di pulizia via PowerShell, seguita da "fatto", commit riuscito (`208da7a`, 23 file), nuovo lock residuo, seconda pulizia, e infine richiesta di eseguire il push direttamente lei (la rete della sandbox non raggiunge GitHub). Push confermato allineando `HEAD` e `origin/prototype/real-search`.

**Prompt (15:28):** *"se vuoi, mi puoi già esporre le tue prime considerazioni sui due nuovi argomenti"* — decide di non aspettare una sessione futura per riprendere i due temi accantonati, ma di farlo subito.

**Reazione/azione:** prime considerazioni basate su un controllo diretto del codice — il valore 20 risultava hardcoded in tre punti diversi dell'applicazione (non solo nella ricerca) — presentate come base di discussione, non come proposta chiusa.

### Fase G — Una precisazione che non è ancora un via libera

**Prompt (15:34):**
> "Premetto che le tre risposte che ti darò sui tre quesiti che mi hai fatto sono solo una puntualizzazione di questa discussione e che non sono ancora un via libera alla creazione/modifica di codice."

Un momento particolarmente indicativo del metodo di lavoro adottato per tutto il progetto: distingue esplicitamente, prima ancora di rispondere, tra il fornire chiarimenti e l'autorizzare un'azione — evitando che una risposta a una domanda tecnica venga interpretata come un mandato implicito.

**Reazione/azione:** presa d'atto esplicita, nessuna azione, si resta in fase di analisi.

**Prompt (15:35, poi 15:36):** un messaggio troncato a metà (*"SI ALLA SCELTA DELLA LUNGHEZZA DELLA PAGINAZIONE SOLO ALLE TRE VISUALIZAZIONI E"*), seguito dalla richiesta di completarlo (*"si infatti... Il seguito."*).

**Reazione/azione:** segnalazione esplicita del troncamento invece di indovinare il contenuto mancante, in attesa del completamento — una scelta deliberata di non presumere.

**Prompt (15:40):** il completamento, in tre parti — il limite di paginazione dev'essere indipendente per ciascuna delle tre schede di ricerca ("controllo su ognuna delle schede"); mandato di scegliere e proporre la miglior implementazione dockerizzata di grpcui secondo i propri criteri; richiesta che l'immagine dockerizzata esista sia sul branch corrente sia su `main`. In coda, una riflessione aperta e non risolutiva: *"sto anche valutando di mergiare questa branch di sviluppo nella main come proposta di risultato della mia prova tecnica, visto che in fondo il ruolo che dovrei ricoprire è quello di analista e coordinatore di progetti e non solo di programmatore senior"*.

**Reazione/azione:** ricerca web dedicata (tramite un sub-agente) sulle opzioni Docker per grpcui, invece di fare affidamento sulla sola memoria — individuata l'immagine ufficiale `fullstorydev/grpcui`, con relativi flag consigliati (`-plaintext`, `-connect-fail-fast=false`) motivati singolarmente. Nessuna azione sulla domanda del merge, lasciata esplicitamente aperta come riflessione dell'autrice, non come richiesta.

### Fase H — Un audit richiesto prima ancora di scrivere una riga

**Prompt (15:56):**
> "per quanto riguarda le porte [...] ti ricordo che avevamo già preso la decisione di renderle parametriche [...] ne approfitterei anche per dare un'occhiata se questa decisione [...] è stata effettivamente parametrizzato. Finito questo check/update delle specifiche, fammi un resoconto di tutte le tue scelte e scoperte e poi attendi il mio via libera per iniziare ad implementare"

Prima di autorizzare l'implementazione di grpcui, richiede una verifica di coerenza su una decisione architetturale presa in precedenza (la parametrizzazione delle porte), e detta esplicitamente la sequenza da seguire: verifica, resoconto, attesa del via libera.

**Reazione/azione:** rilettura completa di `docker-compose.yml`, `docker/.env`, `docker/README.md`, `docker/LINK-SERVIZI.md`, confronto con le stesse voci su `main`. Esito: piena conformità, con una sola duplicazione minore già documentata in precedenza (non un'anomalia nuova). Resoconto consegnato, nessuna implementazione avviata.

**Prompt (16:03):** *"ok, inizia pure a creare codice per tutto quello che abbiamo deciso e pianificato"* — il via libera, finalmente, per entrambi i filoni.

**Reazione/azione:** implementazione dei tre limiti indipendenti in `SearchNavigation.State`, del selettore di righe per pagina in `SearchResultsTable.razor`, dell'aggiornamento delle tre pagine di risultato; aggiunta del servizio `grpcui` a `docker-compose.yml`/`.env`/README/LINK-SERVIZI sul branch corrente. Per replicare le stesse modifiche su `main` senza disturbare il lavoro non ancora committato sul branch corrente, è stato creato un git worktree — la prima versione del quale è stata creata per errore in un percorso non sincronizzato con il computer dell'autrice, generando un blocco (`'main' is already checked out`) risolto solo con il suo intervento da PowerShell.

### Fase I — Difficoltà tecniche impreviste, gestite con pazienza

Questa parte della sessione (dalle 22:33 in poi, dopo una pausa) è segnata da una serie di intoppi tecnici — non di merito, ma di meccanica: un ambiente con un bridge file system che impedisce la normale pulizia dei file di lock di git, e un worktree che, ricreato correttamente dentro la cartella sincronizzata, si è poi rivelato comunque inutilizzabile direttamente dal PowerShell dell'autrice per un problema di percorsi assoluti incompatibili tra i due ambienti.

**Prompt (22:42):**
> "gestisci il commit di main come meglio credi, ma prima: non pensi sia il caso di rifare il docker compose per vedere se tutto funziona? e neanche il dotnet build?"

Prima di autorizzare il commit, richiede esplicitamente una verifica funzionale reale — non solo la validazione statica già fatta — sia di Docker sia della build .NET. Un ulteriore esempio del pattern "verifica prima di procedere" osservato per tutta la sessione.

**Reazione/azione:** dichiarazione onesta di un limite — l'ambiente di esecuzione dell'assistente non ha Docker né l'SDK .NET installati, quindi entrambe le verifiche devono passare dal computer dell'autrice — con indicazione dei comandi esatti da eseguire.

**Prompt (22:46):** riporta un errore di compilazione reale (`CS7036`, argomento mancante nel costruttore di `SearchNavigation.State`) — la verifica richiesta ha effettivamente trovato un difetto.

**Reazione/azione:** individuazione e correzione di un'inizializzazione dimenticata in `NavMenu.razor` (rimasta con la vecchia arità del costruttore, sfuggita a una verifica precedente basata sulla ricerca testuale della parola "Limit" invece che sul controllo sistematico di ogni chiamata), con dichiarazione esplicita dell'errore di metodo che l'aveva causata.

**Prompt (22:49, poi 22:54):** conferma che sia `dotnet build`/`dotnet test` sia `docker compose` sono andati a buon fine.

**Reazione/azione:** commit su `main` (`c991c22`), tentato più volte e bloccato ripetutamente da lock di git residui (`index.lock`, `HEAD.lock`, un file `locked` del worktree, un lock sul ref `refs/heads/main`) — ciascuno risolto con una richiesta mirata di pulizia da PowerShell e la conferma "fatto" dell'autrice, fino al commit riuscito.

**Prompt (23:01):** riporta un ulteriore errore nel tentativo di push (*"fatal: not a git repository"*), mostrando l'output esatto del proprio terminale invece di descriverlo a parole — un dettaglio che si rivela decisivo per la diagnosi.

**Reazione/azione:** diagnosi del problema di fondo (il worktree conteneva percorsi assoluti scritti dal punto di vista della sandbox cloud, non della macchina dell'autrice) e soluzione che aggira il problema invece di provare a ripararlo: il push funziona comunque dalla cartella di lavoro abituale, perché opera sul ref locale e non sulla cartella del worktree.

**Prompt (23:03):** *"fatto tutte e due"* (push e pulizia del worktree) — reazione: verifica dell'allineamento `main`/`origin/main`, poi richiesta di conferma per procedere anche con il commit su `prototype/real-search`.

**Prompt (23:03):** *"procedi con il commit"* — reazione: commit riuscito (`c46d841`, 12 file), dopo un altro paio di cicli di lock/pulizia ormai gestiti con procedura consolidata.

**Prompt (23:06):** *"pusha"* — reazione: nuovo tentativo di push diretto (per completezza, sapendo già che sarebbe stato bloccato), conferma del blocco di rete, richiesta esplicita all'autrice di eseguirlo lei. Conferma finale (*"fatto"*) e verifica dell'allineamento `HEAD`/`origin/prototype/real-search`.

### Fase L — Una richiesta che chiude il cerchio

**Prompt (23:09):**
> "ottimo :-) Adesso vorrei chiederti una cosa: hai modo di trasformare tutta questa chat in un documento che tiene traccia di tutti i miei prompt e delinea per sommi capi le tue reazioni ed azioni conseguenti ad essi? Vorrei che il documento fosse una testimonianza del processo di elaborazione del progetto e che mostri anche le evoluzioni e gli approcci che ho utilizzato"

La richiesta che ha dato origine a questo stesso documento — un'ultima azione che è essa stessa parte dell'evidenza del metodo adottato: non si limita a consegnare codice funzionante, ma chiede che il processo con cui ci si è arrivati diventi leggibile e verificabile da terzi.

---

## Osservazioni conclusive: l'evoluzione dell'approccio

Alcuni tratti ricorrono con una regolarità che vale la pena mettere in evidenza, perché è probabilmente questo — più del singolo dettaglio implementativo — a costituire la vera "testimonianza" richiesta.

**Il confine tra chiarire e autorizzare è sempre stato tracciato esplicitamente.** Fin dall'inizio della collaborazione (una regola dichiarata anche in sessioni precedenti a questa) nessuna modifica di codice è stata avviata senza un via libera inequivocabile, e in almeno un'occasione (Fase G) è stata l'autrice stessa a precisare, prima ancora di rispondere a delle domande tecniche, che quelle risposte non andavano interpretate come un'autorizzazione implicita.

**La preservazione dello scaffolding di partenza è stata un vincolo di metodo, non solo tecnico.** Il rifiuto immediato di toccare `FlightsServiceImpl` (Fase A) e il ragionamento sull'empirico/black-box che ne è seguito (Fase B) mostrano una lettura del compito assegnato — dedurre il comportamento del sistema dato, non riscriverlo — coerente dall'inizio alla fine.

**Le decisioni architetturali sono sempre passate da una fase di comprensione, spesso esplicitamente separata dalla fase di implementazione.** Le domande su microservizi vs. endpoint (Fase C) o sui criteri di parametrizzazione (Fase H) non erano funzionali a un'azione immediata: erano richieste di capire prima di scegliere, con il via libera alla scrittura del codice arrivato solo a comprensione raggiunta.

**La verifica indipendente è stata sistematica, non occasionale.** `dotnet build`/`dotnet test` sono stati eseguiti e riportati dopo praticamente ogni batch di modifiche, e in un caso (Fase I) questa abitudine ha effettivamente intercettato un errore di compilazione reale prima che arrivasse a un commit. L'audit della copertura di test richiesto di propria iniziativa (Fase E), prima ancora che venisse proposto, è dello stesso segno.

**Le questioni aperte sono state gestite come tali, non forzate a una conclusione prematura.** Il "non lo so ancora, discutiamone di più" (Fase A), la precisazione sul non-via-libera (Fase G), e la riflessione lasciata esplicitamente sospesa sul possibile merge verso `main` come proposta di consegna (Fase G) mostrano una gestione consapevole del proprio processo decisionale: quando una risposta non era ancora matura, veniva dichiarato invece di essere forzata.

**Il progetto è stato trattato come un processo da tracciare, non solo da eseguire.** La richiesta di "tenere il memo" dei due argomenti aperti (Fase F) e questa stessa richiesta di documentazione finale (Fase L) sono coerenti con un approccio che tratta la gestione del lavoro — sequenza, priorità, tracciabilità delle decisioni — come parte del compito tanto quanto il codice prodotto. È un dettaglio non irrilevante rispetto alla candidatura per un ruolo di analista e coordinatore di progetti, più che di sola programmazione: il modo in cui la collaborazione è stata condotta — chiarire prima di autorizzare, verificare prima di accettare, documentare le motivazioni e non solo il risultato, tracciare esplicitamente ciò che resta aperto — è esso stesso un artefatto del ruolo, non solo il codice che ne è uscito.
