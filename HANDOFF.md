# HANDOFF — PreviewTypeMigration

**Datum:** 2026-04-02  
**Poslednji commit:** `941c739 PreviewTypeMigration: Excel Export`  
**Trenutna grana:** `master`

---

## Šta je PreviewTypeMigration

Zasebna migraciona putanja za "Preview" dokumente (PI i LE dosijei).  
Koristi svoju tabelu `PreviewDocStaging` (SQL Server) i ima sopstvene faze:

| Faza | Servis | Opis |
|------|--------|------|
| 1 | `PreviewLoadService` | Čita dokumente iz Alfresca (PI/LE folderi), upisuje u `PreviewDocStaging` sa statusom `PENDING` |
| 2 | `PreviewFolderPreparationService` | Proverava da li destination folder postoji na Alfresciu; zove ClientAPI za enrichment; status → `FOLDER_EXISTS` ili `FOLDER_PENDING_CREATION` |
| 3 | `PreviewFolderCreationService` | Kreira Alfresco foldere za `FOLDER_PENDING_CREATION` zapise; status → `FOLDER_CREATED` |
| 6 | `PreviewToStagingTransferService` | Prenosi `FOLDER_EXISTS`/`FOLDER_CREATED` zapise u `DocStaging`; status → `TRANSFERRED` |
| 7 | `PreviewExportService` | Eksportuje `PreviewDocStaging` u `.xlsx` (dva sheet-a: PI i LE) |
| R | `PreviewFolderRollbackService` | **NOVO (TEST)** — briše kreirane Alfresco foldere i resetuje status na `FOLDER_PENDING_CREATION` |

---

## Status tabele `PreviewDocStaging`

```
PENDING                  → početni status nakon Faze 1
IN_PROGRESS              → privremeni (atomično zauzimanje batch-a)
FOLDER_EXISTS            → folder već postoji na Alfresciu (Faza 2)
FOLDER_PENDING_CREATION  → folder ne postoji, ClientAPI enrichment obavljen (Faza 2)
FOLDER_CREATED           → folder kreiran u Fazi 3
TRANSFERRED              → prebačen u DocStaging (Faza 6)
```

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

## Izmene u ovoj sesiji (UNCOMMITTED)

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

`PreviewExportService.ExportAsync(dossierType?, documentType?, outputPath)`:
- Dohvata iz `GetForExportAsync` (bez filter po statusu — vraća sve)
- Deli na PI i LE sheet-ove u ClosedXML workbook-u
- Headers: 50 kolona (sve iz `PreviewDocStaging` + `ParentFolderName`)
- Auto-fit kolone, freeze header red, auto-filter

Filteri za transfer/export (u UI):
- `CmbTransferDossierType`: (sve) / PI / LE
- `TxtTransferDocumentType`: slobodan unos, prazno = sve
- Isti kontroli koriste se i za Transfer (Faza 6) i za Export (Faza 7)

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
