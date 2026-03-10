# Resume Migration Checkpoint - Predlog resenja

## Cilj

Omoguciti korisniku nastavak prethodne neuspesne migracije od tacke prekida, umesto pokretanja migracije ispocetka.

---

## Pregled trenutnog stanja

### Faze migracije (MigrationWorker.RunAsync)

| # | Faza | Servis | Opis |
|---|------|--------|------|
| 1 | Priprema baze | `MigrationPreparationService` | Brise sve sto nije DONE/ERROR |
| 2 | Pretraga dokumenata | `DocumentSearchService` | Pretrazuje Alfresco po `ecm:docDesc`, populise DocStaging sa statusom READY |
| 3 | Priprema foldera | `FolderPreparationService` | Kreira destination foldere, READY → PREPARATION → PREPARED |
| 4 | Premestanje | `MoveService` | Premesta dokumente, PREPARED → IN_PROGRESS → DONE |

### Trenutni tok pri ponovnom pokretanju

```
Pokretanje migracije
│
├─ PrepareForMigrationAsync()
│   ├─ DELETE FROM DocStaging WHERE Status NOT IN ('DONE','ERROR')   ← PROBLEM
│   └─ DELETE FROM FolderStaging WHERE Status NOT LIKE 'DONE%'      ← PROBLEM
│
└─ Sve faze krecu ispocetka
```

**Problem:** Sav napredak iz prethodnog pokusaja (READY, PREPARATION, PREPARED, IN_PROGRESS dokumenti) se gubi.

---

## Predlozeno resenje

### Pregled promena

```
Pokretanje migracije
│
├─ PrepareForMigrationAsync()               ← IZMENA
│   ├─ UPDATE: PREPARATION → READY          (DocStaging)
│   ├─ UPDATE: IN_PROGRESS → PREPARED       (DocStaging)
│   ├─ UPDATE: IN_PROGRESS → READY          (FolderStaging)
│   └─ NEMA BRISANJA
│
├─ Proveri PhaseCheckpoints
│   │
│   ├─ DocumentSearch = Completed?  → PRESKOCI
│   ├─ DocumentSearch = InProgress/Failed?
│   │   └─ Nastavi sa sacuvanim skipCount-om per folder   ← NOVO
│   │
│   ├─ FolderPreparation = Completed? → PRESKOCI
│   ├─ FolderPreparation = InProgress/Failed?
│   │   └─ Nastavi normalno (uzima READY dokumente)       ← BEZ PROMENE
│   │
│   ├─ Move = Completed? → PRESKOCI
│   └─ Move = InProgress/Failed?
│       └─ Nastavi normalno (uzima PREPARED dokumente)    ← BEZ PROMENE
```

---

## Detaljan opis promena po fajlovima

---

### KORAK 1: Izmena cleanup logike

**Fajl:** `Migration.Extensions/SqlServer/RepositoryExtensions.cs`

#### 1.1 Zameniti `DeleteIncompleteDocumentsAsync`

**Trenutno:**
```sql
DELETE FROM DocStaging
WHERE (Status != 'DONE' AND Status != 'ERROR')
   OR Status IS NULL
```

**Novo - dodati metodu `ResetIncompleteDocumentsAsync`:**
```sql
-- Reset PREPARATION nazad na READY (folder prep bio u toku kad je puklo)
UPDATE DocStaging
SET Status = 'READY',
    UpdatedAt = GETUTCDATE()
WHERE Status = 'PREPARATION'

-- Reset IN PROGRESS nazad na PREPARED (move bio u toku kad je puklo)
UPDATE DocStaging
SET Status = 'PREPARED',
    UpdatedAt = GETUTCDATE()
WHERE Status = 'IN PROGRESS'
```

- `READY` ostaje READY - validan za FolderPreparation
- `PREPARED` ostaje PREPARED - validan za Move
- `DONE` ostaje - zavrsen
- `ERROR` ostaje - za manuelnu istragu
- `NULL` - opciono obrisati ili resetovati na READY

#### 1.2 Zameniti `DeleteIncompleteFoldersAsync`

**Trenutno:**
```sql
DELETE FROM FolderStaging
WHERE Status NOT LIKE 'DONE%'
   OR Status IS NULL
```

**Novo - dodati metodu `ResetIncompleteFoldersAsync`:**
```sql
UPDATE FolderStaging
SET Status = 'READY',
    UpdatedAt = GETUTCDATE()
WHERE Status = 'IN PROGRESS'
```

- Folderi koji su READY ostaju READY
- Folderi koji su DONE ostaju DONE
- Folderi koji su bili IN PROGRESS se resetuju na READY

---

### KORAK 2: Izmena MigrationPreparationService

**Fajl:** `Migration.Infrastructure/Implementation/Services/MigrationPreparationService.cs`

#### 2.1 Promeniti `PrepareForMigrationAsync`

**Trenutno poziva:**
- `docRepo.DeleteIncompleteDocumentsAsync()`
- `folderRepo.DeleteIncompleteFoldersAsync()`

**Novo - pozivati:**
- `docRepo.ResetIncompleteDocumentsAsync()`
- `folderRepo.ResetIncompleteFoldersAsync()`

**Promeniti povratni tip `MigrationPreparationResult`:**
- Umesto `DeletedDocuments` i `DeletedFolders`
- Koristiti `ResetDocuments` i `ResetFolders` (broj resetovanih zapisa)

---

### KORAK 3: Prosirenje PhaseCheckpoint modela

**Fajl:** `Alfresco.Contracts/Oracle/Models/PhaseCheckpoint.cs`

#### 3.1 Dodati novo polje

```csharp
public string? FetchedCountsPerFolder { get; set; }
```

Ovo polje cuva JSON sa brojem **ukupno dohvacenih dokumenata sa Alfresco API-ja** po svakom DOSSIER folderu:

```json
{
  "PI": 45000,
  "LE": 12000,
  "D": 0,
  "ACC": 0
}
```

**Vazno:** Ovo je broj SVIH dohvacenih dokumenata sa Alfresco-a (ukljucujuci i one koji su filtrirani/preskoceni jer su vec migrirani). Ne samo upisanih u DocStaging.

#### 3.2 Dodati kolonu u bazu

```sql
ALTER TABLE PhaseCheckpoints
ADD FetchedCountsPerFolder NVARCHAR(MAX) NULL
```

---

### KORAK 4: Izmena PhaseCheckpointRepository

**Fajl:** `SqlServer.Infrastructure/Implementation/PhaseCheckpointRepository.cs`

#### 4.1 Dodati metodu `UpdateFetchedCountsAsync`

Nova metoda koja azurira `FetchedCountsPerFolder` kolonu:

```sql
UPDATE PhaseCheckpoints
SET FetchedCountsPerFolder = @fetchedCountsJson,
    TotalProcessed = @totalProcessed,
    LastProcessedIndex = @lastFolderTypeIndex,
    UpdatedAt = GETUTCDATE()
WHERE Phase = @phase
```

#### 4.2 Prosirititi `GetCheckpointAsync`

Osigurati da SELECT ukljucuje novo polje `FetchedCountsPerFolder`.

---

### KORAK 5: Izmena DocumentSearchService - Glavni deo

**Fajl:** `Migration.Infrastructure/Implementation/Services/DocumentSearchService.cs`

Ovo je najbitnija i najkompleksnija promena.

#### 5.1 Dodati globalni thread-safe brojac po folderu

```csharp
// Mapa: folderType -> ukupno dohvacenih sa Alfresco-a (ukljucujuci filtrirane)
private ConcurrentDictionary<string, long> _fetchedCountsPerFolder = new();
```

#### 5.2 Brojati SVE dohvacene dokumente sa Alfresco-a

U metodi koja procesira batch odgovor od Alfresco API-ja, NAKON sto se dobije response, ali PRE filtriranja:

```
Pseudo-kod:

response = await AlfrescoApi.SearchAsync(request)
allDocuments = response.List.Entries     // SVA dokumenta iz response-a

// Brojati SVE dohvacene, pre filtriranja
Interlocked.Add(ref fetchedCountForThisFolder, allDocuments.Count)

// Tek onda filtrirati
filteredDocuments = allDocuments.Where(doc => ...)  // filter logika
```

**Zasto se broje svi a ne samo filtrirani?**
Jer skipCount radi na nivou Alfresco pretrage - Alfresco ne zna za nase filtere. Ako smo dohvatili 45000 dokumenata (od kojih je 30000 proslo filter), moramo nastaviti od skipCount=45000 da ne bi ponovo dohvatali istih 45000.

#### 5.3 Periodican checkpoint save

Na kraju svakog batch-a (ili svakih N batch-eva), snimiti progres:

```
Pseudo-kod:

// Serijalizovati mapu u JSON
fetchedCountsJson = JsonSerializer.Serialize(_fetchedCountsPerFolder)

// Sacuvati u PhaseCheckpoint
await _checkpointRepo.UpdateFetchedCountsAsync(
    phase: MigrationPhase.DocumentSearch,
    fetchedCountsJson: fetchedCountsJson,
    totalProcessed: _totalDocumentsProcessed,
    lastFolderTypeIndex: _currentFolderTypeIndex
)
```

**Frekvencija snimanja checkpoint-a:**
- Posle svakog uspesno zavrsenog batch-a, ili
- Svakih N batch-eva (npr. 5-10), zavisno od performansi

#### 5.4 Resume logika pri pokretanju

Na pocetku `RunBatchAsync` / `RunLoopAsync`, pre pocetka petlje:

```
Pseudo-kod:

checkpoint = await _checkpointRepo.GetCheckpointAsync(MigrationPhase.DocumentSearch)

if (checkpoint != null && checkpoint.Status == InProgress/Failed)
{
    // Ucitaj sacuvane brojace
    _fetchedCountsPerFolder = JsonSerializer.Deserialize(checkpoint.FetchedCountsPerFolder)
    _currentFolderTypeIndex = checkpoint.LastProcessedIndex ?? 0
    _totalDocumentsProcessed = checkpoint.TotalProcessed

    // Za svaki folder tip koji je vec zavrsen (index < _currentFolderTypeIndex)
    // → preskociti potpuno

    // Za tekuci folder tip (index == _currentFolderTypeIndex)
    // → poceti sa skipCount = _fetchedCountsPerFolder[currentFolderType]
}
```

#### 5.5 Prilagoditi petlju po folder tipovima

Trenutno petlja ide redom po svim DOSSIER folderima (PI, LE, D, ACC...).

**Izmena:** Pri resume-u, preskociti foldere ciji index je manji od `_currentFolderTypeIndex`:

```
Pseudo-kod:

for (int i = _currentFolderTypeIndex; i < folderTypes.Count; i++)
{
    var folderType = folderTypes[i]
    _currentFolderTypeIndex = i

    // Odrediti pocetni skipCount
    int startSkipCount = 0
    if (_fetchedCountsPerFolder.TryGetValue(folderType, out var savedCount))
    {
        startSkipCount = (int)savedCount
    }

    // Generisati skip vrednosti od startSkipCount
    var skipValues = Enumerable
        .Range(0, ...)
        .Select(j => startSkipCount + j * batchSize)
        .ToList()

    // Paralelna obrada
    await Parallel.ForEachAsync(skipValues, parallelOptions, async (skipCount, token) =>
    {
        var result = await SearchDocumentsAsync(folderId, skipCount, batchSize, token)

        // Brojati SVE dohvacene
        Interlocked.Add(ref _fetchedCountsPerFolder[folderType], result.AllDocuments.Count)

        // Filtrirati i procesirati
        ...
    })

    // Sacuvati checkpoint na kraju foldera
    await SaveCheckpointAsync()
}
```

#### 5.6 Dedup zastita pri insertu

Posto nastavljamo sa skipCount-om, i dokumenti se sortiraju po `cm:created` u fiksnom vremenskom periodu (2021-2023), nema rizika od pomeranja pozicija. Medjutim, kao dodatna zastita, INSERT u DocStaging bi trebao da koristi `MERGE` ili `INSERT ... WHERE NOT EXISTS` po `NodeId` koloni da se spreci duplikat ako batch granica padne na isti dokument.

**Napomena:** Ako vec postoji MERGE logika za insert - proveriti da li je po NodeId.

---

### KORAK 6: Izmena MigrationWorker orchestration

**Fajl:** `Migration.Infrastructure/Implementation/Services/MigrationWorker.cs`

#### 6.1 Prilagoditi `ExecutePhaseAsync` logiku

Trenutna logika u `ExecutePhaseAsync` vec proverava checkpoint status i preskace Completed faze. Potrebno je osigurati da za `InProgress` i `Failed` status poziva fazu ponovo (sa resume logikom koja je u samom servisu).

Proveriti da logika izgleda ovako:

```
Pseudo-kod:

checkpoint = await GetCheckpoint(phase)

if (checkpoint.Status == Completed)
{
    Log("Phase already completed, skipping")
    return  // Preskoci
}

if (checkpoint.Status == InProgress || checkpoint.Status == Failed)
{
    Log("Resuming phase from checkpoint")
    // Faza sama zna da nastavi od checkpoint-a (korak 5.4)
}

// Pokreni/nastavi fazu
await MarkPhaseStarted(phase)
await phaseAction(ct)
await MarkPhaseCompleted(phase)
```

#### 6.2 Zamena poziva PrepareForMigration

Osigurati da se poziva nova verzija `PrepareForMigrationAsync` koja resetuje umesto brise (korak 2).

---

### KORAK 7: FolderPreparationService - Minimalna promena

**Fajl:** `Migration.Infrastructure/Implementation/Services/FolderPreparationService.cs`

**Potrebna promena:** NEMA izmena u logici servisa.

Servis vec:
- Ima `ResetStuckItemsAsync()` i `ResetStuckFoldersAsync()` metode
- Uzima dokumente sa statusom READY
- Radi sa checkpoint-ima i resume logikom

Jedino sto je potrebno je da cleanup (korak 1) ne obrise READY dokumente, sto smo vec resili.

**Verifikacija:** Proveriti da `GetUniqueFoldersAsync()` korektno radi i kad u DocStaging vec postoje PREPARED dokumenti iz prethodnog pokusaja - trebalo bi da ih ignorise jer trazi samo READY.

---

### KORAK 8: MoveService - Minimalna promena

**Fajl:** `Migration.Infrastructure/Implementation/Services/MoveService.cs`

**Potrebna promena:** NEMA izmena u logici servisa.

Servis vec:
- Ima `ResetStuckItemsAsync()` za resetovanje zaglavljenih IN_PROGRESS dokumenata
- Uzima dokumente sa statusom PREPARED
- Ima checkpoint i resume logiku

Jedino sto je potrebno je da cleanup (korak 1) ne obrise PREPARED dokumente i da resetuje IN_PROGRESS na PREPARED, sto smo vec resili.

---

## Rezime promena po fajlovima

| # | Fajl | Tip promene | Kompleksnost |
|---|------|-------------|--------------|
| 1 | `RepositoryExtensions.cs` | Zamena DELETE sa UPDATE | Niska |
| 2 | `MigrationPreparationService.cs` | Poziv novih metoda umesto starih | Niska |
| 3 | `PhaseCheckpoint.cs` | Dodavanje jednog polja | Niska |
| 4 | Migracija baze | ALTER TABLE - nova kolona | Niska |
| 5 | `PhaseCheckpointRepository.cs` | Nova metoda + prosirenje SELECT-a | Niska |
| 6 | `DocumentSearchService.cs` | Brojac, checkpoint, resume logika | **Visoka** |
| 7 | `MigrationWorker.cs` | Minimalna prilagodba | Niska |
| 8 | `FolderPreparationService.cs` | Bez promene (verifikacija) | - |
| 9 | `MoveService.cs` | Bez promene (verifikacija) | - |

---

## Dijagram statusa sa resume-om

```
                    PRVI POKUSAJ                         RESUME POKUSAJ
                    ============                         ==============

DocStaging:         READY ─────────┐
                      │            │ (prekid)
                      ▼            │
                  PREPARATION ─────┤                  PREPARATION ──reset──► READY
                      │            │                                           │
                      ▼            │                                           ▼
                   PREPARED ───────┤                   PREPARED            PREPARATION
                      │            │                      │                    │
                      ▼            │                      ▼                    ▼
                  IN_PROGRESS ─────┘                  IN_PROGRESS──reset──► PREPARED
                      │                                                       │
                      ▼                                                       ▼
                    DONE                                 DONE             IN_PROGRESS
                                                                              │
                                                                              ▼
                                                                            DONE
```

---

## Rizici i mitigacije

| Rizik | Mitigacija |
|-------|-----------|
| Duplikati u DocStaging pri resume-u pretrage | MERGE/INSERT WHERE NOT EXISTS po NodeId |
| Checkpoint se ne snimi pre pada | Cesce snimanje (svaki batch); gubitak je max 1 batch koji se ponovo dohvati |
| FolderPreparation uzme vec PREPARED dokument | Servis filtrira po Status='READY', PREPARED se ignorise |
| Paralelni batch-evi i Interlocked tacnost | `Interlocked.Add` je atomican, ConcurrentDictionary je thread-safe |
| Veliki JSON u FetchedCountsPerFolder | Max 4-5 folder tipova, JSON je zanemarljive velicine |

---

## Sekvenca poziva pri normalnom pokretanju vs resume

### Normalno pokretanje (prvi put)

```
1. PrepareForMigration → Nista za reset (prazna baza)
2. DocumentSearch:
   - checkpoint = null/NotStarted
   - Krece od PI folder, skipCount=0
   - Procesira sve foldere
   - Cuva checkpoint periodicno
3. FolderPreparation → Procesira READY dokumente
4. Move → Procesira PREPARED dokumente
```

### Resume pokretanje (nakon pada u DocumentSearch fazi)

```
1. PrepareForMigration → Reset PREPARATION→READY, IN_PROGRESS→PREPARED
2. DocumentSearch:
   - checkpoint = InProgress, FetchedCountsPerFolder = {"PI":45000,"LE":12000}
   - PI folder (index 0) < currentIndex (1) → PRESKOCI
   - LE folder (index 1) = currentIndex → skipCount=12000, NASTAVI
   - D, ACC folderi → procesirati normalno od skipCount=0
3. FolderPreparation → Procesira READY dokumente (stare + nove)
4. Move → Procesira PREPARED dokumente (stare + nove)
```

### Resume pokretanje (nakon pada u FolderPreparation fazi)

```
1. PrepareForMigration → Reset PREPARATION→READY, IN_PROGRESS→PREPARED
2. DocumentSearch:
   - checkpoint = Completed → PRESKOCI
3. FolderPreparation:
   - checkpoint = InProgress
   - ResetStuckItems (vec postoji)
   - Uzima READY dokumente (resetovani + oni koji nisu bili procesirani)
   - Nastavlja normalno
4. Move → Procesira PREPARED dokumente
```

### Resume pokretanje (nakon pada u Move fazi)

```
1. PrepareForMigration → Reset IN_PROGRESS→PREPARED
2. DocumentSearch → checkpoint = Completed → PRESKOCI
3. FolderPreparation → checkpoint = Completed → PRESKOCI
4. Move:
   - checkpoint = InProgress
   - ResetStuckItems (vec postoji)
   - Uzima PREPARED dokumente (resetovani + oni koji nisu bili premesteni)
   - Nastavlja normalno
```
