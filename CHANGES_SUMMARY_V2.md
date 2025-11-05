# Izmene - Mapiranje Migracije Verzija 2.0

## 📌 Datum: 2025-11-05

---

## 🎯 Glavna Promena

### ❌ STARA LOGIKA
```csharp
// Mapiranje bazirano na IMENU dokumenta
var newName = DocumentNameMapper.GetMigratedName(documentName);
```

**Problem**: Ime dokumenta može biti random GUID!

### ✅ NOVA LOGIKA
```csharp
// Mapiranje bazirano na ecm:docDesc property-ju
var migrationInfo = DocumentStatusDetector.GetMigrationInfoByDocDesc(
    docDesc: document.Properties["ecm:docDesc"]
);
```

**Rešenje**: `ecm:docDesc` sadrži vrednost iz `Naziv` ili `NazivDoc` polja iz liste.

---

## 🆕 Nove Klase i Fajlovi

### 1. **HeimdallDocumentMapper.cs** ⭐
- **Lokacija**: `Alfresco.Contracts\Mapper\HeimdallDocumentMapper.cs`
- **Svrha**: Centralna lista sa svim mapiranjima
- **Izvor**: `C:\Users\Nikola Preradov\Desktop\Migracija_Tabele\CSV\HeimdallDis.csv`
- **Format**:
  ```csharp
  List<(
      string Naziv,              // Engleski naziv
      string SifraDoc,           // Originalna šifra
      string NazivDoc,           // Srpski naziv
      string TipDosiea,          // Tip dosijea
      string SifraDocMigracija,  // Nova šifra
      string NazivDocMigracija   // Novi naziv
  )>
  ```
- **Broj stavki**: 40 dokumenata

### 2. **MIGRATION_RULES_V2.md** 📄
- **Lokacija**: Root projekta
- **Svrha**: Detaljna dokumentacija pravila migracije
- **Sadržaj**:
  - Proces mapiranja
  - Pravila za status
  - Pravila za dosijee
  - Primeri
  - Step-by-step guide

### 3. **README.md** 📄
- **Lokacija**: `Alfresco.Contracts\Mapper\README.md`
- **Svrha**: Brzi vodič za korišćenje mappera
- **Sadržaj**:
  - Pregled promena
  - Opis klasa
  - Tok korišćenja
  - Primeri

---

## ♻️ Refaktorisane Klase

### 1. **DocumentCodeMapper.cs**
**Promene**:
- Uklonjen hardkodiran Dictionary
- Sada je wrapper oko `HeimdallDocumentMapper`
- Sva logika prebačena u centralnu listu

**Pre**:
```csharp
private static readonly Dictionary<string, string> CodeMappings = new() { ... };
```

**Posle**:
```csharp
public static string GetMigratedCode(string originalCode)
{
    return HeimdallDocumentMapper.GetMigratedCode(originalCode);
}
```

### 2. **DocumentNameMapper.cs**
**Promene**:
- Uklonjen hardkodiran Dictionary
- Sada je wrapper oko `HeimdallDocumentMapper`
- Dodate nove metode:
  - `GetSerbianName()` - vraća srpski naziv
  - `GetDossierType()` - vraća tip dosijea

**Pre**:
```csharp
private static readonly Dictionary<string, string> NameMappings = new() { ... };
```

**Posle**:
```csharp
public static string GetMigratedName(string originalName)
{
    return HeimdallDocumentMapper.GetMigratedName(originalName);
}
```

---

## ✨ Proširene Klase

### 1. **DocumentStatusDetector.cs**
**Nova metoda**:
```csharp
public static DocumentMigrationInfo GetMigrationInfoByDocDesc(
    string docDesc,
    string? originalCode = null,
    string? existingStatus = null
)
```

**Novo polje u `DocumentMigrationInfo`**:
```csharp
public string TipDosiea { get; init; } = string.Empty;
```

**Šta radi**:
1. Uzima `ecm:docDesc` kao input
2. Pronalazi mapping u listi (po Naziv ili NazivDoc)
3. Vraća kompletne informacije o migraciji:
   - Novi naziv (NazivDocMigracija)
   - Nova šifra (SifraDocMigracija)
   - Status (aktivan/neaktivan na osnovu sufiksa)
   - Tip dosijea (TipDosiea)

### 2. **DossierTypeDetector.cs**
**Nova metoda**:
```csharp
public static DossierType DetectFromDocDesc(string docDesc)
```

**Šta radi**:
- Uzima `ecm:docDesc` kao input
- Pronalazi mapping u listi
- Vraća DossierType na osnovu TipDosiea

**Nova metoda**:
```csharp
public static string GetDossierFolderName(DossierType dossierType)
```

**Vraća**:
- "DOSSIER-ACC" za AccountPackage
- "DOSSIER-PI" za ClientFL
- "DOSSIER-LE" za ClientPL
- "DOSSIER-D" za Deposit

---

## 🔄 Novi Tok Mapiranja

### Proces:

```
1. Dokument iz starog Alfresco-a
   ↓
2. Izvuci ecm:docDesc property
   ↓
3. HeimdallDocumentMapper.FindByOriginalName(docDesc)
   ↓
4. Dobij mapping:
   - Naziv
   - SifraDoc
   - NazivDoc
   - TipDosiea
   - SifraDocMigracija
   - NazivDocMigracija
   ↓
5. Proveri sufiks "- migracija"
   ↓
6. Odredi status (aktivan/neaktivan)
   ↓
7. Odredi destination folder iz TipDosiea
   ↓
8. Migriraj dokument sa novim atributima
```

---

## 📊 Mapiranje: TipDosiea → Destination Folder

| TipDosiea iz Liste | DossierType | Destination Folder | Dossier ID |
|-------------------|-------------|-------------------|------------|
| "Dosije paket racuna" | AccountPackage | DOSSIER-ACC | ACC-{CoreId} |
| "Dosije klijenta FL / PL" | ClientFLorPL | DOSSIER-PI ili DOSSIER-LE* | PI-{CoreId} ili LE-{CoreId} |
| "Dosije klijenta PL" | ClientPL | DOSSIER-LE | LE-{CoreId} |
| "Dosije depozita" | Deposit | DOSSIER-D | DE-{CoreId}-{SifraTipaProizvoda}-{brojUgovora} |

*Zahteva ClientAPI poziv za razrešavanje

---

## 📝 Primer: Pre i Posle

### STARA LOGIKA (Pre)
```csharp
// Problem: Ime dokumenta je GUID
string documentName = "a5f3c2d1-4e9a-4b6c-8d1e-2f3a4b5c6d7e.pdf";
var newName = DocumentNameMapper.GetMigratedName(documentName);
// Result: "a5f3c2d1-4e9a-4b6c-8d1e-2f3a4b5c6d7e.pdf" (ne radi!)
```

### NOVA LOGIKA (Posle)
```csharp
// Rešenje: Koristimo ecm:docDesc
string docDesc = document.Properties["ecm:docDesc"]; // "Personal Notice"

var migrationInfo = DocumentStatusDetector.GetMigrationInfoByDocDesc(
    docDesc: docDesc,
    originalCode: "00253"
);

// Result:
// NewName: "GDPR saglasnost - migracija"
// NewCode: "00849"
// Status: "poništen"
// TipDosiea: "Dosije klijenta FL / PL"
// DestinationFolder: "DOSSIER-PI" ili "DOSSIER-LE"
```

---

## ⚠️ Breaking Changes

### 1. **Dodato novo polje u `DocumentMigrationInfo`**
```csharp
public string TipDosiea { get; init; } = string.Empty;
```

**Akcija**: Kod koji koristi `DocumentMigrationInfo` treba da se ažurira ako koristi dekonstrukciju ili pattern matching.

### 2. **Nova metoda `GetDossierFolderName()` u `DossierTypeDetector`**
```csharp
GetDossierFolderName(DossierType) → string
```

**Akcija**: Zameniti staru logiku sa ovom metodom za dobijanje folder imena.

---

## ✅ Testovi

### Test Scenarios:

1. **Mapiranje po engleskom nazivu**
   ```csharp
   docDesc = "Personal Notice"
   → Mapping pronađen po Naziv polju
   ```

2. **Mapiranje po srpskom nazivu**
   ```csharp
   docDesc = "GDPR saglasnost"
   → Mapping pronađen po NazivDoc polju
   ```

3. **Sufiks "- migracija"**
   ```csharp
   NazivDocMigracija = "GDPR saglasnost - migracija"
   → Status = "poništen" (NEAKTIVAN)
   ```

4. **Bez sufiksa**
   ```csharp
   NazivDocMigracija = "Ugovor o tekućem računu"
   → Status = "validiran" (AKTIVAN)
   ```

5. **TipDosiea mapiranje**
   ```csharp
   TipDosiea = "Dosije paket racuna"
   → DossierType = AccountPackage
   → DestinationFolder = "DOSSIER-ACC"
   ```

---

## 🔮 Buduće Izmene

### Planirana Implementacija

1. **DUT Source Lista**
   - Nova lista za depozitne dosijee
   - Format sličan `HeimdallDocumentMapper`
   - Dodatna pravila za DUT

2. **Ažuriranje Postojeće Liste**
   - `HeimdallDocumentMapper` lista će se proširivati
   - Nove stavke će biti dodavane iz CSV fajlova

---

## 🗂️ Struktura Fajlova

```
Alfresco/
├── Alfresco.Contracts/
│   └── Mapper/
│       ├── HeimdallDocumentMapper.cs       ⭐ NOVA
│       ├── DocumentCodeMapper.cs           ♻️ REFAKTORISAN
│       ├── DocumentNameMapper.cs           ♻️ REFAKTORISAN
│       ├── DocumentStatusDetector.cs       ✨ PROŠIRENA
│       ├── DossierTypeDetector.cs          ✨ PROŠIRENA
│       ├── SourceDetector.cs               ✅ BEZ PROMENA
│       ├── DossierIdFormatter.cs           ✅ BEZ PROMENA
│       ├── OpisToTipMapper.cs              ✅ BEZ PROMENA
│       ├── DestinationRootFolderDeterminator.cs ✅ BEZ PROMENA
│       └── README.md                       📄 NOVA
├── MIGRATION_RULES_V2.md                   📄 NOVA
└── CHANGES_SUMMARY_V2.md                   📄 NOVA (ovaj fajl)
```

---

## 📞 Kontakt

Za pitanja kontaktirajte tim za migraciju.

---

**Verzija**: 2.0
**Datum**: 2025-11-05
**Status**: ✅ Implementirano
