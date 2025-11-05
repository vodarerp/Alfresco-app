# Pravila Migracije i Mapiranja - Verzija 2.0

## 📋 Pregled

Ovaj dokument opisuje ažurirana pravila za migraciju dokumenata iz starog u novi Alfresco sistem.

**KLJUČNA PROMENA**: Ime dokumenta može biti random GUID. Za mapiranje koristimo `ecm:docDesc` property koji sadrži vrednosti iz `HeimdallDocumentMapper` liste.

---

## 🔑 Osnovna Logika Mapiranja

### 1. **Identifikacija Dokumenta**

```
ecm:docDesc → Sadrži vrednost iz polja "Naziv" ili "NazivDoc" iz DocumentMappings liste
```

**Napomena**: `ecm:docDesc` je ključno polje jer ime dokumenta može biti random GUID.

### 2. **Proces Mapiranja**

```csharp
// Primer: ecm:docDesc = "Personal Notice"
var mapping = HeimdallDocumentMapper.FindByOriginalName("Personal Notice");

// Rezultat mapiranja:
// Naziv: "Personal Notice"
// SifraDoc: "00253"
// NazivDoc: "GDPR saglasnost"
// TipDosiea: "Dosije klijenta FL / PL"
// SifraDocMigracija: "00849"
// NazivDocMigracija: "GDPR saglasnost - migracija"
```

---

## 📊 Struktura Mapiranja

### HeimdallDocumentMapper Lista

```csharp
List<(
    string Naziv,              // Engleski naziv iz starog Alfresco-a
    string SifraDoc,           // Originalna šifra dokumenta
    string NazivDoc,           // Srpski naziv dokumenta
    string TipDosiea,          // Tip dosijea (određuje destination folder)
    string SifraDocMigracija,  // Nova šifra dokumenta nakon migracije
    string NazivDocMigracija   // Novi naziv dokumenta nakon migracije
)>
```

---

## 🎯 Pravila za Status Dokumenta

### Test Case 1: Dokumenti sa Sufiksom "- migracija"

**Pravilo**: Ako `NazivDocMigracija` sadrži sufiks "- migracija" → dokument je **NEAKTIVAN**

```csharp
// Primer:
ecm:docDesc = "Personal Notice"
→ NazivDocMigracija = "GDPR saglasnost - migracija"
→ Status = "poništen" (NEAKTIVAN)
```

**Alfresco status**: `poništen`

### Test Case 2: Dokumenti bez Sufiksa

**Pravilo**: Ako `NazivDocMigracija` NE sadrži sufiks "- migracija" → dokument je **AKTIVAN**

```csharp
// Primer:
ecm:docDesc = "Current Accounts Contract"
→ NazivDocMigracija = "Ugovor o tekućem računu"
→ Status = "validiran" (AKTIVAN)
```

**Alfresco status**: `validiran`

### Test Case 11: Provera Postojećeg Statusa

**Pravilo**: Ako je dokument bio neaktivan u starom sistemu → ostaje **NEAKTIVAN**

```csharp
// Prioritet provere:
1. Da li je već bio neaktivan? → Ostaje neaktivan
2. Da li ima sufiks "- migracija"? → Postaje neaktivan
3. Inače → Aktivan
```

---

## 📁 Pravila za Mapiranje Dosijea

### Test Case 3: Dosije Paket Računa

**Pravilo**: `TipDosiea = "Dosije paket racuna"` → **DOSSIER-ACC**

```csharp
// Primer:
ecm:docDesc = "Admission Card"
→ TipDosiea = "Dosije paket racuna"
→ Destination Folder = "DOSSIER-ACC"
→ Dossier ID Format = "ACC-{CoreId}"
```

### Test Case 4: Dosije Klijenta FL/PL

**Pravilo**: `TipDosiea = "Dosije klijenta FL / PL"` → **DOSSIER-PI** ili **DOSSIER-LE** (zavisi od ClientAPI)

```csharp
// Primer:
ecm:docDesc = "Personal Notice"
→ TipDosiea = "Dosije klijenta FL / PL"
→ ClientAPI poziv → Segment = "PI"
→ Destination Folder = "DOSSIER-PI"
→ Dossier ID Format = "PI-{CoreId}"
```

**Logika razrešavanja**:
- Poziv ClientAPI-a sa `CoreId`
- Segment = "PI" ili "RETAIL" → **DOSSIER-PI**
- Segment = "LE" ili "SME" → **DOSSIER-LE**

### Test Case 5: Dosije Klijenta PL (samo PL)

**Pravilo**: `TipDosiea = "Dosije klijenta PL"` (bez "FL") → **DOSSIER-LE**

```csharp
→ Destination Folder = "DOSSIER-LE"
→ Dossier ID Format = "LE-{CoreId}"
```

### Test Case 17: Dosije Depozita

**Pravilo**: `TipDosiea = "Dosije depozita"` → **DOSSIER-D**

```csharp
→ Destination Folder = "DOSSIER-D"
→ Dossier ID Format = "DE-{CoreId}-{SifraTipaProizvoda}-{brojUgovora}"
→ Source = "DUT"
```

---

## 🗂️ Mapiranje TipDosiea → Destination Folder

| TipDosiea | DossierType | Destination Folder | Dossier ID Format | Source |
|-----------|-------------|-------------------|-------------------|--------|
| Dosije paket racuna | AccountPackage | DOSSIER-ACC | ACC-{CoreId} | Heimdall |
| Dosije klijenta FL / PL | ClientFLorPL | DOSSIER-PI ili DOSSIER-LE | PI-{CoreId} ili LE-{CoreId} | Heimdall |
| Dosije klijenta PL | ClientPL | DOSSIER-LE | LE-{CoreId} | Heimdall |
| Dosije fizičkog lica | ClientFL | DOSSIER-PI | PI-{CoreId} | Heimdall |
| Dosije pravnog lica | ClientPL | DOSSIER-LE | LE-{CoreId} | Heimdall |
| Dosije depozita | Deposit | DOSSIER-D | DE-{CoreId}-{SifraTipaProizvoda}-{brojUgovora} | DUT |
| Dosije ostalo | Other | DOSSIER-UNKNOWN | - | Heimdall |

---

## 🔄 Tok Migracije Dokumenta

### Korak 1: Učitavanje Dokumenta iz Starog Alfresco-a

```csharp
// Učitaj dokument
var document = GetDocumentFromOldAlfresco(documentId);

// Ključni properti-ji:
string docDesc = document.Properties["ecm:docDesc"];  // Npr. "Personal Notice"
string originalCode = document.Properties["ecm:sifraDoc"];  // Npr. "00253"
string existingStatus = document.Properties["ecm:status"];  // Npr. "validiran"
string coreId = document.Properties["ecm:coreId"];  // Npr. "13001926"
```

### Korak 2: Mapiranje Pomoću ecm:docDesc

```csharp
// Koristi ecm:docDesc za mapiranje
var migrationInfo = DocumentStatusDetector.GetMigrationInfoByDocDesc(
    docDesc: docDesc,
    originalCode: originalCode,
    existingStatus: existingStatus
);

// Rezultat:
// migrationInfo.NewName = "GDPR saglasnost - migracija"
// migrationInfo.NewCode = "00849"
// migrationInfo.Status = "poništen"
// migrationInfo.TipDosiea = "Dosije klijenta FL / PL"
// migrationInfo.IsActive = false
```

### Korak 3: Određivanje Destination Foldera

```csharp
// Koristi TipDosiea za određivanje foldera
var dossierType = DossierTypeDetector.DetectFromDocDesc(docDesc);
// dossierType = DossierType.ClientFLorPL

// Razreši FL/PL pomoću ClientAPI
if (dossierType == DossierType.ClientFLorPL)
{
    var clientInfo = await ClientApi.GetClientInfo(coreId);
    dossierType = DossierTypeDetector.ResolveFLorPL(clientInfo.Segment);
    // dossierType = DossierType.ClientFL
}

// Dobij destination folder
var destinationFolder = DossierTypeDetector.GetDossierFolderName(dossierType);
// destinationFolder = "DOSSIER-PI"
```

### Korak 4: Formiranje Dossier ID

```csharp
// Koristi DossierIdFormatter
var dossierId = DossierIdFormatter.Format(dossierType, coreId);
// dossierId = "PI-13001926"
```

### Korak 5: Određivanje Source

```csharp
// Koristi SourceDetector
var source = SourceDetector.GetSource(dossierType);
// source = "Heimdall"
```

### Korak 6: Migracija Dokumenta

```csharp
// Kreiraj ili nađi dosije
var dossier = await GetOrCreateDossier(dossierId, destinationFolder, coreId);

// Migriraj dokument sa novim atributima
await MigrateDocument(
    sourceDocument: document,
    destinationDossier: dossier,
    newName: migrationInfo.NewName,
    newCode: migrationInfo.NewCode,
    status: migrationInfo.Status,
    source: source
);
```

---

## 💡 Primeri Mapiranja

### Primer 1: Personal Notice (FL klijent)

```csharp
// INPUT
ecm:docDesc = "Personal Notice"
ecm:coreId = "13001926"
ClientAPI.Segment = "PI"

// MAPPING
Naziv = "Personal Notice"
SifraDoc = "00253"
NazivDoc = "GDPR saglasnost"
TipDosiea = "Dosije klijenta FL / PL"
SifraDocMigracija = "00849"
NazivDocMigracija = "GDPR saglasnost - migracija"

// OUTPUT
NewName = "GDPR saglasnost - migracija"
NewCode = "00849"
Status = "poništen" (NEAKTIVAN)
DestinationFolder = "DOSSIER-PI"
DossierId = "PI-13001926"
Source = "Heimdall"
```

### Primer 2: Current Accounts Contract (ACC)

```csharp
// INPUT
ecm:docDesc = "Current Accounts Contract"
ecm:coreId = "13001926"

// MAPPING
Naziv = "Current Accounts Contract"
SifraDoc = "00110"
NazivDoc = "Ugovor o tekućem računu"
TipDosiea = "Dosije paket racuna"
SifraDocMigracija = "00110"
NazivDocMigracija = "Ugovor o tekućem računu"

// OUTPUT
NewName = "Ugovor o tekućem računu"
NewCode = "00110"
Status = "validiran" (AKTIVAN)
DestinationFolder = "DOSSIER-ACC"
DossierId = "ACC-13001926"
Source = "Heimdall"
```

### Primer 3: Communication Consent (LE klijent)

```csharp
// INPUT
ecm:docDesc = "Communication Consent"
ecm:coreId = "50034220"
ClientAPI.Segment = "LE"

// MAPPING
Naziv = "Communication Consent"
SifraDoc = "00141"
NazivDoc = "Izjava o kanalima komunikacije"
TipDosiea = "Dosije klijenta FL / PL"
SifraDocMigracija = "00842"
NazivDocMigracija = "Izjava o kanalima komunikacije - migracija"

// OUTPUT
NewName = "Izjava o kanalima komunikacije - migracija"
NewCode = "00842"
Status = "poništen" (NEAKTIVAN)
DestinationFolder = "DOSSIER-LE"
DossierId = "LE-50034220"
Source = "Heimdall"
```

---

## 🛠️ Implementacija - Glavne Klase

### 1. HeimdallDocumentMapper

```csharp
// Pronalazi mapping po ecm:docDesc
var mapping = HeimdallDocumentMapper.FindByOriginalName(docDesc);

// Metodije:
- FindByOriginalName(string) → Traži po Naziv polju
- FindByOriginalCode(string) → Traži po SifraDoc polju
- GetMigratedCode(string) → Vraća SifraDocMigracija
- GetMigratedName(string) → Vraća NazivDocMigracija
- GetDossierType(string) → Vraća TipDosiea
- WillReceiveMigrationSuffix(string) → Da li ima sufiks
```

### 2. DocumentStatusDetector

```csharp
// NOVA METODA - koristi ecm:docDesc
var migrationInfo = DocumentStatusDetector.GetMigrationInfoByDocDesc(
    docDesc: "Personal Notice",
    originalCode: "00253",
    existingStatus: "validiran"
);

// Vraća DocumentMigrationInfo sa svim podacima
```

### 3. DossierTypeDetector

```csharp
// NOVA METODA - koristi ecm:docDesc
var dossierType = DossierTypeDetector.DetectFromDocDesc("Personal Notice");
// Returns: DossierType.ClientFLorPL

// Dobij folder name
var folderName = DossierTypeDetector.GetDossierFolderName(dossierType);
// Returns: "DOSSIER-PI" ili "DOSSIER-LE" (nakon razrešavanja)
```

---

## ⚠️ Važne Napomene

### 1. **ecm:docDesc je Kljuc**

- **NE koristiti ime dokumenta** za mapiranje (može biti GUID)
- **Uvek koristiti ecm:docDesc** property za identifikaciju

### 2. **TipDosiea Određuje Destination**

- TipDosiea iz liste direktno mapira u DOSSIER folder
- FL/PL tip zahteva ClientAPI poziv za razrešavanje

### 3. **Lista Nije Konačna**

- `HeimdallDocumentMapper` lista će se ažurirati
- Biće dodata nova lista za **DUTSource** (za depozitne dosijee)

### 4. **Sufiks "- migracija" Određuje Status**

- Ako `NazivDocMigracija` završava sa "- migracija" → NEAKTIVAN
- Inače → AKTIVAN

### 5. **Source Određen Tipom Dosijea**

- Heimdall: ACC, FL, PL, Other
- DUT: Deposit

---

## 🔄 Proces za FL/PL Razrešavanje

```
TipDosiea = "Dosije klijenta FL / PL"
           ↓
    ClientAPI poziv
           ↓
    Segment = "PI" | "LE"
           ↓
┌──────────┴──────────┐
│                      │
PI                    LE
↓                      ↓
DOSSIER-PI        DOSSIER-LE
PI-{CoreId}       LE-{CoreId}
```

---

## 📝 Migracija Step-by-Step

1. **Učitaj dokument** iz starog Alfresco-a
2. **Izvuci ecm:docDesc** property
3. **Pronađi mapping** u `HeimdallDocumentMapper.FindByOriginalName(docDesc)`
4. **Izvuci TipDosiea** iz mapiranja
5. **Odredi DossierType** pomoću `DossierTypeDetector.DetectFromDocDesc(docDesc)`
6. **Razreši FL/PL** (ako je potrebno) pomoću ClientAPI
7. **Formiraj DossierId** pomoću `DossierIdFormatter`
8. **Odredi Destination Folder** pomoću `GetDossierFolderName(dossierType)`
9. **Odredi Source** pomoću `SourceDetector.GetSource(dossierType)`
10. **Odredi novi naziv** iz `NazivDocMigracija`
11. **Odredi novu šifru** iz `SifraDocMigracija`
12. **Odredi status** na osnovu sufiksa "- migracija"
13. **Kreiraj ili nađi dosije** u destination folderu
14. **Migriraj dokument** sa novim atributima

---

## 🚀 Buduće Izmene

### DUT Source Lista

Biće dodata nova lista za DUT source (depozitni dosijei) koja će sadržati:
- Mapiranje za depozitne dokumente
- Dodatna pravila specifična za DUT
- Format će biti sličan `HeimdallDocumentMapper` listi

---

## 📞 Kontakt

Za pitanja kontaktirajte tim za migraciju.

**Verzija**: 2.0
**Datum**: 2025-11-05
