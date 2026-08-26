# GlobalSearchService.IntegrationTests

Test "dal vivo" contro la stack Docker Compose realmente avviata — nessun mock: chiamano
davvero `GlobalSearchService`, che a sua volta chiama `AirportsService`/`FlightsService`
reali e Redis reale.

## Prerequisito

Avvia prima l'intera stack:

```
cd docker
docker compose up --build
```

Aspetta che tutti i container risultino `Healthy` (vedi output di `docker compose up`)
prima di lanciare i test.

## Come lanciarli

Questo progetto e' **volutamente non referenziato** in `TechInterview.sln`: un normale
`dotnet test` (o "Run All" in Visual Studio Test Explorer) alla radice della solution non
lo tocca mai per sbaglio, e quindi non fallisce se Docker e' spento. Vanno lanciati in modo
esplicito, puntando al progetto:

```
dotnet test tests/GlobalSearchService.IntegrationTests/GlobalSearchService.IntegrationTests.csproj
```

Se vuoi comunque vederli/lanciarli da Visual Studio, puoi aggiungere il progetto alla
solution tu stessa ("Add > Existing Project..."): tieni presente pero' che, fatto questo,
"Run All" in Test Explorer li includerebbe insieme agli unit test, quindi andrebbe sempre
verificato che Docker sia avviato prima di premere "Run All".

## Se una porta e' diversa dal default

Gli URL di default (`http://localhost:8083` per GlobalSearchService) corrispondono ai
valori di `docker/.env`. Se li hai cambiati, sovrascrivi con una variabile d'ambiente prima
di lanciare i test, ad es. in PowerShell:

```
$env:GLOBALSEARCH_BASE_URL = "http://localhost:9999"
dotnet test tests/GlobalSearchService.IntegrationTests/GlobalSearchService.IntegrationTests.csproj
```

## Cosa coprono

Vedi il commento in cima a `GlobalSearchApiLiveTests.cs` per il dettaglio (incluso il
perche' i test non assumono mai un contenuto preciso dei voli/aeroporti, solo proprieta'
statisticamente certe o strutturali) — in breve:

- `/health` risponde.
- Una query troppo corta (`<3` caratteri) da' 400, anche end-to-end.
- Una ricerca su una citta' nota restituisce risultati con gli aeroporti sempre prima dei
  voli.
- Un offset oltre la fine dei risultati da' 200 con items vuoto e il conteggio reale.
- Una stessa query ripetuta due volte e' molto piu' veloce la seconda, grazie alla cache
  Redis — l'unica cosa che, per costruzione, gli unit test (che mockano Redis) non possono
  verificare.
