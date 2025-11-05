# Alfresco Migration Mappers - Verzija 2.0

## 🎯 Ključne Promene

### **NOVA LOGIKA**: Mapiranje preko `ecm:docDesc` umesto imena dokumenta

**Razlog**: Ime dokumenta u starom Alfresco-u može biti random GUID.

---

## 📋 Struktura

### HeimdallDocumentMapper (CENTRALNA LISTA)

Sadrži kompletno mapiranje svih dokumenata:

```csharp
List<(
    string Naziv,              // Engleski naziv (iz starog Alfresco-a)
    string SifraDoc,           // Originalna šifra
    string NazivDoc,           // Srpski naziv
    string TipDosiea,          // Tip dosijea (određuje folder)
    string SifraDocMigracija,  // Nova šifra
    string NazivDocMigracija   // Novi naziv
)>
```

**Izvor podataka**: `C:\Users\Nikola Preradov\Desktop\Migracija_Tabele\CSV\HeimdallDis.csv`

---

## 🗂️ Klase

### 1. **HeimdallDocumentMapper.cs** ⭐ NOVA

Centralna lista sa svim mapiranjima.

**Metode**:
- `FindByOriginalName(string)` - Traži po Naziv polju
- `FindByOriginalCode(string)` - Traži po SifraDoc polju
- `GetMigratedCode(string)` - Vraća SifraDocMigracija
- `GetMigratedName(string)` - Vraća NazivDocMigracija
- `GetDossierType(string)` - Vraća TipDosiea
- `GetSerbianName(string)` - Vraća NazivDoc
- `WillReceiveMigrationSuffix(string)` - Proverava sufiks
- `CodeWillChange(string)` - Proverava promenu šifre

### 2. **DocumentCodeMapper.cs** ♻️ REFAKTORISAN

Wrapper oko `HeimdallDocumentMapper` za mapiranje šifara.

```csharp
GetMigratedCode(string originalCode) → string
CodeWillChange(string originalCode) → bool
```

### 3. **DocumentNameMapper.cs** ♻️ REFAKTORISAN

Wrapper oko `HeimdallDocumentMapper` za mapiranje naziva.

```csharp
GetMigratedName(string originalName) → string
WillReceiveMigrationSuffix(string originalName) → bool
GetSerbianName(string originalName) → string
GetDossierType(string originalName) → string
```

### 4. **DocumentStatusDetector.cs** ✨ PROŠIRENA

Dodana nova metoda koja koristi `ecm:docDesc`:

```csharp
// NOVA METODA - koristi ecm:docDesc
GetMigrationInfoByDocDesc(
    string docDesc,
    string? originalCode = null,
    string? existingStatus = null
) → DocumentMigrationInfo
```

**Vraća**:
- `OriginalName` - Originalni naziv iz ecm:docDesc
- `NewName` - Vrednost iz NazivDocMigracija
- `OriginalCode` - Originalna šifra
- `NewCode` - Vrednost iz SifraDocMigracija
- `IsActive` - Da li je dokument aktivan
- `Status` - "validiran" ili "poništen"
- `WillReceiveMigrationSuffix` - Da li ima sufiks "- migracija"
- `CodeWillChange` - Da li se šifra menja
- `TipDosiea` - Tip dosijea iz mapiranja

### 5. **DossierTypeDetector.cs** ✨ PROŠIRENA

Dodana nova metoda koja koristi `ecm:docDesc`:

```csharp
// NOVA METODA - koristi ecm:docDesc
DetectFromDocDesc(string docDesc) → DossierType

// NOVA METODA - vraća DOSSIER folder name
GetDossierFolderName(DossierType dossierType) → string
// Returns: "DOSSIER-ACC", "DOSSIER-PI", "DOSSIER-LE", "DOSSIER-D"
```

### 6. **SourceDetector.cs** ✅ BEZ PROMENA

Određuje source na osnovu DossierType:
- Heimdall: za ACC, FL, PL
- DUT: za Deposit

---

## 🔄 Tok Korišćenja

### Primer: Migracija dokumenta

```csharp
// 1. Učitaj dokument iz starog Alfresco-a
var document = GetDocumentFromOldAlfresco(documentId);
string docDesc = document.Properties["ecm:docDesc"];  // "Personal Notice"

// 2. Dobij informacije o migraciji
var migrationInfo = DocumentStatusDetector.GetMigrationInfoByDocDesc(
    docDesc: docDesc,
    originalCode: "00253",
    existingStatus: "validiran"
);

// 3. Odredi tip dosijea
var dossierType = DossierTypeDetector.DetectFromDocDesc(docDesc);

// 4. Razreši FL/PL ako je potrebno
if (dossierType == DossierType.ClientFLorPL)
{
    var clientInfo = await ClientApi.GetClientInfo(coreId);
    dossierType = DossierTypeDetector.ResolveFLorPL(clientInfo.Segment);
}

// 5. Dobij destination folder
var destinationFolder = DossierTypeDetector.GetDossierFolderName(dossierType);
// Result: "DOSSIER-PI"

// 6. Formiraj dossier ID
var dossierId = DossierIdFormatter.Format(dossierType, coreId);
// Result: "PI-13001926"

// 7. Odredi source
var source = SourceDetector.GetSource(dossierType);
// Result: "Heimdall"

// 8. Migriraj dokument
await MigrateDocument(
    sourceDocument: document,
    destinationFolder: destinationFolder,
    dossierId: dossierId,
    newName: migrationInfo.NewName,
    newCode: migrationInfo.NewCode,
    status: migrationInfo.Status,
    source: source
);
```

---

## 📊 Mapiranje TipDosiea → Destination

| TipDosiea | Destination Folder | Dossier ID Format |
|-----------|-------------------|-------------------|
| Dosije paket racuna | DOSSIER-ACC | ACC-{CoreId} |
| Dosije klijenta FL / PL | DOSSIER-PI ili DOSSIER-LE | PI-{CoreId} ili LE-{CoreId} |
| Dosije klijenta PL | DOSSIER-LE | LE-{CoreId} |
| Dosije depozita | DOSSIER-D | DE-{CoreId}-{SifraTipaProizvoda}-{brojUgovora} |

---

## ⚠️ Važno

### 1. **ecm:docDesc je ključno polje**
- NE koristiti ime dokumenta za mapiranje
- Ime dokumenta može biti GUID
- ecm:docDesc sadrži Naziv ili NazivDoc iz liste

### 2. **Sufiks "- migracija" određuje status**
- Ako `NazivDocMigracija` završava sa "- migracija" → Status = "poništen" (NEAKTIVAN)
- Inače → Status = "validiran" (AKTIVAN)

### 3. **Lista nije konačna**
- `HeimdallDocumentMapper` lista će se ažurirati
- Biće dodata nova lista za DUTSource

### 4. **FL/PL zahteva ClientAPI**
- Tip "Dosije klijenta FL / PL" zahteva poziv ClientAPI-a
- Segment određuje da li je DOSSIER-PI ili DOSSIER-LE

---

## 🔗 Dodatna Dokumentacija

Pogledajte `MIGRATION_RULES_V2.md` za detaljnu dokumentaciju pravila migracije.

---

## 📝 Primer iz Liste

```csharp
// CSV red:
"Personal Notice;00253;GDPR saglasnost;Dosije klijenta FL / PL;00849;GDPR saglasnost - migracija"

// Mapiranje:
Naziv: "Personal Notice"
SifraDoc: "00253"
NazivDoc: "GDPR saglasnost"
TipDosiea: "Dosije klijenta FL / PL"
SifraDocMigracija: "00849"
NazivDocMigracija: "GDPR saglasnost - migracija"

// Rezultat migracije:
NewName: "GDPR saglasnost - migracija"
NewCode: "00849"
Status: "poništen" (jer ima sufiks "- migracija")
TipDosiea: "Dosije klijenta FL / PL"
DestinationFolder: "DOSSIER-PI" ili "DOSSIER-LE" (zavisno od ClientAPI)
```

---

**Verzija**: 2.0
**Datum**: 2025-11-05
**Izvor podataka**: HeimdallDis.csv
