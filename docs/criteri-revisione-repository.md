# Criteri per la revisione dei file nel repository

Nota introduttiva: questo documento raccoglie il criterio usato per decidere cosa
rimuovere dal repository nella revisione pre-consegna di `prototype/real-search`,
concordato in chat il 1 settembre 2026. Non riguarda scelte di architettura del
sistema (per quelle vedi `architecture.md`), ma la governance del repository stesso —
serve a non dover rinegoziare da zero il criterio a ogni nuovo giro di pulizia.

## Criterio generale

Un file **non va rimosso** se non è stato creato da Ilaria Fiorini **e** risale a
prima del suo primo intervento sul progetto (commit `92b76a5`, 17/08/2026). In pratica:
il materiale originale della sfida — lo scaffold e il codice forniti da Massimo Fornari
nei commit iniziali (`244b5f5`, `741f1af`, `3bf1081`, `871fcc3`, `741f1af`, `320d93c`,
`3aec02e`, `fb6951b`, `adf6e27`) — resta intoccabile in questa revisione, anche quando
sembra inutilizzato o ridondante.

La riorganizzazione del repository in `src/`/`docs/`/`docker/`/`tests/` (commit
`92b76a5`) ha spostato molti di quei file di percorso senza modificarne il contenuto:
per stabilire la vera origine di un file non basta guardare la prima riga con
`--diff-filter=A`, serve il rilevamento dei rename:

```bash
git log --all --follow --diff-filter=A --format="%h %ad %an" --date=short -- <path>
```

## Eccezione: metadati IDE e impostazioni personali

Restano candidati validi alla rimozione, **indipendentemente da chi li abbia creati o
committati per primo**, i file che sono metadati dell'IDE o impostazioni personali già
esclusi dal `.gitignore` del progetto stesso. In questo caso non si tratta di materiale
della sfida, ma di file che il `.gitignore` del progetto dichiara esplicitamente di non
voler tracciare — sono finiti nella storia solo perché committati prima che la relativa
regola di `.gitignore` entrasse in vigore. Il criterio generale protegge il materiale
originale della sfida, non le tracce accidentali degli strumenti di sviluppo di chi lo
ha scritto.

Caso concreto — commit `e676301` (2 settembre 2026): sono stati rimossi 7 file, di cui
5 rientrano in questa eccezione perché risalgono ai commit iniziali di Massimo Fornari
(`244b5f5`, `741f1af`), non al lavoro di Ilaria:

- `.idea/.idea.TechInterview/.idea/.gitignore`
- `.idea/.idea.TechInterview/.idea/encodings.xml`
- `.idea/.idea.TechInterview/.idea/indexLayout.xml`
- `.idea/.idea.TechInterview/.idea/vcs.xml`
- `TechInterview.sln.DotSettings.user`

Gli altri 2 file dello stesso commit (`_to_delete/_mvtest_dirdst/sub/f.txt` e
`_to_delete/_mvtest_dst/dummy2.txt`, residui di un test di spostamento file) erano
invece stati creati da Ilaria stessa (commit `f89f03d`) e sarebbero stati rimovibili
anche senza bisogno dell'eccezione.

## Casi esaminati e mantenuti, per riferimento

Applicando il criterio generale, due candidati apparentemente ragionevoli sono stati
esaminati e **scartati** (nessuna rimozione):

- **`src/TechInterview.Web/wwwroot/lib/bootstrap/dist/`** — 44 file, di cui uno solo
  (`bootstrap.min.css`) effettivamente referenziato in `App.razor`; gli altri 43 (mappe
  sorgente, varianti RTL, bundle/ESM non minificati) non sono mai caricati da nessuna
  pagina. Nonostante l'apparente inutilizzo, l'intera cartella risale al commit iniziale
  `244b5f5` di Massimo Fornari: è materiale della sfida, non va toccata.
- **`src/TechInterview.Web/Components/Pages/Counter.razor`** — pagina demo di default
  dello scaffold Blazor (`/counter`), non linkata da `NavMenu.razor` né referenziata
  altrove. Stessa origine (`244b5f5`): materiale della sfida, resta.

Nessuno dei due è un metadato IDE o un'impostazione personale, quindi l'eccezione sopra
non si applica: restano protetti dal criterio generale.
