# MoveService Refactoring + Folder Naming Fix - Summary

## Datum: 2025-01-21

## Ciljevi Refaktorisanja
1. **Simplifikacija `MoveService`**: Ukloni kompleksnu logiku za folder resolution i prebaci je na `FolderPreparationService` u FAZI 3
2. **Folder Naming Fix**: Osiguraj da svi dossier folderi imaju crticu između prefiksa i CoreId (npr. `PI-102206` umesto `PI102206`)

---

## Šta Je Urađeno

### 1. **Dodato novo polje u `DocStaging` model**
**Fajl**: `Alfresco.Contracts\Oracle\Models\DocStaging.cs`

```csharp
/// <summary>
/// ACTUAL Alfresco Folder UUID where document will be moved
/// Populated by FolderPreparationService in FAZA 3 after creating folder hierarchy
/// Used by MoveService in FAZA 4 for direct move operation (no folder resolution needed)
/// </summary>
public string? DestinationFolderId { get; set; }
```

**SQL Migration**: `SQL\09_Add_DestinationFolderId_To_DocStaging.sql`
- Dodaje kolonu `DestinationFolderId NVARCHAR(100) NULL`
- Kreira index za performanse

---

### 2. **Novi repository metod**
**Interface**: `SqlServer.Abstraction\Interfaces\IDocStagingRepository.cs`

```csharp
Task<int> UpdateDestinationFolderIdAsync(
    string dossierDestFolderId,
    string alfrescoFolderId,
    CancellationToken ct = default);
```

**Implementacija**: `SqlServer.Infrastructure\Implementation\DocStagingRepository.cs`
- Batch update svih dokumenata u folderu
- Idempotentno (ne update-uje ako već postoji vrednost)

---

### 3. **FolderPreparationService izmene**
**Fajl**: `Migration.Infrastructure\Implementation\Services\FolderPreparationService.cs`

**Dodato**:
- Dependency: `IServiceScopeFactory` za kreiranje scopova
- Nova metoda: `UpdateDocumentDestinationFolderIdAsync()`
  - Poziva se nakon kreiranja svakog foldera
  - Update-uje sve dokumente koji pripadaju tom folderu

**Proces**:
```
CreateFolderAsync()
  → Kreira folder hijerarhiju
  → Dobija finalFolderId
  → UpdateDocumentDestinationFolderIdAsync(dossierDestFolderId, finalFolderId)
     → Batch update DocStaging.DestinationFolderId za sve dokumente
```

---

### 4. **MoveService drastično pojednostavljen**
**Fajl**: `Migration.Infrastructure\Implementation\Services\MoveService.cs`

#### **Uklonjeno**:
- ❌ `IDocumentResolver _resolver` dependency
- ❌ `GetParentFolderName()` metoda
- ❌ `CreateOrGetDestinationFolder()` metoda
- ❌ `BuildDossierProperties()` metoda (već obsolete)

#### **Simplifikovan `MoveSingleDocumentAsync`**:

**STARI kod (kompleksan)**:
```csharp
// STEP 1: Create/Get destination folder (2 API poziva)
var destFolderId = await CreateOrGetDestinationFolder(doc, ct);
  → GetParentFolderName(doc.TargetDossierType)
  → _resolver.ResolveAsync(rootId, "DOSSIERS-PI", ct)
  → _resolver.ResolveAsync(parentId, "PI102206", ct)

// STEP 2: Move document
await _moveExecutor.MoveAsync(doc.NodeId, destFolderId, ct);

// STEP 3: Lookup mapping
var mapping = await mappingService.FindByOriginalNameAsync(...);

// STEP 4: Update properties
await _write.UpdateNodePropertiesAsync(...);
```

**NOVI kod (jednostavan)**:
```csharp
// VALIDATION: Ensure DestinationFolderId is set by FAZA 3
if (string.IsNullOrWhiteSpace(doc.DestinationFolderId))
    throw new InvalidOperationException("FAZA 3 must run first!");

// STEP 1: Move document (direktno, bez folder resolution!)
await _moveExecutor.MoveAsync(doc.NodeId, doc.DestinationFolderId, ct);

// STEP 2: Lookup mapping
var mapping = await mappingService.FindByOriginalNameAsync(...);

// STEP 3: Update properties
await _write.UpdateNodePropertiesAsync(...);
```

**Smanjenje sa 4 na 3 koraka** + **eliminisano 2 API poziva** po dokumentu!

---

### 5. **DocumentResolver refaktorisan**
**Fajl**: `Migration.Infrastructure\Implementation\Document\DocumentResolver.cs`

**Dodato**: Novi parametar `createIfMissing`

```csharp
public async Task<string> ResolveAsync(
    string destinationRootId,
    string newFolderName,
    Dictionary<string, object>? properties,
    bool createIfMissing,  // ← NOVO
    CancellationToken ct)
{
    // ... postojeća logika ...

    if (!string.IsNullOrEmpty(folderId))
        return folderId; // Folder postoji

    // NOVO: Provera da li treba kreirati
    if (!createIfMissing)
    {
        throw new InvalidOperationException(
            $"Folder '{newFolderName}' not found and createIfMissing=false. " +
            $"Should have been created by FolderPreparationService.");
    }

    // Kreiraj folder...
}
```

**Backward compatibility**: Stari overload-i pozivaju novi sa `createIfMissing: true`

---

## Prednosti Refaktorisanja

### ✅ **Performanse**
- **-2 API poziva** po dokumentu u MoveService
  - Staro: `GetFolderByRelative("DOSSIERS-PI")` + `GetFolderByRelative("PI102206")`
  - Novo: Već imamo folder ID u `doc.DestinationFolderId`
- **Batch update** umesto individualnih update-a
- FolderPreparationService kreira sve foldere paralelno (50 tasks)

### ✅ **Jednostavnost**
- MoveService: **-150 linija koda**
- Jasna separation of concerns:
  - **FAZA 3**: Kreira foldere + popunjava `DestinationFolderId`
  - **FAZA 4**: Samo move + update properties
- Nema više kompleksnog mapiranja `TargetDossierType` → folder name

### ✅ **Sigurnost**
- **Fail-fast**: Ako FAZA 3 nije završena → instant exception sa jasnom porukom
- Nema "slučajnog kreiranja" foldera sa nedostajućim properties
- Validation: `DestinationFolderId IS NULL` → exception

### ✅ **Održivost**
- Svaki servis ima **jednu odgovornost**
- Lakše za testiranje (manje dependencies)
- Eksplicitna kontrola kreiranja foldera (`createIfMissing` parametar)

---

## Migration Pipeline (PRE i POSLE)

### **STARI Pipeline**
```
FAZA 1: FolderDiscovery       → Skenira dosijee
FAZA 2: DocumentDiscovery     → Priprema metadata
FAZA 3: FolderPreparation     → Kreira foldere
FAZA 4: Move                  → PONOVO proverava/kreira foldere + move dokumenta ❌
```

### **NOVI Pipeline (Refaktorisan)**
```
FAZA 1: FolderDiscovery       → Skenira dosijee
FAZA 2: DocumentDiscovery     → Priprema metadata
FAZA 3: FolderPreparation     → Kreira foldere + popunjava DestinationFolderId ✅
FAZA 4: Move                  → Samo move + update properties (brzo!) ✅
```

---

## Primer Izvršavanja

### **FAZA 3: FolderPreparationService**
```
Input: DocStaging sa dokumentima koji trebaju foldere

Step 1: GetUniqueDestinationFoldersAsync()
  → Distinct (TargetDossierType, DossierDestFolderId) iz DocStaging
  → Npr: [(500, "PI102206"), (400, "LE500342"), ...]

Step 2: Paralelno (50 tasks) za svaki unique folder:
  a) CreateFolderAsync("PI102206")
     → Kreira "DOSSIERS-PI" (cache hit posle prvog puta)
     → Kreira "PI102206" pod "DOSSIERS-PI"
     → Dobija finalFolderId = "abc-123-def-456"

  b) UpdateDocumentDestinationFolderIdAsync("PI102206", "abc-123-def-456")
     → UPDATE DocStaging
        SET DestinationFolderId = 'abc-123-def-456'
        WHERE DossierDestFolderId = 'PI102206'
     → Update-ovano 150 dokumenata u folderu "PI102206"

Output: Svi dokumenti imaju popunjeno DestinationFolderId polje
```

### **FAZA 4: MoveService**
```
Input: DocStaging sa DestinationFolderId != NULL

Step 1: Preuzmi batch od 1000 dokumenata
  → SELECT * FROM DocStaging WHERE Status = 'READY' AND DestinationFolderId IS NOT NULL

Step 2: Za svaki dokument (paralelno, DOP=30):
  a) Validation:
     if (doc.DestinationFolderId == null) → throw Exception

  b) Move:
     await _moveExecutor.MoveAsync(doc.NodeId, doc.DestinationFolderId)
     → Direktan API poziv, folder ID već poznat!

  c) Update properties:
     await _write.UpdateNodePropertiesAsync(doc.NodeId, {...})

Step 3: Batch update statusa
  → Mark 1000 documents as DONE

Output: 1000 dokumenata uspešno pomereno
```

---

## Breaking Changes

### ⚠️ **Dependency Injection Changes**

**FolderPreparationService** konstruktor:
```diff
  public FolderPreparationService(
      IDocStagingRepository docRepo,
      IDocumentResolver documentResolver,
      IPhaseCheckpointRepository phaseCheckpointRepo,
      IUnitOfWork uow,
+     IServiceScopeFactory scopeFactory,  // ← DODATO
      ILogger<FolderPreparationService> logger,
      IOptions<MigrationOptions> migrationOptions)
```

**MoveService** konstruktor:
```diff
  public MoveService(
      IMoveReader moveService,
      IMoveExecutor moveExecutor,
      IDocStagingRepository docRepo,
-     IDocumentResolver resolver,  // ← UKLONJENO
      IAlfrescoWriteApi write,
      IAlfrescoReadApi read,
      IOptions<MigrationOptions> options,
      IServiceScopeFactory scopeFactory,
      ILoggerFactory logger)
```

**Rešenje**: DI container automatski resolve-uje dependencies (već registrovano)

---

## Testing Checklist

### Pre Production Deploy:
- [ ] Pokrenuti SQL migration script: `09_Add_DestinationFolderId_To_DocStaging.sql`
- [ ] Verifikovati da kolona postoji: `SELECT TOP 1 * FROM DocStaging`
- [ ] Testirati FAZU 3 na malom skupu (~100 dokumenata)
- [ ] Verifikovati da je `DestinationFolderId` popunjen
- [ ] Testirati FAZU 4 sa popunjenim `DestinationFolderId`
- [ ] Testirati fail-fast: FAZA 4 bez FAZE 3 → trebao bi baciti exception
- [ ] Performance test: meriti vreme FAZE 4 (očekujemo 20-30% ubrzanje)

---

## Rollback Plan

Ako nešto ne radi:
1. **Git revert** ovog commit-a
2. **Drop column** iz SQL-a:
   ```sql
   ALTER TABLE DocStaging DROP COLUMN DestinationFolderId;
   ```
3. **Rebuild** projekta

---

## Files Changed

### Dodato:
- `SQL\09_Add_DestinationFolderId_To_DocStaging.sql` (novi SQL script)
- `FOLDER_NAMING_FIX_SUMMARY.md` (detaljni dokument za folder naming fix)

### Izmenjeno:
- `Alfresco.Contracts\Oracle\Models\DocStaging.cs` (+7 linija)
- `SqlServer.Abstraction\Interfaces\IDocStagingRepository.cs` (+11 linija)
- `SqlServer.Infrastructure\Implementation\DocStagingRepository.cs` (+23 linija)
- `Migration.Abstraction\Interfaces\IDocumentResolver.cs` (+21 linija)
- `Migration.Infrastructure\Implementation\Document\DocumentResolver.cs` (+38 linija)
- `Migration.Infrastructure\Implementation\Services\FolderPreparationService.cs` (+48 linija)
- `Migration.Infrastructure\Implementation\Services\MoveService.cs` (-156 linija, +15 linija)
- `Alfresco.Contracts\Mapper\DossierIdFormatter.cs` (+52 linija, izmena logike)

**Ukupno**: +205 linija dodato, -156 linija uklonjeno = **+49 linija neto** (ali mnogo kvalitetnijeg i ispravnijeg koda!)

---

## Zaključak

Refaktorisanje je **uspešno**:
- ✅ **MoveService simplifikovan**: -150 linija koda, jasna separation of concerns
- ✅ **Performanse bolje**: Eliminisano 2 API poziva po dokumentu
- ✅ **Folder naming fix**: Svi dossier folderi sada imaju crticu (PI-102206, ACC-500342, DE-500342-00008_12345)
- ✅ **Backward compatibility**: Podržava i stari format (bez crtice) i novi format (sa crticom)
- ✅ **Arhitektura čistija**: FolderPreparationService kreira foldere + popunjava IDs, MoveService samo move
- ✅ **Sigurnost poboljšana**: Fail-fast validation ako FAZA 3 nije završena
- ✅ **Build uspešan**: Nema compilation errora

**Preporuka**: Deploy na TEST okruženju prvo, pa nakon verifikacije na PRODUCTION.

**Detaljnija Dokumentacija**:
- MoveService refactoring: Ovaj dokument
- Folder naming fix: `FOLDER_NAMING_FIX_SUMMARY.md`
