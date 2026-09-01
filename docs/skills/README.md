# Skill per Claude, dedotte da questo progetto

Questa cartella non fa parte dell'applicazione: raccoglie una prima bozza di quattro
"skill" per Claude (Anthropic) — pacchetti di istruzioni riusabili che insegnano
all'assistente come affrontare bene un certo tipo di compito — dedotte osservando come
è stato effettivamente condotto lo sviluppo di questo prototipo. Il ragionamento che le
ha originate, e i pattern ricorrenti da cui sono state estratte, sono documentati in
`docs/diario-sviluppo.md`.

Sono bozze di prima stesura, non ancora testate su casi reali: il passo successivo
naturale (non ancora fatto) sarebbe eseguirle su qualche compito concreto e rivedere gli
output prima di considerarle mature.

## Indice

- **`rigorous-dev-collaboration/`** — la disciplina di collaborazione osservata per
  l'intero progetto: diagnosi basata sul codice reale, alternative presentate come
  scelta esplicita quando il tradeoff è reale, nessuna riga di codice scritta senza un
  via libera inequivocabile, verifica indipendente dopo ogni implementazione, commit
  come passo separatamente autorizzato.
- **`architecture-decision-log/`** — come mantenere un registro delle decisioni
  architetturali (comprese le alternative scartate e perché) scritto contestualmente
  alla decisione stessa, sul modello di `docs/architecture.md` in questo stesso
  progetto.
- **`device-bridge-git-workflow/`** — come lavorare con git quando il repository è
  raggiunto tramite il bridge di Cowork verso il computer dell'utente: divisione dei
  compiti tra assistente e persona per le operazioni di rete, e soprattutto come
  riconoscere e risolvere i file di lock residui caratteristici di questo bridge
  (il problema che ci ha rallentato più volte durante questa sessione).
- **`process-diary-from-transcript/`** — come trasformare la cronologia reale di una
  conversazione in un documento di processo strutturato (esattamente quello che ha
  prodotto `docs/diario-sviluppo.md`), incluso lo script Python (`scripts/
  extract_transcript.py`) usato per estrarre i prompt reali dal file di trascrizione
  della sessione.
