# PreviewTypeMigration — Dokumentacija v1

**Datum:** 2026-04-07  
**Verzija aplikacije:** 5.2.0  
**Autor:** vodarerp

---

## Pregled

PreviewTypeMigration je posebna migraciona putanja za dokumente iz PI (fizičke) i LE (pravne) dosijeima u Alfresciu.  
Koristi sopstvenu SQL Server staging tabelu (`PreviewDocStaging`) i sopstvene servise odvojene od ostalih migracionih putanja.

Cilj je:
1. Pročitati sve dokumente iz PI i LE foldera u izvornom Alfresciu
2. Pronaći ili kreirati odgovarajuće destination foldere u ciljnom Alfresciu
3. Preneti metapodatke u `DocStaging` tabelu (odakle ide dalje u postojeći tok migracije)
4. Eksportovati pregled u Excel za kontrolu kvaliteta

---

## Tabele u bazi

### `PreviewDocStaging` (SQL Server)
Centralna staging tabela. Svaki red = jedan dokument iz Alfresca.

| Kolona | Opis |
|--------|------|
| `Id` | PK, identity |
| `NodeId` | Alfresco node ID dokumenta (unique) |
| `Name` | Naziv fajla |
| `Status` | Trenutni status u pipeline-u (videti sekciju statusa) |
| `DossierType` | Tip izvornog dosijea: `PI` ili `LE` |
| `TargetDossierType` | Numerički tip destinacionog dosijea: `500`=PI, `400`=LE, `300`=ACC, `700`=DE |
| `DossierDestinationFolderName` | Naziv destinacionog foldera (npr. `PI-123456`) |
| `DossierDestinationFolderId` | Alfresco node ID destinacionog foldera (popunjava Faza 3) |
| `DossierDestinationFolderIsCreated` | `1` = folder postoji/kreiran, `0` = nije |
| `CoreId` | Identifikator klijenta |
| `DocDescription` | `ecm:docDesc` — opis dokumenta, koristi se za mapiranje tipa |
| `DocumentType` | Mapiran tip dokumenta |
| `DocumentTypeMigration` | Šifra dokumenta za migraciju |
| `ClientApi*` | Podaci sa ClientAPI (ime klijenta, tip, segment, JMBG...) |
| `RecordInserted` | Datum upisa |
| `Properties` | JSON svih Alfresco properties dokumenta |

### `PreviewLoadCheckpoint` (SQL Server)
Čuva napredak Faze 1 po tipu foldera (PI/LE), da bi se moglo nastaviti od mesta prekida.

| Kolona | Opis |
|--------|------|
| `FolderType` | `PI` ili `LE` |
| `FetchedCount` | Broj dokumenata fetchovanih sa Alfresca (skip vrednost za naredni run) |
| `UpdatedAt` | Datum poslednjeg upisa |

### `FolderStaging` (SQL Server)
Tabela za praćenje foldera u migraciji. Faza 3 upisuje folder rekorde ovde.

Ključne kolone: `NodeId`, `Name`, `Status`=`DONE`, `IsNewlyCreated` (1=kreiran migracijom, 0=postojao)

### `DocStaging` (SQL Server)
Destinaciona tabela za Fazu 6. Standardna staging tabela za ceo migracioni tok.

---

## Statusi u `PreviewDocStaging`

```
PENDING                  Faza 1 upisuje; Faza 2 čita
IN_PROGRESS              Privremeni lock (Faza 2 ili 3 atomično zauzimaju batch)
FOLDER_PENDING_EXISTS    Faza 2: folder pronađen u Alfresciu → Faza 3 samo potvrđuje
FOLDER_PENDING_CREATION  Faza 2: folder ne postoji → Faza 3 kreira
FOLDER_EXISTS            FINAL — Faza 3: folder potvrđen (nije trebalo kreirati)
FOLDER_CREATED           FINAL — Faza 3: folder kreiran
TRANSFERRED              Faza 6: prebačeno u DocStaging
```

**Dijagram toka:**
```
[Faza 1] ──────────────────────────────────────── PENDING
[Faza 2] PENDING → lock(IN_PROGRESS) ──────────── FOLDER_PENDING_EXISTS
                                      └──────────── FOLDER_PENDING_CREATION
[Faza 3] FOLDER_PENDING_EXISTS → lock ─────────── FOLDER_EXISTS  ──┐
         FOLDER_PENDING_CREATION → lock ───────── FOLDER_CREATED ──┤
[Faza 6] FOLDER_EXISTS | FOLDER_CREATED ──────────────────────────── TRANSFERRED
```

---

## Faza 1 — Učitavanje dokumenata (`PreviewLoadService`)

**UI dugme:** "Start Faza 1"  
**Servis:** `Migration.Infrastructure/Implementation/Services/PreviewLoadService.cs`  
**Interface:** `Migration.Abstraction/Interfaces/Wrappers/IPreviewLoadService.cs`

### Šta radi

1. Resetuje interno stanje (singleton servis, pa je reset obavezan na svakom startu)
2. Učitava checkpoint iz `PreviewLoadCheckpoint` (odakle je stao prethodni run)
3. Za svaki konfigurisani folder (PI i/ili LE — zavisno od filtera):
   - Izvršava AFTS search: `ANCESTOR:"<folderId>" AND TYPE:"cm:content"`
   - Opcionalno filtrira po datumu (`cm:created`, ako je `UseDateFilter=true`)
   - Paralelno obrađuje batcheve (`Parallel.ForEachAsync`, MDP iz konfiguracije)
   - Post-filter: zadržava samo dokumente čiji parent folder odgovara regex `^{type}[0-9]` (npr. `^PI[0-9]`)
   - Za svaki dokument: mapira Alfresco properties → `PreviewDocStaging` zapis
   - Batch upisuje u `PreviewDocStaging` via `InsertManyMergeAsync` (MERGE = ignorisanje duplikata po NodeId)
   - Periodično flushuje (svakih 5 batcheva) i na kraju
4. Čuva checkpoint posle svakog foldera

### Šta upisuje

**Tabela:** `PreviewDocStaging`  
**Status:** `PENDING`  
**Ključna polja koja se popunjavaju:**
- `NodeId`, `Name`, `NodeType`
- `DossierType` (PI ili LE)
- `DocDescription`, `OriginalDocumentCode`, `OldAlfrescoStatus`
- `DocumentType`, `DocumentTypeMigration`, `NewDocumentName` — via `IOpisToTipMapper`
- `TargetDossierType` — via `DestinationRootFolderDeterminator`
- `DossierDestinationFolderName` — via `DossierIdFormatter.ConvertForTargetType`
- `CoreId` — iz property `ecm:coreId` ili iz naziva parent foldera
- `ClientSegment`, `ProductType`, `ContractNumber`, `AccountNumbers`
- `IsActive`, `NewAlfrescoStatus`
- `Properties` (JSON svih Alfresco properties)

### UI opcije

| Opcija | Opis |
|--------|------|
| `CmbFaza1FolderFilter` | Izvor foldera: "Sve (PI + LE)" / "Samo PI" / "Samo LE" |

### Checkpoint / Resume

`PreviewLoadCheckpoint` tabela čuva `FetchedCount` po tipu foldera.  
Svaki start kreira **novi** run od početka (resetuje instance state), ali **koristi checkpoint** kao `skipCount` pri Alfresco search-u — tako nastavlja od mesta gde je stao.

---

## Faza 2 — Priprema foldera (`PreviewFolderPreparationService`)

**UI dugme:** "Start Faza 2"  
**Servis:** `Migration.Infrastructure/Implementation/Services/PreviewFolderPreparationService.cs`

### Šta radi

1. Atomično uzima batch distinktnih foldera sa statusom `PENDING` iz `PreviewDocStaging`
2. Za svaki folder:
   - Određuje `DOSSIERS-*` parent folder na osnovu prefiksa (`FL`→`DOSSIERS-FL`, `PL`→`DOSSIERS-PL`, itd.)
   - Keširа ID `DOSSIERS-*` foldera u `ConcurrentDictionary` (jednom po pokretanju)
   - Traži folder po imenu u Alfresciu (`GetFolderByNameAsync`)
   - Ako **postoji**: uzima NodeId, zove ClientAPI → piše `FOLDER_PENDING_EXISTS`
   - Ako **ne postoji**: zove ClientAPI za enrichment → piše `FOLDER_PENDING_CREATION`
3. Greška → reset na `PENDING`

### Šta čita / upisuje

**Čita iz:** `PreviewDocStaging` WHERE Status = `PENDING`  
**Upisuje u:** `PreviewDocStaging` via `UpdateFolderDataAndClientApiAsync`

| Scenario | Status | DossierDestinationFolderId | ClientApi* polja |
|----------|--------|---------------------------|-----------------|
| Folder pronađen | `FOLDER_PENDING_EXISTS` | popunjeno | prazno |
| Folder ne postoji | `FOLDER_PENDING_CREATION` | null | popunjeno |

**Konfiguracija:**
```json
"Migration:RootDestinationFolderId"  // Root folder ciljnog Alfresca
"Migration:PreviewFolderPreparation:BatchSize"         // default 200
"Migration:PreviewFolderPreparation:MaxDegreeOfParallelism"  // default 10
```

---

## Faza 3 — Kreiranje foldera (`PreviewFolderCreationService`)

**UI dugme:** "Start Faza 3"  
**Servis:** `Migration.Infrastructure/Implementation/Services/PreviewFolderCreationService.cs`

### Šta radi

1. Atomično uzima batch distinktnih foldera sa statusom `FOLDER_PENDING_CREATION` ili `FOLDER_PENDING_EXISTS` (CTE + UPDATE + OUTPUT, ROWLOCK/UPDLOCK/READPAST)
2. Za svaki folder paralelno (`Parallel.ForEachAsync`, MDP=5):
   a. Dohvata prvi zapis tog foldera iz `PreviewDocStaging` (reprezentativni zapis sa ClientAPI podacima)
   b. Ako `NeedsCreation=true` (bio `FOLDER_PENDING_CREATION`):
      - Određuje parent folder ID iz konfiguracije (`ResolveDossierParentId` po prefiksu)
      - Rekonstruiše `ClientData` iz staginga
      - Gradi properties za Alfresco (`BuildFolderProperties`)
      - Određuje `nodeType` iz `FolderNodeTypeMapping` po `TargetDossierType`
      - Kreira folder: `CreateFolderAsync_v1(parentId, folderName, properties, nodeType)`
      - Race condition handling: ako kreiranje baci exception, proverava da li folder već postoji
      - Piše `FOLDER_CREATED` + NodeId
   c. Ako `NeedsCreation=false` (bio `FOLDER_PENDING_EXISTS`):
      - Piše `FOLDER_EXISTS` + NodeId (već je bio u stagingu)
3. Kreira `FolderStaging` zapis i bulk-insertuje u `FolderStaging` tabelu
4. Greška → reset na originalni status (`FOLDER_PENDING_CREATION` ili `FOLDER_PENDING_EXISTS`)

### Šta čita / upisuje

**Čita iz:** `PreviewDocStaging` WHERE Status IN (`FOLDER_PENDING_CREATION`, `FOLDER_PENDING_EXISTS`)  
**Upisuje u:**
- `PreviewDocStaging` via `UpdateFolderDataAsync` → status `FOLDER_CREATED` ili `FOLDER_EXISTS`
- `FolderStaging` via `InsertManyIgnoreDuplicatesAsync` → status `DONE`

**Mapiranje prefiksa na parent folder ID:**
| Prefiks | Config ključ |
|---------|-------------|
| `PI` | `Migration:RootPIFolderId` |
| `LE` | `Migration:RootLEFolderId` |
| `ACC` | `Migration:RootACCFolderId` |
| `DE` | `Migration:RootDepoFolderId` |
| ostalo | `Migration:RootOtherFolderId` |

ID-evi su u formatu `workspace://SpacesStore/<GUID>` — servis uzima samo GUID deo.

**Konfiguracija:**
```json
"Migration:PreviewFolderCreation:BatchSize"                // default 50
"Migration:PreviewFolderCreation:MaxDegreeOfParallelism"   // default 5
"Migration:FolderNodeTypeMapping": { "ClientFL": "ecm:ecmDossierPi", ... }
```

---

## Faza 6 — Transfer u DocStaging (`PreviewToStagingTransferService`)

**UI dugme:** "Start Transfer (Faza 6)"  
**Servis:** `Migration.Infrastructure/Implementation/Services/PreviewToStagingTransferService.cs`

### Šta radi

1. Čita batcheve iz `PreviewDocStaging` sa statusom `FOLDER_EXISTS` ili `FOLDER_CREATED`
   - Opcionalni filter: `DossierType` (PI/LE) i `TargetDossierType` (numerički)
2. Paralelno obrađuje batcheve (MDP=6 default)
3. Za svaki zapis: mapira `PreviewDocStaging` → `DocStaging`
4. Bulk-insertuje u `DocStaging` (MERGE na NodeId — ignore duplicates)
5. Ažurira status u `PreviewDocStaging` → `TRANSFERRED`
6. Retry logika za deadlock-e (do 3 pokušaja)

### Šta čita / upisuje

**Čita iz:** `PreviewDocStaging` WHERE Status IN (`FOLDER_EXISTS`, `FOLDER_CREATED`)  
**Upisuje u:**
- `DocStaging` — novi zapis sa svim metapodacima
- `PreviewDocStaging` — status → `TRANSFERRED`

### UI filteri

| Filter | Vrednost (Tag) | Opis |
|--------|---------------|------|
| `CmbTransferDossierType` | `""` / `"PI"` / `"LE"` | Filter po tipu izvornog dosijea |
| `CmbTargetDossierType` | `""` / `"500"` / `"400"` / `"300"` / `"700"` | Filter po tipu destinacionog dosijea |

---

## Faza 7 — Excel Export (`PreviewExportService`)

**UI dugme:** "Export (Faza 7)"  
**Servis:** `Migration.Infrastructure/Implementation/Services/PreviewExportService.cs`

### Šta radi

1. Dohvata sve zapise iz `PreviewDocStaging` (opciono sa filterima po `DossierType` / `TargetDossierType`)
2. Kreira `.xlsx` fajl (via `MiniExcelLibs`)
3. Sheet-ovi se kreiraju po `TargetDossierType` vrednosti: `300`, `400`, `500`, `700`, `Other`
4. Max 500.000 redova po sheet-u

### Excel kolonе (ExportRow)

`Id`, `ClientId`, `DocumentName`, `OldDocumentType`, `MigrationDocumentType`, `MigrationDocumentName`,  
`OldStatus`, `MigrationStatus`, `ProductType`, `OldDossierName`, `MigrationFolderName`,  
`CategoryCode`, `CategoryName`, `Source`, `AccountNumbers`, `DocumentCreation`,  
`ClientApiMbrJmbg`, `ClientApiClientName`, `ClientApiClientType`, `ClientApiClientSubtype`,  
`ClientApiResidency`, `ClientApiStaff`, `OfficeId`, `BarClex`, `Contributor`

### UI filteri

Isti kao za Fazu 6: `CmbTransferDossierType` + `CmbTargetDossierType`

---

## Rollback Faze 3 (`PreviewFolderRollbackService`)

**Namena:** Samo za testiranje — brisanje foldera kreiranih u Fazi 3  
**UI dugme:** "Rollback Faza 3" (vidljivo samo ako `EnablePreviewFolderRollback: true`)  
**Feature flag:** `"EnablePreviewFolderRollback": true` u `appsettings.json`

### Šta radi

1. Dohvata sve foldere sa statusom `FOLDER_CREATED` iz `PreviewDocStaging`
2. Paralelno briše svaki folder u Alfresciu (`DeleteNodeAsync`)
3. Za svaki uspešno obrisani: resetuje status na `FOLDER_PENDING_CREATION`

> **UPOZORENJE:** Pre produkcije postaviti `EnablePreviewFolderRollback: false`

---

## Arhitektura — Dijagram projekata

```
Alfresco.Contracts
  └─ Oracle/Models/PreviewDocStaging.cs          ← model staging tabele
  └─ Options/MigrationOptions.cs                  ← konfiguracija

SqlServer.Abstraction
  └─ Interfaces/IPreviewDocStagingRepository.cs
  └─ Interfaces/IPreviewLoadCheckpointRepository.cs
  └─ Interfaces/IFolderStagingRepository.cs

SqlServer.Infrastructure
  └─ Implementation/PreviewDocStagingRepository.cs
  └─ Implementation/PreviewLoadCheckpointRepository.cs
  └─ Implementation/FolderStagingRepository.cs

Migration.Abstraction
  └─ Interfaces/Wrappers/
      ├─ IPreviewLoadService.cs
      ├─ IPreviewFolderPreparationService.cs
      ├─ IPreviewFolderCreationService.cs
      ├─ IPreviewToStagingTransferService.cs
      ├─ IPreviewExportService.cs
      └─ IPreviewFolderRollbackService.cs

Migration.Infrastructure
  └─ Implementation/Services/
      ├─ PreviewLoadService.cs
      ├─ PreviewFolderPreparationService.cs
      ├─ PreviewFolderCreationService.cs
      ├─ PreviewToStagingTransferService.cs
      ├─ PreviewExportService.cs
      └─ PreviewFolderRollbackService.cs

Alfresco.App
  └─ UserControls/PreviewMigrationUC.xaml(.cs)   ← WPF UI
  └─ App.xaml.cs                                  ← DI registracija
  └─ appsettings.json                             ← konfiguracija
```

---

## Konfiguracija (`appsettings.json`)

```json
{
  "Migration": {
    "RootDestinationFolderId": "<GUID ciljnog root foldera>",
    "RootPIFolderId":  "workspace://SpacesStore/<GUID>",
    "RootLEFolderId":  "workspace://SpacesStore/<GUID>",
    "RootACCFolderId": "workspace://SpacesStore/<GUID>",
    "RootDepoFolderId":  "workspace://SpacesStore/<GUID>",
    "RootOtherFolderId": "workspace://SpacesStore/<GUID>",
    "MaxDocumentsToProcess": 10000,

    "DocumentTypeDiscovery": {
      "BatchSize": 100,
      "MaxDegreeOfParallelism": 5,
      "UseDateFilter": false,
      "DateFrom": "2020-01-01",
      "DateTo": "2024-12-31"
    },

    "PreviewFolderPreparation": {
      "BatchSize": 200,
      "MaxDegreeOfParallelism": 10
    },

    "PreviewFolderCreation": {
      "BatchSize": 50,
      "MaxDegreeOfParallelism": 5
    },

    "PreviewToStagingTransfer": {
      "BatchSize": 500,
      "MaxDegreeOfParallelism": 6
    },

    "FolderNodeTypeMapping": {
      "ClientFL":  "ecm:ecmDossierPi",
      "ClientPL":  "ecm:ecmDossierLe",
      "Default":   "cm:folder"
    }
  },

  "EnablePreviewFolderRollback": false
}
```

---

## UI — Faze i kontrole

> **[SS — screenshot aplikacije sa PreviewMigrationUC tab-om treba dodati ovde]**

### Faza 1 kartica
| Kontrola | Opis |
|----------|------|
| `TxtFaza1Processed` | Broj učitanih dokumenata |
| `TxtFaza1Failed` | Broj grešaka |
| `CmbFaza1FolderFilter` | Izbor izvora: Sve / PI / LE |
| `BtnStartFaza1` | Pokretanje |
| `BtnStopFaza1` | Zaustavljanje |

### Faza 2 kartica
| Kontrola | Opis |
|----------|------|
| `TxtFaza2FolderProcessed` | Ukupno foldera provereno |
| `TxtFaza2FolderPending` | Foldera za kreiranje (`FOLDER_PENDING_CREATION`) |
| `BtnStartFaza2` | Pokretanje |
| `BtnStopFaza2` | Zaustavljanje |

### Faza 3 kartica
| Kontrola | Opis |
|----------|------|
| `TxtFaza3Total` | Ukupno foldera |
| `TxtFaza3Pending` | Na čekanju |
| `TxtFaza3Created` | Kreirano |
| `BtnStartFaza3` | Pokretanje |
| `BtnStopFaza3` | Zaustavljanje |
| `BtnRollbackFaza3` | Rollback (vidljivo samo ako EnablePreviewFolderRollback=true) |

> **[SS — screenshot statistika u donjoj traci treba dodati ovde]**

### Transfer + Export kartica
| Kontrola | Opis |
|----------|------|
| `CmbTransferDossierType` | Filter po izvoru: Sve / PI / LE |
| `CmbTargetDossierType` | Filter po destinaciji: Sve / PI / LE / ACC / DE |
| `BtnStartTransfer` | Pokretanje Faze 6 |
| `BtnExport` | Export u Excel (Faza 7) |

---

## Tipični redosled izvršavanja

```
1. Podesiti konfiguraciju u appsettings.json (Root folder ID-evi)
2. Pokrenuti Fazu 1 — čita dokumente iz Alfresca → PreviewDocStaging (status: PENDING)
3. Pokrenuti Fazu 2 — proverava destination foldere → statusi: FOLDER_PENDING_EXISTS / FOLDER_PENDING_CREATION
4. Pokrenuti Fazu 3 — kreira foldere → statusi: FOLDER_EXISTS / FOLDER_CREATED; upisuje FolderStaging
5. Opciono: eksportovati u Excel (Faza 7) za kontrolu pre transfera
6. Pokrenuti Fazu 6 (Transfer) → prebacuje u DocStaging (status: TRANSFERRED)
7. Dalje: standardni migracioni tok iz DocStaging
```

---

## Ključni detalji implementacije

### Atomično uzimanje batch-a (Faza 2 i 3)

SQL uzorak koji se koristi za atomično zauzimanje batch-a bez race conditiona:

```sql
WITH CTE AS (
    SELECT TOP (@BatchSize) *
    FROM PreviewDocStaging WITH (ROWLOCK, UPDLOCK, READPAST)
    WHERE Status = 'PENDING'  -- ili FOLDER_PENDING_*
)
UPDATE CTE SET Status = 'IN_PROGRESS'
OUTPUT INSERTED.*;
```

### Paralelno procesiranje

- `Parallel.ForEachAsync` sa `MaxDegreeOfParallelism` iz konfiguracije
- Svaki thread kreira sopstveni `IServiceScope` za `IUnitOfWork` i repozitorijum
- `Interlocked.Increment/Read` za thread-safe brojače
- `ConcurrentDictionary` za keširane vrednosti (folder ID-evi, checkpoint)

### DI registracija

Svi servisi su registrovani kao `Singleton` u `App.xaml.cs`.  
Repozitorijumi i `IUnitOfWork` su `Scoped` — kreiraju se unutar `CreateAsyncScope()`.

### Logovanje

Tri loggera:
- `FileLogger` — detaljan log u fajl
- `DbLogger` — greške u bazu (stack trace)
- `UiLogger` — kratke poruke vidljive u Live Log tab-u aplikacije
