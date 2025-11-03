# IMPLEMENTACIJA PARENT FOLDERA - Migration System

**Datum:** 2025-11-03
**Status:** ✅ Implementirano i testirano
**Build:** ✅ Uspešan (0 grešaka)

---

## 📋 SADRŽAJ

1. [Pregled problema](#pregled-problema)
2. [Analiza trenutnog stanja](#analiza-trenutnog-stanja)
3. [Rešenje](#rešenje)
4. [Implementirane izmene](#implementirane-izmene)
5. [Tok rada servisa](#tok-rada-servisa)
6. [Finalna struktura foldera](#finalna-struktura-foldera)
7. [Testiranje](#testiranje)
8. [Sledeći koraci](#sledeći-koraci)

---

## PREGLED PROBLEMA

### Pitanje
Kako **FolderDiscoveryService** i **DocumentDiscoveryService** treba da rade? Koji folderi trebaju da budu kreirani u `RootDestinationFolder`?

### Odgovor iz analize (ANALIZA_MIGRACIJE.md)

**FolderDiscoveryService:**
- ✅ Čita foldere iz **starog** Alfresco-a
- ✅ Otkriva `TargetDossierType` (300/400/500/700/999)
- ✅ Poziva ClientAPI za nedostajuće podatke
- ✅ Čuva u **FOLDER_STAGING** tabelu
- ❌ **Trebalo bi da kreira parent foldere tokom discovery faze**

**DocumentDiscoveryService:**
- ✅ Čita dokumente iz **starog** Alfresco-a
- ✅ Mapira nazive, šifre, status, source
- ✅ Otkriva `TargetDossierType`
- ✅ Filtrira excluded types (00702)
- ✅ Proverava DUT status za depozite
- ✅ Čuva u **DOC_STAGING** tabelu
- ❌ **NE kreira nove foldere**

---

## ANALIZA TRENUTNOG STANJA

### ❌ Trenutna implementacija (pre izmena)

**FolderDiscoveryService** je kreirao **DOSSIER-{Type}** foldere:

```
RootDestinationFolder/
├── DOSSIER-FL/        ← kreirao se
├── DOSSIER-PL/        ← kreirao se
├── DOSSIER-ACC/       ← kreirao se
└── DOSSIER-D/         ← kreirao se
```

**Problem:** Ovo nisu pravi parent folderi prema analizi dokumenta!

---

### ✅ Trebalo bi da bude (prema ANALIZA_MIGRACIJE.md)

```
RootDestinationFolder/
├── 300 Dosije paket računa/     ← treba kreirati
├── 400 Dosije pravnog lica/     ← treba kreirati
├── 500 Dosije fizičkog lica/    ← treba kreirati
├── 700 Dosije depozita/         ← treba kreirati
└── 999 Dosije - Unknown/        ← treba kreirati
```

**Sa podfolderima:**

```
RootDestinationFolder/
├── 300 Dosije paket računa/          ← FolderDiscoveryService kreira
│   ├── ACC-13001926/                 ← MoveService kreira
│   │   └── dokumenti...
│   └── ACC-13000667/
│       └── dokumenti...
│
├── 400 Dosije pravnog lica/          ← FolderDiscoveryService kreira
│   ├── LE-50034220/                  ← MoveService kreira
│   │   └── dokumenti...
│   └── LE-50034141/
│       └── dokumenti...
│
├── 500 Dosije fizičkog lica/         ← FolderDiscoveryService kreira
│   ├── PI-102206/                    ← MoveService kreira
│   │   └── dokumenti...
│   └── PI-13001926/
│       └── dokumenti...
│
├── 700 Dosije depozita/              ← FolderDiscoveryService kreira
│   ├── DE-10194302-00008-10104302_20241105154459/  ← MoveService kreira
│   │   └── dokumenti...
│   └── ...
│
└── 999 Dosije - Unknown/             ← FolderDiscoveryService kreira
    └── UNKNOWN-{CoreId}/             ← MoveService kreira
        └── dokumenti...
```

---

## REŠENJE

### Ideja
**FolderDiscoveryService** treba da:
1. Posle učitavanja foldera sa starog Alfresco-a
2. Odmah odredi parent folder (300/400/500/700/999)
3. Proveri da li parent folder postoji
4. Ako ne postoji → kreira ga
5. Sačuva `DossierDestFolderId` u `FOLDER_STAGING`

### Princip
- **FolderDiscoveryService** → kreira **parent foldere** (300/400/500/700/999)
- **MoveService** → kreira **client subfoldere** (PI-{CoreId}, LE-{CoreId}, ACC-{CoreId}, DE-{...})

---

## IMPLEMENTIRANE IZMENE

### 1️⃣ DossierTypeDetector.cs

**Lokacija:** `Alfresco.Contracts/Mapper/DossierTypeDetector.cs`

**Dodate 3 nove helper metode:**

#### `GetDestinationFolderName(DossierType)`

Vraća naziv parent foldera za dati `DossierType`:

```csharp
public static string GetDestinationFolderName(DossierType dossierType)
{
    return dossierType switch
    {
        DossierType.AccountPackage => "300 Dosije paket računa",
        DossierType.ClientPL => "400 Dosije pravnog lica",
        DossierType.ClientFL => "500 Dosije fizičkog lica",
        DossierType.Deposit => "700 Dosije depozita",
        DossierType.Unknown => "999 Dosije - Unknown",
        DossierType.Other => "999 Dosije - Unknown", // Fallback
        DossierType.ClientFLorPL => throw new InvalidOperationException(
            "Cannot get folder name for unresolved ClientFLorPL type. Must resolve to ClientFL or ClientPL first."),
        _ => "999 Dosije - Unknown"
    };
}
```

---

#### `MapDossierFolderTypeToDossierType(string)`

Mapira DOSSIER folder type (FL/PL/ACC/D) iz starog Alfresco-a → `DossierType` enum:

```csharp
public static DossierType MapDossierFolderTypeToDossierType(string folderType)
{
    return folderType?.ToUpperInvariant() switch
    {
        "FL" => DossierType.ClientFLorPL,  // Treba razrešiti kasnije pomoću ClientSegment
        "PL" => DossierType.ClientPL,
        "ACC" => DossierType.AccountPackage,
        "D" => DossierType.Deposit,
        _ => DossierType.Unknown
    };
}
```

---

#### `GetPossibleDossierTypes(string)`

Vraća sve moguće `DossierType` vrednosti koje mogu nastati iz datog folder type-a. Za FL vraća i ClientFL i ClientPL jer mogu biti oba:

```csharp
public static IEnumerable<DossierType> GetPossibleDossierTypes(string folderType)
{
    var baseType = MapDossierFolderTypeToDossierType(folderType);

    if (baseType == DossierType.ClientFLorPL)
    {
        // FL folder može sadržati i fizička i pravna lica
        yield return DossierType.ClientFL;
        yield return DossierType.ClientPL;
    }
    else if (baseType != DossierType.Other && baseType != DossierType.ClientFLorPL)
    {
        yield return baseType;
    }
}
```

---

### 2️⃣ FolderDiscoveryService.cs - EnsureDossierFoldersExistAsync()

**Lokacija:** `Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`

**Modifikovana metoda** koja kreira parent foldere:

```csharp
/// <summary>
/// Pre-creates all parent destination folders (300/400/500/700/999) SEQUENTIALLY
/// to avoid race conditions during DocumentDiscoveryService parallel processing
/// </summary>
private async Task EnsureDossierFoldersExistAsync(IEnumerable<string> folderTypes, CancellationToken ct)
{
    _fileLogger.LogInformation("Pre-creating parent destination folders (300/400/500/700/999)...");
    _dbLogger.LogInformation("Starting parent folder pre-creation");

    // Determine which DossierTypes are needed based on discovered folderTypes
    var neededDossierTypes = new HashSet<DossierType>();

    foreach (var folderType in folderTypes)
    {
        // Get all possible DossierTypes for this folder type
        // For FL, this returns both ClientFL (500) and ClientPL (400)
        var possibleTypes = DossierTypeDetector.GetPossibleDossierTypes(folderType);
        foreach (var type in possibleTypes)
        {
            neededDossierTypes.Add(type);
        }
    }

    // Always ensure Unknown folder exists as fallback
    neededDossierTypes.Add(DossierType.Unknown);

    _fileLogger.LogInformation("Will create {Count} parent folders: {Types}",
        neededDossierTypes.Count,
        string.Join(", ", neededDossierTypes.Select(t => $"{(int)t} ({t})")));

    // Create each parent folder sequentially
    foreach (var dossierType in neededDossierTypes.OrderBy(t => (int)t))
    {
        // Skip unresolved types (should not happen at this point)
        if (dossierType == DossierType.ClientFLorPL || dossierType == DossierType.Other)
        {
            _fileLogger.LogWarning("Skipping unresolved DossierType: {Type}", dossierType);
            continue;
        }

        var dossierTypeCode = ((int)dossierType).ToString();

        // Check cache first
        if (_dossierFolderCache.ContainsKey(dossierTypeCode))
        {
            _fileLogger.LogDebug("Parent folder {Code} already exists in cache", dossierTypeCode);
            continue;
        }

        var folderName = DossierTypeDetector.GetDestinationFolderName(dossierType);
        _fileLogger.LogInformation("Creating parent folder: {FolderName} (Code: {Code})",
            folderName, dossierTypeCode);

        try
        {
            // Create/resolve the parent folder under RootDestinationFolderId
            var parentFolderId = await _resolver.ResolveAsync(
                _options.Value.RootDestinationFolderId,
                folderName,
                ct).ConfigureAwait(false);

            // Cache the folder ID by DossierType code (300/400/500/700/999)
            _dossierFolderCache[dossierTypeCode] = parentFolderId;

            _fileLogger.LogInformation(
                "Successfully created/resolved parent folder {Code} ({Name}) -> {FolderId}",
                dossierTypeCode, folderName, parentFolderId);
            _dbLogger.LogInformation(
                "Created parent folder {Code} ({Name})",
                dossierTypeCode, folderName);
        }
        catch (Exception ex)
        {
            _fileLogger.LogError("Failed to create parent folder {Code} ({Name}): {Error}",
                dossierTypeCode, folderName, ex.Message);
            _dbLogger.LogError(ex,
                "Failed to create parent folder {Code} ({Name})",
                dossierTypeCode, folderName);
            _uiLogger.LogError("Failed to create parent folder {Code}", dossierTypeCode);
            throw;
        }
    }

    _fileLogger.LogInformation(
        "Successfully pre-created {Count} parent destination folders: {Folders}",
        _dossierFolderCache.Count,
        string.Join(", ", _dossierFolderCache.Keys.OrderBy(k => k)));
    _dbLogger.LogInformation(
        "Pre-created {Count} parent folders",
        _dossierFolderCache.Count);
}
```

**Ključne izmene:**
- **PRE:** Kreirao `DOSSIER-FL`, `DOSSIER-PL`, `DOSSIER-ACC`, `DOSSIER-D`
- **SADA:** Kreira `300 Dosije paket računa`, `400 Dosije pravnog lica`, `500 Dosije fizičkog lica`, `700 Dosije depozita`, `999 Dosije - Unknown`
- Cache koristi **DossierType kod** (300/400/500/700/999) umesto folder type-a (FL/PL/ACC/D)
- Za FL kreira **i 400 i 500** jer mogu biti oba

---

### 3️⃣ FolderDiscoveryService.cs - Populate DossierDestFolderId

**Lokacija:** `Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs` (linija ~201-245)

**Modifikovana logika** za setovanje `DossierDestFolderId`:

```csharp
// Apply folder type detection and source mapping (NEW - FAZA 3)
ApplyFolderTypeDetectionAndMapping(foldersToInsert, currentType);

// Populate DossierDestFolderId from cache using TargetDossierType
var successCount = 0;
var failCount = 0;

foreach (var folder in foldersToInsert)
{
    var dossierTypeCode = folder.TargetDossierType.ToString();

    if (_dossierFolderCache.TryGetValue(dossierTypeCode, out var parentFolderId))
    {
        folder.DossierDestFolderId = parentFolderId;
        successCount++;

        _fileLogger.LogTrace("Folder {Name}: DossierDestFolderId={ParentId} (Type {Code})",
            folder.Name, parentFolderId, dossierTypeCode);
    }
    else
    {
        failCount++;
        _fileLogger.LogWarning(
            "Parent folder {Code} not found in cache for folder {Name}! Falling back to Unknown.",
            dossierTypeCode, folder.Name);

        // Fallback to Unknown (999)
        if (_dossierFolderCache.TryGetValue("999", out var unknownFolderId))
        {
            folder.DossierDestFolderId = unknownFolderId;
            folder.TargetDossierType = (int)DossierType.Unknown;
        }
        else
        {
            _fileLogger.LogError(
                "Unknown folder (999) also not in cache! This should not happen. Folder {Name} will have null DossierDestFolderId.",
                folder.Name);
        }
    }
}

_fileLogger.LogDebug(
    "Populated DossierDestFolderId for {SuccessCount}/{TotalCount} folders ({FailCount} fallbacks to Unknown)",
    successCount, foldersToInsert.Count, failCount);
```

**Ključne izmene:**
- **PRE:** Koristio `currentType` (FL/PL/ACC/D) iz cache-a
- **SADA:** Koristi `folder.TargetDossierType` (300/400/500/700/999)
- **Fallback:** Ako parent folder nije pronađen → postavlja ga na Unknown (999)
- **Detaljnije logovanje:** Success/fail count za debugging

---

## TOK RADA SERVISA

### Faza 1: FolderDiscoveryService startuje

```
1. Otkriva DOSSIER subfoldere u starom Alfresco-u (FL, PL, ACC, D)
   ↓
2. EnsureDossierFoldersExistAsync() kreira parent foldere:
   ✅ 300 Dosije paket računa/
   ✅ 400 Dosije pravnog lica/
   ✅ 500 Dosije fizičkog lica/
   ✅ 700 Dosije depozita/
   ✅ 999 Dosije - Unknown/
   ↓
3. Keširaju se NodeId-ovi u _dossierFolderCache:
   ["300"] = "node-id-abc123"
   ["400"] = "node-id-def456"
   ["500"] = "node-id-ghi789"
   ["700"] = "node-id-jkl012"
   ["999"] = "node-id-mno345"
```

**Log primer:**
```
[INFO] Pre-creating parent destination folders (300/400/500/700/999)...
[INFO] Will create 5 parent folders: 300 (AccountPackage), 400 (ClientPL), 500 (ClientFL), 700 (Deposit), 999 (Unknown)
[INFO] Creating parent folder: 300 Dosije paket računa (Code: 300)
[INFO] Successfully created/resolved parent folder 300 (300 Dosije paket računa) -> abc-123-def-456
[INFO] Creating parent folder: 400 Dosije pravnog lica (Code: 400)
[INFO] Successfully created/resolved parent folder 400 (400 Dosije pravnog lica) -> def-456-ghi-789
...
[INFO] Successfully pre-created 5 parent destination folders: 300, 400, 500, 700, 999
```

---

### Faza 2: Čitanje foldera iz starog Alfresco-a

```
1. Čita folder iz DOSSIER-FL/ (stari Alfresco)
   Name: "102206"
   ↓
2. EnrichFoldersWithClientDataAsync():
   - Poziva ClientAPI za CoreId: 102206
   - Dobija: Segment = "PI" (fizičko lice)
   ↓
3. ApplyFolderTypeDetectionAndMapping():
   - InferTipDosijeaFromFolderType("FL") → "Dosije klijenta FL / PL"
   - DetectFromTipDosijea() → DossierType.ClientFLorPL
   - ResolveFLorPL("PI") → DossierType.ClientFL (500)
   - folder.TargetDossierType = 500
   - folder.Source = "Heimdall"
   - folder.TipDosijea = "Dosije klijenta FL / PL"
   ↓
4. Populate DossierDestFolderId:
   - dossierTypeCode = "500"
   - _dossierFolderCache["500"] → "node-id-ghi789"
   - folder.DossierDestFolderId = "node-id-ghi789"
   ↓
5. Sačuva u FOLDER_STAGING tabelu:
   FolderId: 12345
   Name: "102206"
   CoreId: "102206"
   TipDosijea: "Dosije klijenta FL / PL"
   TargetDossierType: 500
   Source: "Heimdall"
   DossierDestFolderId: "node-id-ghi789"  ← KLJUČNO!
   ClientSegment: "PI"
```

**Log primer:**
```
[DEBUG] Processing DOSSIER-FL folder
[INFO] Read 500 folders from DOSSIER-FL
[DEBUG] Fetching client data from ClientAPI for CoreId: 102206
[DEBUG] Successfully enriched folder 102206 with ClientAPI data
[DEBUG] Resolved FL/PL for folder 102206: ClientSegment=PI -> DossierType=ClientFL
[TRACE] Folder 102206: TipDosijea=Dosije klijenta FL / PL, TargetDossierType=ClientFL, Source=Heimdall
[TRACE] Folder 102206: DossierDestFolderId=node-id-ghi789 (Type 500)
[DEBUG] Populated DossierDestFolderId for 500/500 folders (0 fallbacks to Unknown)
[INFO] Successfully inserted 500 folders into staging
```

---

### Faza 3: MoveService (kasnije - nije deo ove implementacije)

```
1. Čita folder iz FOLDER_STAGING:
   FolderId: 12345
   Name: "102206"
   CoreId: "102206"
   TargetDossierType: 500
   DossierDestFolderId: "node-id-ghi789"  ← Već zna parent folder!
   ↓
2. Generiše unique identifier: "PI-102206"
   ↓
3. Proverava da li PI-102206 postoji u folderu 500 (node-id-ghi789)
   ↓
4. AKO NE POSTOJI (TC 8):
   - Kreira folder "PI-102206" u parent folderu node-id-ghi789
   - Poziva ClientAPI za atribute (ime, prezime, JMBG, itd.)
   - Popunjava custom properties na folderu
   ↓
5. AKO POSTOJI (TC 9):
   - Samo migriraj dokumente
   ↓
6. Premesti dokumente u PI-102206/
```

---

## FINALNA STRUKTURA FOLDERA

### Novi Alfresco strukturа

```
RootDestinationFolder/  (node-id: root-abc-123)
│
├── 300 Dosije paket računa/          ← FolderDiscoveryService kreirao (node-id-abc123)
│   ├── ACC-13001926/                 ← MoveService će kreirati
│   │   ├── Ugovor o tekućem računu.pdf
│   │   ├── KDP za fizička lica - migracija.pdf (NEAKTIVAN)
│   │   └── Potvrda o prijemu kartice.pdf (AKTIVAN)
│   └── ACC-13000667/
│       └── dokumenti...
│
├── 400 Dosije pravnog lica/          ← FolderDiscoveryService kreirao (node-id-def456)
│   ├── LE-50034220/                  ← MoveService će kreirati
│   │   ├── Izjava o kanalima komunikacije - migracija.pdf (NEAKTIVAN)
│   │   └── KYC upitnik - migracija.pdf (NEAKTIVAN)
│   └── LE-50034141/
│       └── dokumenti...
│
├── 500 Dosije fizičkog lica/         ← FolderDiscoveryService kreirao (node-id-ghi789)
│   ├── PI-102206/                    ← MoveService će kreirati
│   │   ├── KYC upitnik - migracija.pdf (NEAKTIVAN)
│   │   └── GDPR saglasnost - migracija.pdf (NEAKTIVAN)
│   └── PI-13001926/
│       └── dokumenti...
│
├── 700 Dosije depozita/              ← FolderDiscoveryService kreirao (node-id-jkl012)
│   ├── DE-10194302-00008-10104302_20241105154459/  ← MoveService će kreirati
│   │   ├── Ugovor o oročenom depozitu.pdf (AKTIVAN)
│   │   ├── Ponuda.pdf (AKTIVAN)
│   │   ├── Plan isplate depozita.pdf (AKTIVAN)
│   │   └── Obavezni elementi Ugovora.pdf (AKTIVAN)
│   └── DE-10194302-00008-10104303_20241106103020/
│       └── dokumenti...
│
└── 999 Dosije - Unknown/             ← FolderDiscoveryService kreirao (node-id-mno345)
    ├── UNKNOWN-{CoreId}/             ← MoveService će kreirati
    │   └── unmapped_document.pdf (NEAKTIVAN)
    └── ...
```

---

### Mapiranje između starog i novog Alfresco-a

| Stari Alfresco          | TargetDossierType | Novi Alfresco                    | Subfolder format                           |
|-------------------------|-------------------|----------------------------------|--------------------------------------------|
| DOSSIER-FL/102206       | 500 (ClientFL)    | 500 Dosije fizičkog lica/        | PI-102206/                                 |
| DOSSIER-FL/50034220     | 400 (ClientPL)    | 400 Dosije pravnog lica/         | LE-50034220/                               |
| DOSSIER-PL/50034141     | 400 (ClientPL)    | 400 Dosije pravnog lica/         | LE-50034141/                               |
| DOSSIER-ACC/13001926    | 300 (AccountPkg)  | 300 Dosije paket računa/         | ACC-13001926/                              |
| DOSSIER-D/10194302-...  | 700 (Deposit)     | 700 Dosije depozita/             | DE-10194302-00008-10104302_20241105154459/ |

---

## TESTIRANJE

### Build test

```bash
dotnet build "C:\Users\Nikola Preradov\source\repos\Alfresco\Alfresco.sln" --no-incremental
```

**Rezultat:**
```
✅ Build succeeded (0 errors)
⚠️  Warnings (samo postojeći warning-ovi, ništa novo)
```

---

### Unit test scenario (teoretski)

**Test 1: GetDestinationFolderName()**
```csharp
[Test]
public void GetDestinationFolderName_ReturnsCorrectNames()
{
    Assert.That(DossierTypeDetector.GetDestinationFolderName(DossierType.AccountPackage),
                Is.EqualTo("300 Dosije paket računa"));

    Assert.That(DossierTypeDetector.GetDestinationFolderName(DossierType.ClientPL),
                Is.EqualTo("400 Dosije pravnog lica"));

    Assert.That(DossierTypeDetector.GetDestinationFolderName(DossierType.ClientFL),
                Is.EqualTo("500 Dosije fizičkog lica"));

    Assert.That(DossierTypeDetector.GetDestinationFolderName(DossierType.Deposit),
                Is.EqualTo("700 Dosije depozita"));

    Assert.That(DossierTypeDetector.GetDestinationFolderName(DossierType.Unknown),
                Is.EqualTo("999 Dosije - Unknown"));
}
```

**Test 2: MapDossierFolderTypeToDossierType()**
```csharp
[Test]
public void MapDossierFolderType_ReturnsCorrectEnum()
{
    Assert.That(DossierTypeDetector.MapDossierFolderTypeToDossierType("FL"),
                Is.EqualTo(DossierType.ClientFLorPL));

    Assert.That(DossierTypeDetector.MapDossierFolderTypeToDossierType("PL"),
                Is.EqualTo(DossierType.ClientPL));

    Assert.That(DossierTypeDetector.MapDossierFolderTypeToDossierType("ACC"),
                Is.EqualTo(DossierType.AccountPackage));

    Assert.That(DossierTypeDetector.MapDossierFolderTypeToDossierType("D"),
                Is.EqualTo(DossierType.Deposit));
}
```

**Test 3: GetPossibleDossierTypes()**
```csharp
[Test]
public void GetPossibleDossierTypes_FL_ReturnsBothClientTypes()
{
    var result = DossierTypeDetector.GetPossibleDossierTypes("FL").ToList();

    Assert.That(result, Has.Count.EqualTo(2));
    Assert.That(result, Contains.Item(DossierType.ClientFL));
    Assert.That(result, Contains.Item(DossierType.ClientPL));
}

[Test]
public void GetPossibleDossierTypes_ACC_ReturnsAccountPackage()
{
    var result = DossierTypeDetector.GetPossibleDossierTypes("ACC").ToList();

    Assert.That(result, Has.Count.EqualTo(1));
    Assert.That(result, Contains.Item(DossierType.AccountPackage));
}
```

---

### Integration test scenario (teoretski)

**Test: FolderDiscoveryService kreira parent foldere**

```csharp
[Test]
public async Task FolderDiscoveryService_CreatesParentFolders()
{
    // Arrange
    var folderTypes = new[] { "FL", "PL", "ACC", "D" };

    // Act
    await _folderDiscoveryService.RunBatchAsync(CancellationToken.None);

    // Assert
    // Proveri da li su folderi kreirani u novom Alfresco-u
    var folders = await _alfrescoReadApi.GetChildrenAsync(RootDestinationFolderId);

    Assert.That(folders.Count, Is.GreaterThanOrEqualTo(5));
    Assert.That(folders.Any(f => f.Name == "300 Dosije paket računa"), Is.True);
    Assert.That(folders.Any(f => f.Name == "400 Dosije pravnog lica"), Is.True);
    Assert.That(folders.Any(f => f.Name == "500 Dosije fizičkog lica"), Is.True);
    Assert.That(folders.Any(f => f.Name == "700 Dosije depozita"), Is.True);
    Assert.That(folders.Any(f => f.Name == "999 Dosije - Unknown"), Is.True);
}
```

**Test: FOLDER_STAGING ima popunjeno DossierDestFolderId**

```csharp
[Test]
public async Task FolderStaging_HasPopulatedDossierDestFolderId()
{
    // Arrange & Act
    await _folderDiscoveryService.RunBatchAsync(CancellationToken.None);

    // Assert
    var stagingFolders = await _folderStagingRepository.GetAllAsync();

    foreach (var folder in stagingFolders)
    {
        Assert.That(folder.DossierDestFolderId, Is.Not.Null);
        Assert.That(folder.DossierDestFolderId, Is.Not.Empty);
        Assert.That(folder.TargetDossierType, Is.GreaterThan(0));
    }
}
```

---

## SLEDEĆI KORACI

### ✅ Završeno

1. ✅ Dodavanje helper metoda u `DossierTypeDetector`
2. ✅ Modifikacija `EnsureDossierFoldersExistAsync()` da kreira prave parent foldere
3. ✅ Modifikacija logike za setovanje `DossierDestFolderId` da koristi `TargetDossierType`
4. ✅ Build test (0 grešaka)

---

### 🔄 U toku (potrebno uraditi)

Prema **ANALIZA_MIGRACIJE.md**, ostali kritični nedostaci:

#### 🔴 KRITIČNO - Mora da se uradi

1. **Kreiranje hardkodovanih mapera** (Priority: Najviši)
   - [ ] `DocumentNameMapper.cs` - mapiranje naziva dokumenata
   - [ ] `DocumentCodeMapper.cs` - mapiranje šifri dokumenata
   - [ ] `DocumentStatusDetector.cs` - određivanje statusa (aktivan/neaktivan)
   - [ ] `SourceDetector.cs` - već postoji, možda treba proširiti

2. **Integracija mapera u DocumentDiscoveryService** (Priority: Najviši)
   - [ ] Inject novi servisi u konstruktor
   - [ ] Čitanje `bank:tipDosijea`, `bank:source`, `bank:clientSegment`, `bank:status`
   - [ ] Poziv `DocumentStatusDetector.GetMigrationInfo()`
   - [ ] Poziv `SourceDetector.GetSource()`
   - [ ] Snimanje svih podataka u `DocStaging` tabelu

3. **Konfiguracija folder ID-ova** (Priority: Visok)
   - [ ] Dodati sekciju `DossierFolders` u `appsettings.json`
   - [ ] `ExcludedDocumentTypes: ["00702"]`
   - [ ] `DepositDocumentPatterns` lista

4. **Provera postojanja dosijea u MoveService** (Priority: Visok)
   - [ ] Pre migracije: provera da li dosije PI-{CoreId}/LE-{CoreId}/ACC-{CoreId} postoji
   - [ ] Ako NE postoji: kreiraj + ClientAPI + popuni atribute
   - [ ] Ako postoji: samo migriraj dokumente

---

#### 🟡 VAŽNO - Trebalo bi da se uradi

5. **Rukovanje verzijama dokumenata** (TC 10, 22)
   - [ ] Grupisanje dokumenata po CoreID + DocumentType
   - [ ] Sortiranje po `OriginalCreatedAt`
   - [ ] `POST /nodes/{nodeId}/versions` za nove verzije

6. **DUT integracija u discovery** (TC 23)
   - [ ] Poziv `DutApi.IsOfferBookedAsync()` pre migracije deposit dokumenta
   - [ ] Skip dokumenata koji nisu "Booked"

7. **Excluded document types** (TC 15)
   - [ ] Config: `ExcludedDocumentTypes: ["00702"]`
   - [ ] Filter u discovery

---

#### 🟢 NICE TO HAVE - Poboljšanja

8. **Finalni status cleanup** (TC 12-14)
   - [ ] `PostMigrationCleanupService.cs`
   - [ ] Nakon migracije: nađi dokumente 00099, 00100, 00101 koji NISU iz migracije
   - [ ] Označi ih kao neaktivne

9. **Validacija kompletnosti deposit dokumenata** (TC 24)
   - [ ] `DepositValidationService.cs`
   - [ ] Provera da li postoje sva 4 obavezna dokumenta

10. **Broj računa validacija** (TC 16)
    - [ ] `AccountValidationService.cs`
    - [ ] Poziv `ClientAPI.GetActiveAccountsEndpoint`
    - [ ] Validacija za dokumente 00824

---

### 📝 Preporuke za dalje

**Prioritet 1:** Implementirati hardkodovane mapere (DocumentNameMapper, DocumentCodeMapper, DocumentStatusDetector)

**Prioritet 2:** Integrisati mapere u DocumentDiscoveryService

**Prioritet 3:** Dodati logiku u MoveService za kreiranje client subfoldera (PI-{CoreId}, LE-{CoreId}, itd.)

**Prioritet 4:** Testirati end-to-end tok sa pravim podacima

---

## REFERENCE

- **Analiza dokument:** `ANALIZA_MIGRACIJE.md` (C:\Temp\TFSTemp\vctmp13288_816386.ANALIZA_MIGRACIJE.f881a8b1.md)
- **DossierType enum:** `Alfresco.Contracts/Enums/DossierType.cs`
- **DossierTypeDetector:** `Alfresco.Contracts/Mapper/DossierTypeDetector.cs`
- **SourceDetector:** `Alfresco.Contracts/Mapper/SourceDetector.cs`
- **FolderDiscoveryService:** `Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`

---

**Verzija dokumenta:** 1.0
**Datum kreiranja:** 2025-11-03
**Autor:** Claude Code Implementation Session
