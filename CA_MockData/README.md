# CA_MockData - Alfresco Mock Data Generator

## Overview

CA_MockData je konzolna aplikacija za generisanje test podataka u Alfresco Content Management sistemu. Podržava dva moda rada:
1. **Stari mod** - Kreira flat folder strukturu: `MockFolders-000001`, `MockFolders-000002`, itd.
2. **Novi mod** - Kreira hijerarhijsku strukturu: `dosie-PL/PL10000000`, `dosie-FL/FL10000001`, itd.

## Nova Folder Struktura

Kada je `UseNewFolderStructure = true`, aplikacija kreira sledeću hijerarhiju:

```
ROOT (RootParentId)
├── dosie-PL/
│   ├── PL10000000/
│   │   ├── Doc_PL10000000_000.pdf
│   │   ├── Doc_PL10000000_001.pdf
│   │   └── Doc_PL10000000_002.pdf
│   ├── PL10000003/
│   │   └── [dokumenti]
│   └── PL10000006/
│       └── [dokumenti]
├── dosie-FL/
│   ├── FL10000001/
│   │   └── [dokumenti]
│   ├── FL10000004/
│   │   └── [dokumenti]
│   └── FL10000007/
│       └── [dokumenti]
└── dosie-ACC/
    ├── ACC10000002/
    │   └── [dokumenti]
    ├── ACC10000005/
    │   └── [dokumenti]
    └── ACC10000008/
        └── [dokumenti]
```

### Kako Funkcioniše Distribucija

Folderi se distribuiraju ravnomerno između tipova klijenata:
- **Folder 0** → `dosie-PL/PL10000000`
- **Folder 1** → `dosie-FL/FL10000001`
- **Folder 2** → `dosie-ACC/ACC10000002`
- **Folder 3** → `dosie-PL/PL10000003`
- **Folder 4** → `dosie-FL/FL10000004`
- ... i tako dalje

Formula: `ClientType = ClientTypes[i % ClientTypes.Length]`

## Konfiguracija

### Config Properties

```csharp
var cfg = new Config()
{
    // Alfresco Connection
    BaseUrl = "http://localhost:8080/",
    Username = "admin",
    Password = "admin",
    RootParentId = "8ccc0f18-5445-4358-8c0f-185445235836",

    // Data Generation
    FolderCount = 10000,        // Ukupan broj foldera za kreiranje
    DocsPerFolder = 3,          // Broj dokumenata po folderu

    // Performance
    DegreeOfParallelism = 8,    // Broj paralelnih worker-a
    MaxRetries = 5,             // Maksimalan broj pokušaja za failed requests
    RetryBaseDelayMs = 100,     // Bazni delay za exponential backoff

    // New Structure Settings (optional)
    UseNewFolderStructure = true,                       // Enable/disable nova struktura
    ClientTypes = new[] { "PL", "FL", "ACC" },         // Tipovi klijenata
    StartingCoreId = 10000000,                         // Početni CoreId
    AddFolderProperties = true                         // Enable custom properties/metadata
};
```

### Stara vs Nova Struktura

#### Stara Struktura (`UseNewFolderStructure = false`)
```
ROOT/
├── MockFolders-000000/
│   ├── MockDoc_000000_000
│   ├── MockDoc_000000_001
│   └── MockDoc_000000_002
├── MockFolders-000001/
│   └── [dokumenti]
└── MockFolders-000002/
    └── [dokumenti]
```

#### Nova Struktura (`UseNewFolderStructure = true`)
```
ROOT/
├── dosie-PL/
│   ├── PL10000000/
│   │   ├── Doc_PL10000000_000.pdf
│   │   ├── Doc_PL10000000_001.pdf
│   │   └── Doc_PL10000000_002.pdf
│   └── ...
├── dosie-FL/
│   └── ...
└── dosie-ACC/
    └── ...
```

## Kako Koristiti

### 1. Konfiguriši Parametre

Otvori `Program.cs` i ažuriraj `Config` objekat:

```csharp
var cfg = new Config()
{
    BaseUrl = "http://localhost:8080/",     // Tvoj Alfresco server
    Username = "admin",                      // Alfresco username
    Password = "admin",                      // Alfresco password
    RootParentId = "your-root-folder-id",   // ROOT folder ID u Alfresku
    FolderCount = 1000,                     // Broj foldera
    DocsPerFolder = 3,                      // Dokumenti po folderu
    UseNewFolderStructure = true            // Koristi novu strukturu
};
```

### 2. Pokreni Aplikaciju

```bash
# Build projekta
dotnet build

# Pokreni aplikaciju
dotnet run --project CA_MockData
```

### 3. Prati Progres

Aplikacija prikazuje progres tokom izvršavanja:

```
Creating dosie folder structure...
Dosie folder created: dosie-PL (ID: abc-123-def)
Dosie folder created: dosie-FL (ID: xyz-456-uvw)
Dosie folder created: dosie-ACC (ID: qwe-789-rty)
All dosie folders ready. Starting client folder creation...

Progress | Folders 200/10000 | Docs 600/30000 | Failed 0 | Elapsed 00:00:45
Progress | Folders 400/10000 | Docs 1200/30000 | Failed 0 | Elapsed 00:01:30
...
DONE | Folders 10000/10000 | Docs 30000/30000 | Failed 0 | Time 00:15:23
```

## Parametri koji se Mogu Prilagoditi

### Client Types

Možeš definisati bilo koje tipove klijenata:

```csharp
// Samo PL i FL
ClientTypes = new[] { "PL", "FL" }

// Samo PL
ClientTypes = new[] { "PL" }

// Svi tipovi
ClientTypes = new[] { "PL", "FL", "ACC" }
```

### Starting CoreId

Početni CoreId za generisanje mock podataka:

```csharp
// Počni od 10000000
StartingCoreId = 10000000  // → PL10000000, FL10000001, ...

// Počni od 20000000
StartingCoreId = 20000000  // → PL20000000, FL20000001, ...
```

### Degree of Parallelism

Broj paralelnih worker-a koji kreiraju foldere i dokumente:

```csharp
// Visok throughput (više CPU)
DegreeOfParallelism = 16

// Srednji throughput
DegreeOfParallelism = 8

// Niski throughput (manje opterećenje servera)
DegreeOfParallelism = 4
```

## Napredne Funkcionalnosti

### Retry Logic

Aplikacija automatski retry-uje failed requests sa exponential backoff:

```csharp
MaxRetries = 5              // Maksimalno 5 pokušaja
RetryBaseDelayMs = 100      // Početni delay 100ms

// Delay schedule:
// Attempt 1: 100ms
// Attempt 2: 200ms
// Attempt 3: 400ms
// Attempt 4: 800ms
// Attempt 5: 1600ms
```

### Cancellation Support

Pritisni `Ctrl+C` za graceful shutdown:

```
^C
[Stopping workers...]
DONE | Folders 5432/10000 | Docs 16296/30000 | Failed 0 | Time 00:08:12
```

### Get or Create Dosie Folders

Aplikacija koristi `GetOrCreateFolderAsync` za dosie foldere:
- Ako folder već postoji, koristi postojeći
- Ako ne postoji, kreira novi
- Omogućava ponovno pokretanje bez greški

## Testiranje

### 1. Testiraj sa Malim Brojem Foldera

```csharp
FolderCount = 10,
DocsPerFolder = 2,
UseNewFolderStructure = true
```

Ovo će kreirati:
- 3 dosie foldera (PL, FL, ACC)
- 10 client foldera (distribuirano)
- 20 dokumenata (10 foldera × 2 doc/folder)

### 2. Proveri Strukturu u Alfresku

Logiraj se u Alfresco i proveri:
- Da li postoje `dosie-PL`, `dosie-FL`, `dosie-ACC` folderi
- Da li svaki dosie folder sadrži client foldere (`PL10000000`, `FL10000001`, itd.)
- Da li svaki client folder sadrži dokumente

### 3. Testiraj Performance

```csharp
FolderCount = 10000,
DocsPerFolder = 3,
DegreeOfParallelism = 8
```

Prati koliko vremena treba za kreiranje 10,000 foldera sa 30,000 dokumenata.

## Troubleshooting

### Problem: "Failed to create dosie folder"

**Uzrok:** Neispravan `RootParentId` ili nedovoljna prava

**Rešenje:**
- Proveri da li `RootParentId` postoji u Alfresku
- Proveri da li korisnik ima write permissions na ROOT folder

### Problem: "HTTP 429 Too Many Requests"

**Uzrok:** Previše zahteva ka Alfresco serveru

**Rešenje:**
```csharp
DegreeOfParallelism = 4,    // Smanji broj worker-a
MaxRetries = 10,            // Povećaj broj retry-a
RetryBaseDelayMs = 200      // Povećaj delay
```

### Problem: "HTTP 401 Unauthorized"

**Uzrok:** Neispravni credentials

**Rešenje:**
```csharp
Username = "correct-username",
Password = "correct-password"
```

## Primeri Korišćenja

### Primer 1: Kreiraj 1000 PL foldera

```csharp
var cfg = new Config()
{
    // ... connection settings ...
    FolderCount = 1000,
    DocsPerFolder = 5,
    UseNewFolderStructure = true,
    ClientTypes = new[] { "PL" },           // Samo PL
    StartingCoreId = 10000000
};
```

Rezultat: `dosie-PL/PL10000000`, `dosie-PL/PL10000001`, ..., `dosie-PL/PL10000999`

### Primer 2: Kreiraj 300 foldera ravnomerno distribuiranih

```csharp
var cfg = new Config()
{
    // ... connection settings ...
    FolderCount = 300,
    DocsPerFolder = 3,
    UseNewFolderStructure = true,
    ClientTypes = new[] { "PL", "FL", "ACC" },
    StartingCoreId = 20000000
};
```

Rezultat:
- 100 PL foldera: `PL20000000`, `PL20000003`, `PL20000006`, ...
- 100 FL foldera: `FL20000001`, `FL20000004`, `FL20000007`, ...
- 100 ACC foldera: `ACC20000002`, `ACC20000005`, `ACC20000008`, ...

### Primer 3: Stara struktura (kompatibilnost)

```csharp
var cfg = new Config()
{
    // ... connection settings ...
    FolderCount = 5000,
    DocsPerFolder = 3,
    UseNewFolderStructure = false  // Koristi staru strukturu
};
```

Rezultat: `MockFolders-000000`, `MockFolders-000001`, ..., `MockFolders-004999`

## Integration sa Migration Sistemom

Ova aplikacija kreira identičnu strukturu kao Migration sistem:

| Migration Sistem | CA_MockData |
|-----------------|-------------|
| `IFolderPathService.GenerateFolderPath("PL", "10101010")` | `dosie-PL/PL10101010` |
| `IFolderManager.EnsureFolderStructureAsync("FL", "20202020")` | `dosie-FL/FL20202020` |
| Client Folder Format | `{ClientType}{CoreId}` (no dash) |

## Performance Tips

1. **Povećaj DegreeOfParallelism** za brže kreiranje (ako server može da izdrži)
2. **Smanji DocsPerFolder** za brže testiranje strukture foldera
3. **Koristi SSD** za brže generisanje dokumenata
4. **Proveri network latency** - lokalni Alfresco je mnogo brži

## Custom Properties (Metadata)

Aplikacija sada podržava dodavanje custom properties (metadata) na foldere!

### Omogući Properties

```csharp
var cfg = new Config()
{
    // ... other settings ...
    AddFolderProperties = true  // Enable properties
};
```

### Built-in Properties (Rade Odmah)

Bez dodatne konfiguracije, automatski se dodaju:
- `cm:title` - "PL Client 10000000"
- `cm:description` - "Mock folder for PL client with CoreId 10000000"

### Custom Properties (Zahtevaju Content Model)

Za custom properties, prvo moraš definisati **Content Model** u Alfrescos:

```csharp
// U GenerateFolderProperties metodi, odkomentiraj:
properties["myapp:coreId"] = coreId.ToString();
properties["myapp:clientType"] = clientType;
properties["myapp:createdDate"] = DateTime.UtcNow.ToString("o");
```

**📖 Za detaljne instrukcije:** Pogledaj `PROPERTIES_GUIDE.md`

### Verifikacija

Proveri properties u Alfresco Share:
1. Navigate do foldera (npr. `dosie-PL/PL10000000`)
2. Klikni "View Details" ili "Edit Properties"
3. Vidi `cm:title`, `cm:description`, i custom properties

## Napomene

- ✅ Thread-safe paralelna obrada
- ✅ Automatic retry sa exponential backoff
- ✅ Graceful shutdown (Ctrl+C)
- ✅ Get-or-Create logic za dosie foldere (idempotent)
- ✅ Progress tracking svake 200 foldere
- ✅ Kompatibilnost sa starom strukturom
- ✅ Custom properties support (metadata)

## Dokumentacija

- **README.md** - Ovaj fajl (glavni guide)
- **PROPERTIES_GUIDE.md** - Detaljan guide za custom properties

---

**Datum:** 2025-10-20
**Status:** Ready for Use
**Build Status:** ✅ Successful
**Features:** Folder Structure + Custom Properties
