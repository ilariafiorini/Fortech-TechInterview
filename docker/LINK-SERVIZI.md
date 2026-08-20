# Link rapidi — ambiente Docker

Riferimento veloce a tutte le interfacce web esposte da `docker compose up --build`
(dalla cartella `docker/`). Le porte sono quelle di **default**: se le hai cambiate in
`docker/.env`, sostituiscile di conseguenza.

| Servizio             | URL                                  | Cosa trovi                                                   |
|-----------------------|---------------------------------------|----------------------------------------------------------------|
| webfrontend            | http://localhost:8080                 | Frontend Blazor Server (pagine Flights, Search, ecc.)          |
| airportsservice        | http://localhost:8081/scalar          | Scalar UI — REST CRUD aeroporti (anche su `/`, redirect automatico) |
| globalsearchservice    | http://localhost:8083/scalar          | Scalar UI — Global Search API, **attualmente un mockup** (anche su `/`) |
| aspire-dashboard       | http://localhost:18888                | Dashboard OpenTelemetry: log, tracce e metriche di tutti i servizi |
| redis-commander        | http://localhost:8084                 | UI web per ispezionare a mano le chiavi cacheate in Redis      |
| flightsservice         | *nessuna UI web di default*           | API puramente gRPC — vedi sotto                                |
| redis                  | *nessuna UI web*                      | Protocollo Redis puro (`localhost:6379`); usa redis-commander sopra, oppure `redis-cli` |

## FlightsService (gRPC)

Non essendo REST, non ha una pagina web fissa da aprire. Con l'ambiente Docker avviato,
apri un'interfaccia interattiva al volo (richiede la reflection gRPC, già attiva in
Development — vedi `docker/README.md`):

```
grpcui -plaintext localhost:8082
```

Il comando stampa un URL locale generato al momento (es. `http://127.0.0.1:xxxxx/`):
aprilo nel browser per il form interattivo. In alternativa, da riga di comando:

```
grpcurl -plaintext localhost:8082 list
```

## Endpoint utili non "esplorabili" da browser

- `http://localhost:<porta-servizio>/health` e `/alive` — health check letti da
  Docker Compose (`depends_on: condition: service_healthy`), non pensati per essere
  aperti a mano.
- `http://localhost:18889` — endpoint OTLP dell'Aspire Dashboard (ricezione telemetria
  dai servizi); non è una UI, è solo il dashboard su 18888 a mostrarti i dati.

---

Dettagli su porte parametrizzabili, healthcheck e comandi di verifica: `docker/README.md`.
