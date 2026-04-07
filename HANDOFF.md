# HANDOFF — PreviewTypeMigration

**Datum:** 2026-04-07  
**Poslednji commit:** `765cdbe Version 5.2.0`  
**Trenutna grana:** `master`

---

## Šta je PreviewTypeMigration

Zasebna migraciona putanja za "Preview" dokumente (PI i LE dosijei).  
Koristi svoju tabelu `PreviewDocStaging` (SQL Server) i ima sopstvene faze:

| Faza | Servis | Opis |
|------|--------|------|
| 1 | `PreviewLoadService` | Čita dokumente iz Alfresca (PI i/ili LE folderi — izbor na UI), upisuje u `PreviewDocStaging` sa statusom `PENDING` |
| 2 | `PreviewFolderPreparationService` | Proverava da li destination folder postoji na Alfresciu; zove ClientAPI za enrichment; status → `FOLDER_PENDING_EXISTS` ili `FOLDER_PENDING_CREATION` |
| 3 | `PreviewFolderCreationService` | Kreira Alfresco foldere za `FOLDER_PENDING_CREATION`; potvrđuje postojeće za `FOLDER_PENDING_EXISTS`; status → `FOLDER_CREATED` ili `FOLDER_EXISTS`; upisuje u `FolderStaging` |
| 6 | `PreviewToStagingTransferService` | Prenosi `FOLDER_EXISTS`/`FOLDER_CREATED` zapise u `DocStaging`; status → `TRANSFERRED` |
| 7 | `PreviewExportService` | Eksportuje `PreviewDocStaging` u `.xlsx` (sheet-ovi po `TargetDossierType`) |
| R | `PreviewFolderRollbackService` | **(TEST)** — briše kreirane Alfresco foldere i resetuje status na `FOLDER_PENDING_CREATION` |

---

## Status tabele `PreviewDocStaging`

```
PENDING                  → početni status nakon Faze 1
IN_PROGRESS              → privremeni (atomično zauzimanje batch-a za Fazu 2 ili 3)
FOLDER_PENDING_EXISTS    → folder pronađen u Alfresciu tokom Faze 2, čeka Fazu 3 da potvrdi
FOLDER_PENDING_CREATION  → folder ne postoji, ClientAPI enrichment obavljen (Faza 2), čeka Fazu 3 da kreira
FOLDER_EXISTS            → FINAL — folder potvrđen u Fazi 3 (nije trebalo kreirati)
FOLDER_CREATED           → FINAL — folder kreiran u Fazi 3
TRANSFERRED              → prebačen u DocStaging (Faza 6)
```

**Tok statusa:**
```
Faza 1: PENDING
Faza 2: PENDING → (IN_PROGRESS lock) → FOLDER_PENDING_EXISTS | FOLDER_PENDING_CREATION
Faza 3: FOLDER_PENDING_EXISTS | FOLDER_PENDING_CREATION → (IN_PROGRESS lock) → FOLDER_EXISTS | FOLDER_CREATED
Faza 6: FOLDER_EXISTS | FOLDER_CREATED → TRANSFERRED
```

> **NAPOMENA:** `FOLDER_PENDING_EXISTS` je uveden da razbije beskonačnu petlju u Fazi 3.  
> `FOLDER_EXISTS` je sada isključivo **finalni** status — Faza 3 ga nikada ne čita, samo upisuje.  
> Pre ove izmene Faza 2 je direktno pisala `FOLDER_EXISTS`, a Faza 3 je čitala `FOLDER_EXISTS`,  
> što je uzrokovalo beskonačno ponavljanje obrade istih foldera.

---

## Arhitektura projekata

```
Alfresco.Contracts/Oracle/Models/PreviewDocStaging.cs      — model tabele
SqlServer.Abstraction/Interfaces/IPreviewDocStagingRepository.cs
SqlServer.Infrastructure/Implementation/PreviewDocStagingRepository.cs
Migration.Abstraction/Interfaces/Wrappers/
    IPreviewLoadService.cs
    IPreviewFolderPreparationService.cs
    IPreviewFolderCreationService.cs
    IPreviewToStagingTransferService.cs
    IPreviewExportService.cs
    IPreviewFolderRollbackService.cs              ← NOVO
Migration.Infrastructure/Implementation/Services/
    PreviewLoadService.cs
    PreviewFolderPreparationService.cs
    PreviewFolderCreationService.cs
    PreviewToStagingTransferService.cs
    PreviewExportService.cs
    PreviewFolderRollbackService.cs               ← NOVO
Alfresco.App/UserControls/PreviewMigrationUC.xaml(.cs)    — WPF UI
Alfresco.App/App.xaml.cs                                   — DI registracija
Alfresco.App/appsettings.json                              — konfiguracija
```

---

## Izmene u sesiji 2026-04-07

### 1. PreviewLoadService — reset stanja i filter po folderu

**Problem:** Servis je singleton, pa su `_totalDocumentsProcessed` i ostali brojači zadržavali vrednosti iz prethodnog pokretanja, sprečavajući ponovni load.

**Fix — reset na početku `RunLoopAsync`:**
```csharp
_totalDocumentsProcessed = 0;
_totalFailed = 0;
_batchCounter = 0;
_currentFolderTypeIndex = 0;
_fetchedCountsPerFolder = new ConcurrentDictionary<string, long>();
```

**Nova funkcionalnost — filter izvora (PI / LE / sve):**
- `IPreviewLoadService` dobio novi overload: `RunLoopAsync(ct, progressCallback, string? folderFilter)`
- Stari overload delegira na novi sa `folderFilter: null`
- Ako `folderFilter = "PI"`, procesira samo PI folder; `null` = oba
- UI: `CmbFaza1FolderFilter` ComboBox u Faza 1 kartici (vrednosti: "Sve (PI + LE)", "Samo PI", "Samo LE")

### 2. Uveden status FOLDER_PENDING_EXISTS (fix beskonačne petlje u Fazi 3)

**Problem:** Faza 2 je pisala `FOLDER_EXISTS` za folder koji postoji u Alfresciu. Faza 3 je čitala `FOLDER_EXISTS` — lock `IN_PROGRESS` — ali nikada nije ažurirala status na ništa, pa su se isti zapisi beskonačno ponavljali u obradi.

**Rešenje:**
- Faza 2 (`PreviewFolderPreparationService`) sada piše `FOLDER_PENDING_EXISTS` umesto `FOLDER_EXISTS`
- Faza 3 (`PreviewFolderCreationService`) čita `FOLDER_PENDING_EXISTS` + `FOLDER_PENDING_CREATION`
  - Za `FOLDER_PENDING_EXISTS`: poziva novi `PersistExistingFolderAsync` → piše `FOLDER_EXISTS` (finalni)
  - Za `FOLDER_PENDING_CREATION`: kreira folder → piše `FOLDER_CREATED` (finalni)
  - Error reset vraća na odgovarajući prethodni status (`FOLDER_PENDING_EXISTS` ili `FOLDER_PENDING_CREATION`)
- `GetDistinctFoldersForFolderStagingAsync` u `PreviewDocStagingRepository` — SQL uslov promenjen:
  ```sql
  WHERE Status IN ('FOLDER_PENDING_CREATION', 'FOLDER_PENDING_EXISTS')
  -- bylo: WHERE Status IN ('FOLDER_PENDING_CREATION', 'FOLDER_EXISTS')
  ```

**Izmenjeni fajlovi:**
- `Migration.Infrastructure/Implementation/Services/PreviewFolderPreparationService.cs`
- `Migration.Infrastructure/Implementation/Services/PreviewFolderCreationService.cs`
- `SqlServer.Infrastructure/Implementation/PreviewDocStagingRepository.cs`
- `Alfresco.App/UserControls/PreviewMigrationUC.xaml.cs` (stats za novi status)

### 3. UI izmene (PreviewMigrationUC)

- **Faza 1 kartica:** Dodat `CmbFaza1FolderFilter` — izbor izvora foldera (Sve / PI / LE)
- **`RefreshStatisticsAsync`:** Statistike sada broje `FOLDER_PENDING_EXISTS` zajedno sa ostalim pending statusima
- **`BtnStartFaza1_Click`:** Čita odabrani filter iz ComboBox-a i prosleđuje servisu

---

## Izmene u ovoj sesiji (prethodna — UNCOMMITTED)

### Uklonjena funkcionalnost
- Filteri na Preview Data tab-u (dosije + tip dokumenta) — korisnik odlučio da ih ne treba
- Status filter na eksportu — korisnik odlučio da ga ne treba
- Parametri `dossierType`, `documentType`, `status` iz `GetPagedAsync` — vraćeno na `(pageNumber, pageSize)` samo

### Dodana funkcionalnost: Rollback Faze 3
**Cilj:** samo za testiranje — brisanje foldera kreiranih u Fazi 3

**Novi/izmenjeni fajlovi:**

1. **`IPreviewDocStagingRepository`** — dodata metoda:
   ```csharp
   Task<IEnumerable<(string FolderName, string FolderId)>> GetCreatedFolderIdsAsync(CancellationToken ct = default);
   ```

2. **`PreviewDocStagingRepository`** — implementacija (Dapper, private `FolderRow` klasa):
   ```sql
   SELECT DISTINCT DossierDestinationFolderName, DossierDestinationFolderId
   FROM PreviewDocStaging
   WHERE Status = 'FOLDER_CREATED'
     AND ISNULL(DossierDestinationFolderId, '') <> ''
     AND ISNULL(DossierDestinationFolderName, '') <> ''
   ```

3. **`IPreviewFolderRollbackService`** — nova interface datoteka

4. **`PreviewFolderRollbackService`** — implementacija:
   - Dohvata sve FOLDER_CREATED foldere iz DB
   - `Parallel.ForEachAsync` sa MDP=5 za paralelno brisanje
   - Za svaki: `IAlfrescoWriteApi.DeleteNodeAsync(folderId)`
   - Nakon uspešnog brisanja: `UpdateFolderDataAsync(folderName, null, 0, "FOLDER_PENDING_CREATION")`
   - `Interlocked.Increment/Read` za thread-safe brojače
   - Progress callback sa TotalItems/ProcessedItems

5. **`appsettings.json`** — feature flag (trenutno `true` za testiranje):
   ```json
   "EnablePreviewFolderRollback": true
   ```

6. **`App.xaml.cs`** — registracija:
   ```csharp
   services.AddSingleton<IPreviewFolderRollbackService, PreviewFolderRollbackService>();
   ```

7. **`PreviewMigrationUC.xaml`** — dugme `BtnRollbackFaza3` (dark red, ispod Stop Faza 3):
   - Default `Visibility="Collapsed"`
   - Postaje vidljivo samo ako je `EnablePreviewFolderRollback: true`

8. **`PreviewMigrationUC.xaml.cs`**:
   - `_rollbackService` + `_ctsRollback` polja
   - Čita `IConfiguration["EnablePreviewFolderRollback"]` u konstruktoru
   - `BtnRollbackFaza3_Click` — confirm dialog → `RunAsync` → refresh stats + data
   - `SetButtonsRunning` uključuje rollback dugme

---

## Ključni detalji implementacije

### `PreviewFolderCreationService` — kako kreira foldere
- `DossierIdFormatter.ExtractPrefix(folderName)` za određivanje prefiksa
- Prefiks se direktno mapira na GUID parent foldera iz konfiguracije (`ResolveDossierParentId`):
  - `PI` → `Migration:RootPIFolderId`
  - `LE` → `Migration:RootLEFolderId`
  - `ACC` → `Migration:RootACCFolderId`
  - `D` → `Migration:RootDepoFolderId`
  - ostalo → `Migration:RootOtherFolderId`
- ID-evi su u formatu `workspace://SpacesStore/GUID` — `ExtractGuid()` uzima deo iza poslednjeg `/`
- `GetOrCreateDossierParentAsync` uklonjen — ID-evi su direktno u konfiguraciji
- `Parallel.ForEachAsync` sa MDP=5 za paralelno kreiranje foldera unutar batcha
- `Interlocked.Increment/Read` za thread-safe brojače
- Node type iz `Migration:FolderNodeTypeMapping` (npr. `ClientFL → ecm:ecmDossierPi`)
- Race condition handling: ako `CreateFolderAsync_v1` baci exception, proverava `GetFolderByNameAsync`

### `IAlfrescoWriteApi.DeleteNodeAsync`
```csharp
Task<bool> DeleteNodeAsync(string nodeId, CancellationToken ct = default); //TO DELETE
```
Napomena: metoda ima komentar `//TO DELETE` u interfejsu — to je originalni komentar koji označava da je planirana za uklanjanje iz interfejsa, ali se koristi za rollback. Ako se ukloni u budućnosti, treba implementirati novu DELETE metodu.

### UI pattern (WPF)
- Sve operacije: `Task.Run(() => service.RunAsync(ct, OnProgress), ct)`
- `Dispatcher.Invoke` za UI update unutar `OnProgress`
- `SetButtonsRunning(true/false)` blokira sva dugmad tokom operacije
- Scope pattern: `App.AppHost.Services.CreateAsyncScope()` + `IUnitOfWork.BeginAsync/CommitAsync/RollbackAsync`

### DI registracija pattern
- Servisi registrovani kao `AddSingleton` u `App.xaml.cs`
- Repozitorijumi kao `AddScoped`
- `IUnitOfWork` kao `AddScoped` (kreira novu konekciju po scope-u)

---

## Export servis — detalji

`PreviewExportService.ExportAsync(dossierType?, targetDossierType?, outputPath)`:
- Dohvata iz `GetForExportAsync` (bez filter po statusu — vraća sve)
- Sheet-ovi se kreiraju po `TargetDossierType` vrednosti (npr. "300", "400", "500", "700"); null vrednosti idu u sheet "Other"
- Headers: 50 kolona (sve iz `PreviewDocStaging` + `ParentFolderName`)
- Auto-fit kolone, freeze header red, auto-filter

### TargetDossierType mapping (UI → DB vrednost)
| UI prikaz | DB vrednost |
|-----------|-------------|
| PI        | 500         |
| LE        | 400         |
| ACC       | 300         |
| DE        | 700         |

Filteri za transfer/export (u UI):
- `CmbTransferDossierType`: (sve) / PI / LE — filtrira po izvoru (`DossierType`)
- `CmbTargetDossierType`: (sve) / PI / LE / ACC / DE — filtrira po destinaciji (`TargetDossierType`, numerički Tag)
- `TxtTransferDocumentType`: postoji u XAML-u ali je `Visibility="Collapsed"` — privremeno sklonjen
- Isti filteri koriste se i za Transfer (Faza 6) i za Export (Faza 7)

### Lanac filtera targetDossierType
`CmbTargetDossierType.Tag` (string) → `BtnExport_Click` / `BtnStartTransfer_Click` → `IPreviewExportService.ExportAsync` / `IPreviewToStagingTransferService.RunAsync` → `IPreviewDocStagingRepository.GetForExportAsync` / `GetForTransferAsync` → SQL `AND TargetDossierType = @TargetDossierType`

---

## Šta treba uraditi (potencijalni sledeći koraci)

1. **Commit** sve uncommitted izmene — standardni commit poruka format: `PreviewTypeMigration: <opis>`
2. **Testiranje rollbacka** — proveriti da `DeleteNodeAsync` radi ispravno za foldere kreirane u Fazi 3
3. **Postaviti `EnablePreviewFolderRollback: false`** nakon testiranja (pre produkcije)
4. **Faza 4/5?** — Nije implementirana; preskače se direktno na Fazu 6 (Transfer)
5. **`//TO DELETE` komentar na `DeleteNodeAsync`** — razjasniti sa timom da li se metoda zadržava

---

## Konfiguracija (appsettings.json — važni parametri)

```json
"Migration:RootDestinationFolderId": "e8e6fdcf-33ec-43c5-a6fd-cf33eca3c53e"
"Migration:RootPIFolderId": "workspace://SpacesStore/328e163f-..."
"Migration:RootLEFolderId": "workspace://SpacesStore/6dc01063-..."
"Migration:RootACCFolderId": "workspace://SpacesStore/875d1f6a-..."
"Migration:RootDepoFolderId": "workspace://SpacesStore/875d1f6a-..."   ← dodat u MigrationOptions
"Migration:RootOtherFolderId": "workspace://SpacesStore/875d1f6a-..."  ← dodat u MigrationOptions
"Migration:DocumentTypeDiscovery:FolderTypes": ["PI", "LE"]
"Migration:FolderNodeTypeMapping": { "ClientFL": "ecm:ecmDossierPi", ... }
"EnablePreviewFolderRollback": true   ← staviti false posle testiranja
```

---

## Build

```bash
dotnet build --no-restore
```
213 warnings (sve pre-existing, nema novih errora).  
Oracle.ManagedDataAccess warnings su očekivani.
