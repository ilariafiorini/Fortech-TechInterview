# Ambiente Docker

Questa cartella contiene tutto il necessario per eseguire l'intera solution
containerizzata in Docker Desktop, senza bisogno dell'SDK .NET installato in locale.

> Elenco rapido di tutti i link (comprese le porte non citate qui sotto): vedi
> `docker/LINK-SERVIZI.md`.

## Servizi

| Servizio            | Porta host | Descrizione                                       |
|----------------------|-----------|------------------------------------------------------|
| webfrontend           | 8080      | Frontend Blazor Server                                |
| airportsservice        | 8081      | REST API — CRUD aeroporti                             |
| flightsservice         | 8082      | gRPC API — CRUD voli                                  |
| globalsearchservice    | 8083      | Global Search API — implementazione reale              |
| aspire-dashboard       | 18888     | Dashboard OpenTelemetry (log/traccia/metriche)        |
| redis                  | 6379      | Cache usata da globalsearchservice                    |
| redis-commander        | 8084      | UI web per ispezionare a mano le chiavi in Redis      |
| grpcui                 | 8085      | UI web per esplorare FlightsService via gRPC reflection |

Tutte le porte host elencate sopra sono i **valori di default**: sono parametrizzate
tramite variabili d'ambiente lette dal file `docker/.env` (Docker Compose lo carica
automaticamente, senza bisogno di flag aggiuntivi). Se una di queste porte e' gia'
occupata da un altro servizio sul tuo computer, apri `docker/.env` e cambia il numero
corrispondente (es. `WEBFRONTEND_PORT=9080`), poi riavvia con `docker compose down` e
di nuovo `docker compose up --build` — non serve toccare `docker-compose.yml`.

`flightsservice` usa internamente anche una seconda porta, 8090, **non pubblicata
sull'host** (quindi non serve parametrizzarla in `.env`, non puo' avere conflitti
con altri servizi del tuo computer): serve solo all'healthcheck Docker interno al
container, perche' la porta 8082/8080 e' HTTP/2 puro (richiesto dai client gRPC) e
non risponde a `curl` in HTTP/1.1. Dettagli nel commento in
`src/FlightsService/Program.cs`.

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
- http://localhost:8084 — Redis Commander (per vedere le chiavi cacheate)

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

## Come esplorare e testare FlightsService (gRPC)

FlightsService non ha una controparte di Scalar/OpenAPI: essendo un'API gRPC pura
(nessuna rotta REST), non esiste uno "spec" OpenAPI da mostrare in una UI web, e gli
strumenti sono diversi da quelli REST. In compenso, in Development espone la
**reflection gRPC** (`Grpc.AspNetCore.Server.Reflection`), che permette a un client
generico di scoprire da solo i servizi/metodi esposti, senza dover importare a mano
`Protos/flights.proto` — e' concettualmente l'equivalente, per gRPC, di quello che
OpenAPI offre alle REST API.

Con la reflection attiva puoi usare, ad esempio:

- **grpcui gia' dockerizzato, http://localhost:8085**: `docker compose up --build` avvia
  anche un container `grpcui` (immagine ufficiale `fullstorydev/grpcui`) gia' puntato su
  `flightsservice`, nessuna installazione richiesta. E' l'opzione consigliata in questo
  ambiente Docker: apri l'URL e hai subito il form interattivo, generato dalla stessa
  reflection descritta sopra. Usa `-connect-fail-fast=false` (il container non si
  arresta se `flightsservice` non e' ancora pronto al primissimo avvio) e
  `restart: on-failure` come rete di sicurezza; la porta e' parametrizzata in
  `docker/.env` (`GRPCUI_PORT`, default 8085) come tutte le altre.
- **grpcurl** (CLI, l'equivalente di curl per gRPC — richiede solo `-plaintext`
  perche' qui non c'e' TLS):

  ```
  grpcurl -plaintext localhost:8082 list
  grpcurl -plaintext localhost:8082 describe flights.Flights
  grpcurl -plaintext -d "{\"offset\": 0, \"limit\": 5}" localhost:8082 flights.Flights/GetFlights
  grpcurl -plaintext -d "{\"id\": \"<un-id-restituito-sopra>\"}" localhost:8082 flights.Flights/GetFlightById
  ```

- **grpcui installato in locale** (`grpcui -plaintext localhost:8082`): stessa UI del
  container dockerizzato sopra, utile se preferisci non usarlo via Docker (es. contro
  un'istanza di FlightsService avviata fuori da questo `docker-compose.yml`).
- **Postman** o **Insomnia**: entrambi supportano gRPC nativamente; basta creare una
  richiesta gRPC verso `localhost:8082` e usare "usa reflection del server" invece di
  importare il file `.proto`.

Nota: la reflection e' mappata solo quando `ASPNETCORE_ENVIRONMENT=Development` (come
gia' per Scalar), quindi funziona con questo `docker-compose.yml` ma andrebbe rimossa
o protetta in un'ipotetica build di Production — il container `grpcui` stesso avrebbe
poco senso in una build del genere, e andrebbe rimosso insieme alla reflection.

## Come vedere la cache in azione (anche con il mockup)

Anche prima di implementare la ricerca reale puoi osservare il comportamento della
cache, perche' il filtro che decide "quali elementi del set cacheato corrispondono
ancora alla query piu' specifica" (in `CachingGlobalSearchService`) lavora sulla
`Description` degli item, che nel mockup e' fissa (`"MXP - Malpensa (Italy)"`,
`"AZ178 - MXP -> JFK"`). Prova ad esempio:

```
curl "http://localhost:8083/api/global-search?query=mxp"    # cache miss: interroga il mock, cachea 2 elementi
curl "http://localhost:8083/api/global-search?query=mxpz"   # cache hit su "mxp": filtra in memoria, 0 risultati
curl "http://localhost:8083/api/global-search?query=jfk"    # nessuna query cacheata e' sottostringa: nuovo cache miss
```

Per ispezionare direttamente lo stato di Redis, da terminale:

```
docker exec -it redis redis-cli
> KEYS globalsearch:*
> SMEMBERS globalsearch:known-queries
> TTL globalsearch:results:mxp
```

oppure usa l'interfaccia grafica su http://localhost:8084 (Redis Commander).

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

## Cosa contiene questa consegna

- `airportsservice`, `flightsservice`: codice gia' fornito con il test,
  containerizzato cosi' com'e'.
- `webfrontend`: frontend Blazor Server, con le quattro pagine di ricerca (vedi
  "Oltre il quesito richiesto" in `docs/architecture.md`).
- `globalsearchservice`: implementazione reale della Global Search API —
  aggregazione vera su Airports/Flights dietro la cache Redis completa
  (`CachingGlobalSearchService`, sliding+scadenza assoluta, riuso dei risultati per
  sottostringa). Dettagli in `docs/architecture.md`.
