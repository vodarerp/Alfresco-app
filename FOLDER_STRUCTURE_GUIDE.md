# Folder Structure Guide

## Overview

Ovaj dokument opisuje novu strukturu foldera za čuvanje dokumenata u migracionom sistemu.

## Nova Struktura Foldera

```
ROOT/
├── dosie-PL/
│   ├── PL10101010/
│   │   └── [dokumenti]
│   ├── PL10101011/
│   │   └── [dokumenti]
│   └── PL10101012/
│       └── [dokumenti]
├── dosie-FL/
│   ├── FL20202020/
│   │   └── [dokumenti]
│   └── FL20202021/
│       └── [dokumenti]
└── dosie-ACC/
    ├── ACC30303030/
    │   └── [dokumenti]
    └── ACC30303031/
        └── [dokumenti]
```

### Komponente Strukture

1. **ROOT Folder** - Glavna lokacija za sve dokumente
   - Konfiguriše se u `appsettings.json` pod `Migration:RootDocumentPath`
   - Primer: `C:\DocumentsRoot` ili `/mnt/documents`

2. **Dosie Folder** - Folder po tipu klijenta
   - Format: `dosie-{ClientType}`
   - Primeri:
     - `dosie-PL` - Pravna Lica (Legal Entities)
     - `dosie-FL` - Fizička Lica (Natural Persons)
     - `dosie-ACC` - Računi (Accounts)

3. **Client Folder** - Folder specifičan za klijenta
   - Format: `{ClientType}{CoreId}`
   - Primeri:
     - `PL10101010` - Pravno lice sa CoreId = 10101010
     - `FL20202020` - Fizičko lice sa CoreId = 20202020
     - `ACC30303030` - Račun sa CoreId = 30303030

## Promena od Stare Strukture

### Stara Struktura
```
ROOT/
└── dosie-PL/
    └── PL-10101010/  ← sa crticom
        └── [dokumenti]
```

### Nova Struktura
```
ROOT/
└── dosie-PL/
    └── PL10101010/   ← bez crtice
        └── [dokumenti]
```

**Glavne Razlike:**
- Uklonjena crtica (`-`) između tipa klijenta i CoreId
- Format: `PL-10101010` → `PL10101010`

## Konfigurisanje ROOT Putanje

### 1. U appsettings.json

```json
{
  "Migration": {
    "RootDocumentPath": "C:\\DocumentsRoot"
  }
}
```

**NAPOMENA:** Za Windows putanje koristite dvostruke backslash-eve (`\\`)

### 2. Preko Environment Variable (Preporučeno za Production)

```bash
# Linux/Mac
export Migration__RootDocumentPath="/mnt/documents"

# Windows PowerShell
$env:Migration__RootDocumentPath = "C:\DocumentsRoot"

# Windows CMD
set Migration__RootDocumentPath=C:\DocumentsRoot
```

### 3. Preko Docker Environment Variable

```yaml
environment:
  - Migration__RootDocumentPath=/mnt/documents
```

## Korišćenje Servisa

### IFolderPathService

Servis za generisanje relativnih putanja foldera.

```csharp
public interface IFolderPathService
{
    // Generiše kompletnu relativnu putanju
    string GenerateFolderPath(string clientType, string coreId);
    // Primer: "dosie-PL/PL10101010"

    // Generiše dosie folder naziv
    string GenerateDosieFolder(string clientType);
    // Primer: "dosie-PL"

    // Generiše client folder naziv
    string GenerateClientFolder(string clientType, string coreId);
    // Primer: "PL10101010"

    // Validira tip klijenta
    bool IsValidClientType(string clientType);

    // Parse-uje putanju nazad u komponente
    (string ClientType, string CoreId) ParseFolderPath(string folderPath);
}
```

**Primer Korišćenja:**

```csharp
public class MyService
{
    private readonly IFolderPathService _pathService;

    public MyService(IFolderPathService pathService)
    {
        _pathService = pathService;
    }

    public void Example()
    {
        // Generiši putanju
        var path = _pathService.GenerateFolderPath("PL", "10101010");
        // Rezultat: "dosie-PL/PL10101010"

        // Parse putanju
        var (clientType, coreId) = _pathService.ParseFolderPath("dosie-PL/PL10101010");
        // clientType = "PL", coreId = "10101010"
    }
}
```

### IFolderManager

Servis za kreiranje i upravljanje fizičkim folderima na file sistemu.

```csharp
public interface IFolderManager
{
    // Kreira strukturu foldera ako ne postoji
    Task<string> EnsureFolderStructureAsync(string clientType, string coreId, CancellationToken ct);

    // Proverava da li struktura postoji
    bool FolderStructureExists(string clientType, string coreId);

    // Vraća kompletnu fizičku putanju (bez kreiranja)
    string GetClientFolderPath(string clientType, string coreId);
}
```

**Primer Korišćenja:**

```csharp
public class DocumentService
{
    private readonly IFolderManager _folderManager;

    public DocumentService(IFolderManager folderManager)
    {
        _folderManager = folderManager;
    }

    public async Task ProcessDocumentAsync(string clientType, string coreId, CancellationToken ct)
    {
        // Osiguraj da folder struktura postoji
        var folderPath = await _folderManager.EnsureFolderStructureAsync(clientType, coreId, ct);
        // folderPath = "C:\DocumentsRoot\dosie-PL\PL10101010"

        // Sada možeš koristiti folderPath za kreiranje fajlova
        var documentPath = Path.Combine(folderPath, "document.pdf");
        // documentPath = "C:\DocumentsRoot\dosie-PL\PL10101010\document.pdf"
    }
}
```

## Validacija

### Validni Tipovi Klijenata

- `PL` - Pravno Lice (Legal Entity)
- `FL` - Fizičko Lice (Natural Person)
- `ACC` - Račun (Account)

**Napomena:** Tipovi se automatski konvertuju u uppercase, tako da `pl`, `Pl`, i `PL` su svi validni.

### Validacija CoreId

- Mora biti numerički
- Ne sme biti prazan
- Primeri validnih: `10101010`, `123456`, `999999999`
- Primeri nevalidnih: `ABC123`, ``, `null`

## Error Handling

Svi servisi bacaju `ArgumentException` za nevažeće parametre:

```csharp
try
{
    var path = _pathService.GenerateFolderPath("INVALID", "123");
}
catch (ArgumentException ex)
{
    // "Invalid client type: INVALID. Must be 'PL', 'FL', or 'ACC'"
}
```

`FolderManager` baca `InvalidOperationException` za greške pri kreiranju foldera:

```csharp
try
{
    var path = await _folderManager.EnsureFolderStructureAsync("PL", "123", ct);
}
catch (InvalidOperationException ex)
{
    // "Failed to create folder structure for PL client 123"
}
```

## Migration Checklist

Kada migriraš sa stare na novu strukturu:

- [ ] Ažuriraj `appsettings.json` sa `Migration:RootDocumentPath`
- [ ] Postavi environment variable za production (ako koristiš)
- [ ] Ažuriraj sve servise koji kreiraju foldere da koriste `IFolderManager`
- [ ] Testiraj kreiranje foldera za sve tipove klijenata (PL, FL, ACC)
- [ ] Verificiraj da se folderi kreiraju bez crtice (`PL10101010` umesto `PL-10101010`)
- [ ] Dokumentuj novi proces u vašem deployment procesu

## Primeri Kompletnih Putanja

### Windows
```
C:\DocumentsRoot\dosie-PL\PL10101010\document.pdf
C:\DocumentsRoot\dosie-FL\FL20202020\statement.pdf
C:\DocumentsRoot\dosie-ACC\ACC30303030\report.pdf
```

### Linux/Mac
```
/mnt/documents/dosie-PL/PL10101010/document.pdf
/mnt/documents/dosie-FL/FL20202020/statement.pdf
/mnt/documents/dosie-ACC/ACC30303030/report.pdf
```

## Integracija sa DocumentResolver

`DocumentResolver` je ažuriran da koristi `IFolderManager` za kreiranje Alfresco foldera sa istim principom:

```csharp
public class DocumentResolver : IDocumentResolver
{
    private readonly IFolderManager _folderManager;

    public async Task<string> ResolveAsync(
        string destinationRootId,
        string newFolderName,
        CancellationToken ct)
    {
        // Kreira folder u Alfrescos sa novim formatom
        // newFolderName bi bio npr. "PL10101010" umesto "PL-10101010"
    }
}
```

## Dodatne Napomene

1. **Thread Safety**: `FolderPathService` i `FolderManager` su thread-safe i registrovani kao Singleton
2. **Performance**: Directory.CreateDirectory automatski kreira sve parent foldere
3. **Permissions**: Osiguraj da aplikacija ima write permissions na ROOT putanju
4. **Logging**: Svi servisi loguju operacije - proveri logove za detalje

## Podrška

Za pitanja ili probleme, konsultuj:
- INTEGRATION_INSTRUCTIONS.md - za setup upute
- IMPLEMENTATION_SUMMARY.md - za pregled implementacije
- REFACTORING_PLAN_ClientAPI_Integration.md - za planove budućih izmena
