# Service Mapping Updates - Verzija 2.0

## 📋 Pregled

Ovaj dokument opisuje ažuriranja u servisima za mapiranje dokumenata tokom migracije.

**Datum**: 2025-11-05
**Verzija**: 2.0

---

## 🎯 Glavna Promena

### ❌ STARA LOGIKA
Svaki servis imao je hardkodiran Dictionary sa mapiranjima:
```csharp
// DocumentNameMapper
private static readonly Dictionary<string, string> NameMappings = new() { ... };

// DocumentCodeMapper
private static readonly Dictionary<string, string> CodeMappings = new() { ... };

// OpisToTipMapper
private static readonly Dictionary<string, string> Mappings = new() { ... };
```

### ✅ NOVA LOGIKA
Svi servisi koriste centralizovan `HeimdallDocumentMapper`:
```csharp
// Dinamičko učitavanje iz centralne liste
var mapping = HeimdallDocumentMapper.FindByOriginalName(documentName);
return mapping?.SifraDocMigracija;
```

---

## 🔧 Ažurirani Servisi

### 1. **OpisToTipMapper.cs** ⭐ KLJUČNA PROMENA

**Lokacija**: `Alfresco.Contracts\Mapper\OpisToTipMapper.cs`

**Šta radi**: Mapira `ecm:docDesc` → `ecm:docType` (šifru dokumenta)

**Promene**:
- ❌ Uklonjen hardkodiran Dictionary sa 80+ stavki
- ✅ Koristi `HeimdallDocumentMapper.FindByOriginalName()`
- ✅ Podržava pretragu po engleskom nazivu (Naziv)
- ✅ Podržava pretragu po srpskom nazivu (NazivDoc)
- ✅ Podržava pretragu po migriranom nazivu (NazivDocMigracija)

**Nove metode**:
```csharp
// Dobij šifru dokumenta
string GetTipDokumenta(string opisDokumenta)
// Returns: SifraDocMigracija ili "UNKNOWN"

// Proveri da li postoji mapping
bool IsKnownOpis(string opisDokumenta)

// Dobij sve mappinge (za debugging)
IReadOnlyDictionary<string, string> GetAllMappings()

// Dobij kompletno mapiranje (NOVO)
(string Naziv, string SifraDoc, string NazivDoc, string TipDosiea,
 string SifraDocMigracija, string NazivDocMigracija)? GetFullMapping(string opisDokumenta)
```

**Koristi se u**:
- `DocumentDiscoveryService.ApplyDocumentMapping()` - line 1180
- Za mapiranje `ecm:docDesc` → `ecm:docType`

---

### 2. **DocumentDiscoveryService.cs** ✅ BEZ PROMENA

**Lokacija**: `Migration.Infrastructure\Implementation\Services\DocumentDiscoveryService.cs`

**Status**: Već koristi `ecm:docDesc` i poziva mappere!

**Tok mapiranja**:
```csharp
// Line 1089: Izvlači ecm:docDesc iz starog Alfresco-a
if (alfrescoEntry.Properties.TryGetValue("ecm:docDesc", out var docDescObj))
    docDesc = docDescObj?.ToString();

// Line 1180: Koristi OpisToTipMapper za mapiranje
mappedDocType = OpisToTipMapper.GetTipDokumenta(docDesc);

// Line 1200: Određuje status na osnovu ecm:docDesc
var statusInfo = DocumentStatusDetector.GetStatusInfoByOpis(docDesc, existingStatus);
```

**Ključni podaci koje izvlači**:
1. `ecm:docDesc` → DocDescription
2. `ecm:docType` → OriginalDocumentCode
3. `ecm:status` → OldAlfrescoStatus
4. `ecm:docDossierType` → TipDosijea
5. `ecm:docClientType` → ClientSegment
6. `ecm:source` → Source

**Mapiranje koje primenjuje**:
- `OpisToTipMapper.GetTipDokumenta(docDesc)` → DocumentType
- `DocumentStatusDetector.GetStatusInfoByOpis(docDesc)` → IsActive, NewAlfrescoStatus
- `DestinationRootFolderDeterminator.DetermineAndResolve()` → TargetDossierType
- `SourceDetector.GetSource()` → Source

---

### 3. **MoveService.cs** ✅ BEZ PROMENA

**Lokacija**: `Migration.Infrastructure\Implementation\Services\MoveService.cs`

**Status**: Koristi podatke koje je `DocumentDiscoveryService` već pripremio!

**Metoda**: `BuildDocumentProperties()` - line 924

```csharp
private Dictionary<string, object> BuildDocumentProperties(DocStaging doc)
{
    var properties = new Dictionary<string, object>
    {
        ["cm:title"] = doc.DocDescription ?? doc.Name ?? "Unknown",
        ["cm:description"] = doc.DocDescription ?? "",
        ["ecm:docDesc"] = doc.DocDescription ?? "",  // ← Koristi već mapirani opis
        ["ecm:coreId"] = doc.CoreId ?? "",
        ["ecm:status"] = doc.NewAlfrescoStatus ?? "validiran",
        ["ecm:docType"] = doc.DocumentType ?? "",  // ← Koristi već mapiranu šifru
        ["ecm:docDossierType"] = doc.TipDosijea ?? "",
        ["ecm:docClientType"] = doc.ClientSegment ?? "",
        ["ecm:source"] = doc.Source ?? "Heimdall"
    };

    // ... ostale properties
}
```

**Napomena**: `MoveService` NE radi mapiranje, samo koristi podatke iz `DocStaging` objekta!

---

### 4. **DocumentStatusDetector.cs** ✅ PROŠIREN

**Lokacija**: `Alfresco.Contracts\Mapper\DocumentStatusDetector.cs`

**Status**: Dodată nova metoda `GetMigrationInfoByDocDesc()`

**Već postojeća metoda**:
```csharp
// Koristi se u DocumentDiscoveryService
public static DocumentStatusInfo GetStatusInfoByOpis(
    string? opisDokumenta,
    string? existingStatus = null)
```

**Nova metoda** (dodată u prethodnoj iteraciji):
```csharp
// Za direktno testiranje mapiranja
public static DocumentMigrationInfo GetMigrationInfoByDocDesc(
    string docDesc,
    string? originalCode = null,
    string? existingStatus = null)
{
    var mapping = HeimdallDocumentMapper.FindByOriginalName(docDesc);
    // Vraća kompletne informacije o migraciji
}
```

---

### 5. **DocumentNameMapper.cs** ✅ VEĆ AŽURIRAN

**Status**: Već ažuriran da koristi `HeimdallDocumentMapper`

**Metode**:
```csharp
GetMigratedName(string originalName) → HeimdallDocumentMapper.GetMigratedName()
WillReceiveMigrationSuffix(string) → HeimdallDocumentMapper.WillReceiveMigrationSuffix()
GetSerbianName(string) → HeimdallDocumentMapper.GetSerbianName()
GetDossierType(string) → HeimdallDocumentMapper.GetDossierType()
```

---

### 6. **DocumentCodeMapper.cs** ✅ VEĆ AŽURIRAN

**Status**: Već ažuriran da koristi `HeimdallDocumentMapper`

**Metode**:
```csharp
GetMigratedCode(string originalCode) → HeimdallDocumentMapper.GetMigratedCode()
CodeWillChange(string originalCode) → HeimdallDocumentMapper.CodeWillChange()
```

---

## 🔄 Tok Mapiranja u Migraciji

### Faza 1: DocumentDiscoveryService (Discovery)

```
1. Učitaj dokument iz starog Alfresco-a
   ↓
2. Izvuci ecm:docDesc property
   ecm:docDesc = "Personal Notice"
   ↓
3. Koristi OpisToTipMapper za mapiranje šifre
   OpisToTipMapper.GetTipDokumenta("Personal Notice")
   ↓
   HeimdallDocumentMapper.FindByOriginalName("Personal Notice")
   ↓
   Result: SifraDocMigracija = "00849"
   ↓
4. Koristi DocumentStatusDetector za status
   GetStatusInfoByOpis("Personal Notice")
   ↓
   Provera sufiksa "- migracija" u NazivDocMigracija
   ↓
   Result: IsActive = false, Status = "poništen"
   ↓
5. Sačuvaj sve u DocStaging:
   - DocDescription = "Personal Notice"
   - DocumentType = "00849"
   - IsActive = false
   - NewAlfrescoStatus = "poništen"
   - TipDosijea = "Dosije klijenta FL / PL"
   - Source = "Heimdall"
```

### Faza 2: MoveService (Migration)

```
1. Učitaj DocStaging objekat (već pripremljen)
   ↓
2. Kreiraj destination folder
   ↓
3. Premesti dokument
   ↓
4. Ažuriraj properties u novom Alfresco-u:
   BuildDocumentProperties(doc)
   ↓
   Properties:
   {
       "ecm:docDesc": "Personal Notice",
       "ecm:docType": "00849",
       "ecm:status": "poništen",
       "ecm:docDossierType": "Dosije klijenta FL / PL",
       "ecm:source": "Heimdall"
   }
```

---

## 📊 Mapiranje Podataka

### Input (Stari Alfresco)

| Property | Primer Vrednosti |
|----------|-----------------|
| ecm:docDesc | "Personal Notice" |
| ecm:docType | "00253" (originalna šifra) |
| ecm:status | "validiran" |
| ecm:coreId | "13001926" |

### Mapiranje (HeimdallDocumentMapper)

| Polje | Vrednost |
|-------|----------|
| Naziv | "Personal Notice" |
| SifraDoc | "00253" |
| NazivDoc | "GDPR saglasnost" |
| TipDosiea | "Dosije klijenta FL / PL" |
| SifraDocMigracija | "00849" |
| NazivDocMigracija | "GDPR saglasnost - migracija" |

### Output (Novi Alfresco)

| Property | Vrednost |
|----------|----------|
| ecm:docDesc | "Personal Notice" |
| ecm:docType | "00849" (nova šifra) |
| ecm:status | "poništen" (zbog sufiksa "- migracija") |
| ecm:docDossierType | "Dosije klijenta FL / PL" |
| ecm:source | "Heimdall" |

---

## ⚠️ Važne Napomene

### 1. **ecm:docDesc je Ključan Property**

- `DocumentDiscoveryService` izvlači `ecm:docDesc` iz starog Alfresco-a
- `OpisToTipMapper` koristi `ecm:docDesc` za mapiranje
- `DocumentStatusDetector` koristi `ecm:docDesc` za određivanje statusa
- `MoveService` kopira `ecm:docDesc` u novi Alfresco

### 2. **Sufiks "- migracija" Određuje Status**

```csharp
// U HeimdallDocumentMapper listi:
NazivDocMigracija = "GDPR saglasnost - migracija"
                                      ↑↑↑↑↑↑↑↑↑↑↑
                                      Sufiks koji označava NEAKTIVAN dokument

// DocumentStatusDetector proverava:
bool hasSuffix = NazivDocMigracija.EndsWith("- migracija");
Status = hasSuffix ? "poništen" : "validiran";
```

### 3. **OpisToTipMapper Podržava Tri Metoda Pretrage**

```csharp
// 1. Pretraga po engleskom nazivu (Naziv)
FindByOriginalName("Personal Notice")

// 2. Pretraga po srpskom nazivu (NazivDoc)
mappings.FirstOrDefault(m => m.NazivDoc == "GDPR saglasnost")

// 3. Pretraga po migriranom nazivu (NazivDocMigracija)
mappings.FirstOrDefault(m => m.NazivDocMigracija == "GDPR saglasnost - migracija")
```

### 4. **DocumentDiscoveryService je "Priprema", MoveService je "Izvršenje"**

- **DocumentDiscoveryService**: Izvlači podatke iz starog Alfresco-a, mapira ih, i čuva u `DocStaging`
- **MoveService**: Čita iz `DocStaging`, premešta dokument, i postavlja properties u novom Alfresco-u

---

## 🧪 Testiranje

### Test 1: Provera Mapiranja Šifre

```csharp
// Input
string docDesc = "Personal Notice";

// Mapiranje
var tipDokumenta = OpisToTipMapper.GetTipDokumenta(docDesc);

// Expected
Assert.Equal("00849", tipDokumenta);
```

### Test 2: Provera Kompletnog Mapiranja

```csharp
// Input
string docDesc = "Personal Notice";

// Mapiranje
var fullMapping = OpisToTipMapper.GetFullMapping(docDesc);

// Expected
Assert.NotNull(fullMapping);
Assert.Equal("Personal Notice", fullMapping.Value.Naziv);
Assert.Equal("00253", fullMapping.Value.SifraDoc);
Assert.Equal("GDPR saglasnost", fullMapping.Value.NazivDoc);
Assert.Equal("Dosije klijenta FL / PL", fullMapping.Value.TipDosiea);
Assert.Equal("00849", fullMapping.Value.SifraDocMigracija);
Assert.Equal("GDPR saglasnost - migracija", fullMapping.Value.NazivDocMigracija);
```

### Test 3: Provera Sufiksa

```csharp
// Input
string docDesc = "Personal Notice";

// Mapping
var mapping = HeimdallDocumentMapper.FindByOriginalName(docDesc);
bool hasSuffix = mapping.Value.NazivDocMigracija.EndsWith("- migracija");

// Expected
Assert.True(hasSuffix);
// Dokument će biti migriran kao NEAKTIVAN
```

---

## 📝 Changelog

### Verzija 2.0 (2025-11-05)

#### ✅ Ažurirano
1. **OpisToTipMapper.cs**
   - Uklonjen hardkodiran Dictionary
   - Dodată metoda `GetFullMapping()`
   - Koristi `HeimdallDocumentMapper` kao centralni izvor

2. **DocumentNameMapper.cs** (prethodna iteracija)
   - Koristi `HeimdallDocumentMapper`

3. **DocumentCodeMapper.cs** (prethodna iteracija)
   - Koristi `HeimdallDocumentMapper`

4. **DocumentStatusDetector.cs** (prethodna iteracija)
   - Dodată metoda `GetMigrationInfoByDocDesc()`

5. **HeimdallDocumentMapper.cs** (prethodna iteracija)
   - Kreirana centralna lista sa 40 dokumenata

#### ✅ Bez Promena
1. **DocumentDiscoveryService.cs**
   - Već koristi `ecm:docDesc` i mappere
   - Tok mapiranja već implementiran

2. **MoveService.cs**
   - Koristi podatke iz `DocStaging`
   - Ne radi mapiranje direktno

---

## 🔗 Povezani Dokumenti

- `MIGRATION_RULES_V2.md` - Detaljna pravila migracije
- `Alfresco.Contracts/Mapper/README.md` - Vodič za mappere
- `CHANGES_SUMMARY_V2.md` - Pregled promena

---

**Verzija**: 2.0
**Status**: ✅ Implementirano i testirano
**Build Status**: Success (0 errors, samo warnings)
