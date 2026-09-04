# README.md
# Tech Interview – Global Search API Challenge

## Stato di questo branch

Questo file descrive il quesito originale della sfida, lasciato invariato.
Questo branch (`main`) rappresenta lo stato del progetto all'approccio
iniziale — containerizzazione dell'ambiente fornito e primo mockup della
Global Search API con cache Redis gia' completa — fermo intenzionalmente a
questo punto come riferimento.

La consegna finale, con l'implementazione reale della ricerca globale e una
parte di presentazione visiva aggiuntiva, e' sul branch `prototype/real-search`.

- Note architetturali di questo branch: [`docs/architecture.md`](docs/architecture.md)
- Come avviare l'ambiente di questo branch (servizi, porte, esempi): [`docker/README.md`](docker/README.md)

---

## Contesto
Questa solution contiene due microservizi .NET 8 (Aspire):

1. **Aeroporti Service**  
   - Espone API **REST**.  
   - Gestisce operazioni **CRUD** sugli aeroporti.

2. **Voli Service**  
   - Espone API **gRPC**.  
   - Gestisce operazioni **CRUD** sui voli.

Questi due sistemi rappresentano due fonti dati separate. I rispettivi contratti e modelli sono disponibili all’interno della solution.

---

## Obiettivo del task
Il tuo compito è progettare e implementare una **funzionalità di ricerca globale** che, dato un termine di ricerca testuale, consenta di ottenere risultati provenienti sia dal dominio *Aeroporti* sia dal dominio *Voli*.

L’API di ricerca globale deve:

- accettare una **stringa di ricerca** di almeno **3 caratteri**;
- accettare parametri di paginazione: `offset`, `limit`;
- restituire una lista di risultati eterogenei, ciascuno con:
  - `id`
  - `resourceType`
  - `description`
- rispettare il limite imposto da `limit`.

### Campi da includere nella ricerca
**Aeroporti**: codice, nome, città, nazione.  
**Voli**: codice volo, numero aeromobile, città di partenza/arrivo, codice aeroporto di partenza/arrivo.

---

## Libertà architetturali
La progettazione è completamente **a tua discrezione**.
Puoi costruire la Global Search API come:

- un nuovo microservizio dedicato;
- un'estensione di un progetto esistente;
- un orchestratore;
- un servizio con proprio database o cache;
- qualunque architettura tu ritenga appropriata.

Non ci sono vincoli: valuta integrazione, modellazione, performance e manutenibilità.

---

## Output atteso
Endpoint HTTP previsto:
```
GET /api/global-search?query={string}&offset={n}&limit={n}
```

### Esempio risposta
```json
{
  "items": [
    {
      "id": "MXP",
      "resourceType": "airport",
      "description": "MXP – Malpensa (Italy)"
    },
    {
      "id": "AZ178",
      "resourceType": "flight",
      "description": "AZ178 – MXP → JFK"
    }
  ],
  "offset": 0,
  "limit": 10,
  "count": 2
}
```

---

## Requisiti minimi
- Query con almeno 3 caratteri.  
- Rispetto del limite `limit`.  
- `description` leggibile.  
- Solution compilabile ed eseguibile.

---

## Bonus facoltativi
- caching o indicizzazione locale
- resilienza (retry, timeouts, circuit breaker)
- test automatici
- logging strutturato
- uso efficace di Aspire

---

## Consegna
Fornire:
- codice dell’implementazione
- eventuali note architetturali
- istruzioni di esecuzione
