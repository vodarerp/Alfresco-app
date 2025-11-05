# Fix: ACC Dossier Folder Naming

## 📋 Problem

Dokumenti koji se migriraju u **DOSSIER-ACC** (Account Package dosijee) su imali pogrešna imena foldera:

**Bilo**:
- `LE-500342` → `LE500342` (folder u DOSSIERS-ACC)
- `PI-102206` → `PI102206` (folder u DOSSIERS-ACC)

**Trebalo**:
- `LE-500342` → `ACC500342` (folder u DOSSIERS-ACC)
- `PI-102206` → `ACC102206` (folder u DOSSIERS-ACC)

---

## 🔧 Rešenje

### 1. **Dodată Nova Metoda u `DossierIdFormatter`**

**Lokacija**: `Alfresco.Contracts\Mapper\DossierIdFormatter.cs`

```csharp
/// <summary>
/// Converts dossier ID to ACC format based on target dossier type
/// Used when documents are migrated to DOSSIER-ACC (Account Package)
/// </summary>
public static string ConvertForTargetType(string oldDossierId, int targetDossierType)
{
    var coreId = ExtractCoreId(oldDossierId);

    // Determine the new prefix based on target dossier type
    string newPrefix = targetDossierType switch
    {
        300 => "ACC",  // Account Package dossier
        400 => "LE",   // Legal Entity dossier
        500 => "PI",   // Physical Individual dossier
        700 => "DE",   // Deposit dossier
        _ => ExtractPrefix(oldDossierId) // Keep original prefix
    };

    return CreateNewDossierId(newPrefix, coreId);
}
```

**Dodatna Metoda** (sa Enum parametrom):
```csharp
public static string ConvertWithPrefixChange(
    string oldDossierId,
    Enums.DossierType targetDossierType)
{
    return ConvertForTargetType(oldDossierId, (int)targetDossierType);
}
```

---

### 2. **Ažuriran `DocumentDiscoveryService`**

**Lokacija**: `Migration.Infrastructure\Implementation\Services\DocumentDiscoveryService.cs`

**Stara Logika** (Line ~1238):
```csharp
// Convert: PI-102206 → PI102206 (remove hyphen)
doc.DossierDestFolderId = DossierIdFormatter.ConvertToNewFormat(folder.Name);
```

**Nova Logika**:
```csharp
// IMPORTANT: For ACC dosijee, convert PI/LE → ACC prefix
// Example: PI-102206 → ACC102206 (if targetType = AccountPackage)
doc.DossierDestFolderId = DossierIdFormatter.ConvertForTargetType(
    folder.Name,
    doc.TargetDossierType ?? (int)DossierType.Unknown);

_fileLogger.LogTrace(
    "Converted dossier ID: '{OldId}' (Type: {OldType}) → '{NewId}' (TargetType: {TargetType})",
    folder.Name, folder.TipDosijea, doc.DossierDestFolderId, destinationType);
```

---

## 🔄 Kako Funkcioniše

### Scenario 1: PI Dokument → ACC Dossier

```
Input:
- folder.Name = "PI-102206"
- doc.TargetDossierType = 300 (AccountPackage)

Process:
DossierIdFormatter.ConvertForTargetType("PI-102206", 300)
├── ExtractCoreId("PI-102206") → "102206"
├── targetDossierType = 300 → newPrefix = "ACC"
└── CreateNewDossierId("ACC", "102206") → "ACC102206"

Output:
- doc.DossierDestFolderId = "ACC102206"
- Destination: DOSSIERS-ACC/ACC102206/
```

### Scenario 2: LE Dokument → ACC Dossier

```
Input:
- folder.Name = "LE-500342"
- doc.TargetDossierType = 300 (AccountPackage)

Process:
DossierIdFormatter.ConvertForTargetType("LE-500342", 300)
├── ExtractCoreId("LE-500342") → "500342"
├── targetDossierType = 300 → newPrefix = "ACC"
└── CreateNewDossierId("ACC", "500342") → "ACC500342"

Output:
- doc.DossierDestFolderId = "ACC500342"
- Destination: DOSSIERS-ACC/ACC500342/
```

### Scenario 3: PI Dokument → PI Dossier (Bez Promene)

```
Input:
- folder.Name = "PI-102206"
- doc.TargetDossierType = 500 (ClientFL)

Process:
DossierIdFormatter.ConvertForTargetType("PI-102206", 500)
├── ExtractCoreId("PI-102206") → "102206"
├── targetDossierType = 500 → newPrefix = "PI"
└── CreateNewDossierId("PI", "102206") → "PI102206"

Output:
- doc.DossierDestFolderId = "PI102206"
- Destination: DOSSIERS-PI/PI102206/
```

---

## 📊 Mapiranje Target Type → Prefix

| TargetDossierType | Enum Value | Novi Prefix | Destination Folder |
|-------------------|------------|-------------|-------------------|
| 300 | AccountPackage | ACC | DOSSIERS-ACC |
| 400 | ClientPL | LE | DOSSIERS-LE |
| 500 | ClientFL | PI | DOSSIERS-PI |
| 700 | Deposit | DE | DOSSIERS-D |
| Other | Unknown | (Original) | (Original) |

---

## 💡 Primeri

### Primer 1: Account Package Dokument

```csharp
// Test Case iz TestCase-migracija.txt - TC 3
// Document: "Account Package" iz LE-500342 foldera

// Pre Fixa:
folder.Name = "LE-500342"
TargetDossierType = 300
DossierDestFolderId = "LE500342"  // POGREŠNO!
Destination = "DOSSIERS-ACC/LE500342/"  // POGREŠNO!

// Posle Fixa:
folder.Name = "LE-500342"
TargetDossierType = 300
DossierDestFolderId = "ACC500342"  // ISPRAVNO!
Destination = "DOSSIERS-ACC/ACC500342/"  // ISPRAVNO!
```

### Primer 2: KYC Questionnaire (ostaje u PI)

```csharp
// Document: "KYC Questionnaire" iz PI-102206 foldera

folder.Name = "PI-102206"
TargetDossierType = 500  // ClientFL
DossierDestFolderId = "PI102206"  // ISPRAVNO (bez promene prefixa)
Destination = "DOSSIERS-PI/PI102206/"  // ISPRAVNO
```

### Primer 3: Communication Consent (ostaje u LE)

```csharp
// Document: "Communication Consent" iz LE-500342 foldera

folder.Name = "LE-500342"
TargetDossierType = 400  // ClientPL
DossierDestFolderId = "LE500342"  // ISPRAVNO (bez promene prefixa)
Destination = "DOSSIERS-LE/LE500342/"  // ISPRAVNO
```

---

## 🧪 Test Cases

### Test 1: PI → ACC Conversion

```csharp
var oldId = "PI-102206";
var targetType = 300; // AccountPackage

var newId = DossierIdFormatter.ConvertForTargetType(oldId, targetType);

Assert.Equal("ACC102206", newId);
```

### Test 2: LE → ACC Conversion

```csharp
var oldId = "LE-500342";
var targetType = 300; // AccountPackage

var newId = DossierIdFormatter.ConvertForTargetType(oldId, targetType);

Assert.Equal("ACC500342", newId);
```

### Test 3: PI → PI (No Change)

```csharp
var oldId = "PI-102206";
var targetType = 500; // ClientFL

var newId = DossierIdFormatter.ConvertForTargetType(oldId, targetType);

Assert.Equal("PI102206", newId);
```

### Test 4: LE → LE (No Change)

```csharp
var oldId = "LE-500342";
var targetType = 400; // ClientPL

var newId = DossierIdFormatter.ConvertForTargetType(oldId, targetType);

Assert.Equal("LE500342", newId);
```

---

## ⚙️ Affected Components

### ✅ Ažurirano

1. **DossierIdFormatter.cs**
   - Dodată metoda `ConvertForTargetType()`
   - Dodată metoda `ConvertWithPrefixChange()`

2. **DocumentDiscoveryService.cs**
   - Ažuriran poziv u `ApplyDocumentMapping()` metodi
   - Sada koristi `ConvertForTargetType()` umesto `ConvertToNewFormat()`

### ✅ Bez Promena

1. **MoveService.cs**
   - Koristi `doc.DossierDestFolderId` koji je već postavljen
   - Ne radi konverziju direktno

2. **FolderDiscoveryService.cs**
   - Čita folder imena iz starog Alfresco-a
   - Ne radi konverziju

---

## 🔍 Kako Detektovati Problem

### Query 1: Pronađi Pogrešne ACC Dosijee

```sql
SELECT
    Id,
    DossierDestFolderId,
    TargetDossierType,
    CoreId,
    Name
FROM DOC_STAGING
WHERE TargetDossierType = 300  -- ACC dosijee
  AND DossierDestFolderId NOT LIKE 'ACC%'  -- Ne počinje sa ACC
ORDER BY DossierDestFolderId;
```

**Očekivani Rezultat Pre Fixa**:
```
Id  | DossierDestFolderId | TargetDossierType | CoreId   | Name
----|---------------------|-------------------|----------|------------------------
123 | LE500342            | 300               | 500342   | Account Package.pdf
456 | PI102206            | 300               | 102206   | Specimen card.pdf
```

**Očekivani Rezultat Posle Fixa**:
```
Id  | DossierDestFolderId | TargetDossierType | CoreId   | Name
----|---------------------|-------------------|----------|------------------------
123 | ACC500342           | 300               | 500342   | Account Package.pdf
456 | ACC102206           | 300               | 102206   | Specimen card.pdf
```

### Query 2: Verifikuj Sve ACC Dosijee

```sql
SELECT
    DossierDestFolderId,
    COUNT(*) as DocumentCount
FROM DOC_STAGING
WHERE TargetDossierType = 300
GROUP BY DossierDestFolderId
ORDER BY DossierDestFolderId;
```

**Svi rezultati treba da počinju sa "ACC"**!

---

## 📝 Migration Impact

### Pre Migracije

Ako ste već pokrenuli migraciju sa starom logikom:

1. **Provera**: Koristite Query 1 da pronađete pogrešne dosijee
2. **Čišćenje**: Obrišite pogrešno kreirane foldere u DOSSIERS-ACC
3. **Re-discovery**: Pokrenite Discovery ponovo sa novom logikom
4. **Re-migration**: Migrirajte ponovo

### Nova Migracija

Sa novom logikom, svi ACC dosijei će automatski dobiti ispravna imena.

---

## ✅ Build Status

```
Build succeeded.
    0 Error(s)
    11 Warning(s) (samo nullability warnings)
```

---

## 🔗 Related Files

- `DossierIdFormatter.cs` - Konverzija ID formata
- `DocumentDiscoveryService.cs` - Discovery proces
- `MoveService.cs` - Migracija dokumenata
- `TestCase-migracija.txt` - Test Case 3 (Dosije paket računa)

---

## 📌 Summary

**Problem**: ACC dosijei imali prefix iz source foldera (PI/LE)

**Rešenje**: Nova metoda `ConvertForTargetType()` koja menja prefix na osnovu target type-a

**Rezultat**: Svi ACC dosijei sada imaju prefix "ACC"

**Primer**:
- `PI-102206` → `ACC102206` (u DOSSIERS-ACC)
- `LE-500342` → `ACC500342` (u DOSSIERS-ACC)

---

**Datum**: 2025-11-05
**Status**: ✅ Fixed & Tested
**Build**: Success
