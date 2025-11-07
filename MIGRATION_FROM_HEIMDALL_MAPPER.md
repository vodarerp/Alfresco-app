# Migracija sa HeimdallDocumentMapper na DocumentMappings tabelu

## Pregled

`HeimdallDocumentMapper` je bio statička klasa sa hard-coded mapiranjima dokumenata. Sada je zamenjen sa **database-driven pristupom** gde se sva mapiranja čitaju iz SQL Server tabele `DocumentMappings`.

## Arhitektura layera

```
┌─────────────────────────────────────────┐
│   Migration.Infrastructure              │  ← OpisToTipMapperV2, DocumentStatusDetectorV2
│   (Implementation Layer)                │     DocumentMappingService
└─────────────────┬───────────────────────┘
                  │ implements
┌─────────────────▼───────────────────────┐
│   Migration.Abstraction                 │  ← IDocumentMappingService
│   (Interface Layer)                     │
└─────────────────┬───────────────────────┘
                  │ references
┌─────────────────▼───────────────────────┐
│   SqlServer.Infrastructure              │  ← DocumentMappingRepository
│   (Data Access Implementation)          │
└─────────────────┬───────────────────────┘
                  │ implements
┌─────────────────▼───────────────────────┐
│   SqlServer.Abstraction                 │  ← IDocumentMappingRepository
│   (Data Access Interface)               │
└─────────────────┬───────────────────────┘
                  │ references
┌─────────────────▼───────────────────────┐
│   Alfresco.Contracts                    │  ← DocumentMapping (Entity Model)
│   (Models/Contracts - Lowest Layer)    │     NO DEPENDENCIES!
└─────────────────────────────────────────┘
```

**VAŽNO**: `Alfresco.Contracts` je najniži layer i **NE SME** da ima reference na druge projekte!

## Struktura tabele DocumentMappings

```sql
CREATE TABLE [dbo].[DocumentMappings](
    [ID] [int] IDENTITY(1,1) NOT NULL,
    [NAZIV] [varchar](500) NULL,
    [BROJ_DOKUMENATA] [int] NULL,
    [sifraDokumenta] [varchar](200) NULL,
    [NazivDokumenta] [varchar](500) NULL,
    [TipDosijea] [varchar](200) NULL,
    [TipProizvoda] [varchar](200) NULL,
    [sifraDokumenta_migracija] [varchar](200) NULL,
    [NazivDokumenta_migracija] [varchar](500) NULL,
    [ExcelFileName] [varchar](300) NULL,
    [ExcelFileSheet] [varchar](300) NULL)
```

## Nove klase

### 1. **DocumentMapping** (Entity Model)
- **Lokacija**: `Alfresco.Contracts\Oracle\Models\DocumentMapping.cs`
- **Opis**: Entity model za DocumentMappings tabelu

### 2. **IDocumentMappingRepository** (Repository Interface)
- **Lokacija**: `SqlServer.Abstraction\Interfaces\IDocumentMappingRepository.cs`
- **Metode**:
  - `FindByOriginalNameAsync(string originalName)` - Pronalazi po NAZIV polju
  - `FindByOriginalCodeAsync(string originalCode)` - Pronalazi po sifraDokumenta polju
  - `FindBySerbianNameAsync(string serbianName)` - Pronalazi po NazivDokumenta polju
  - `FindByMigratedNameAsync(string migratedName)` - Pronalazi po NazivDokumenta_migracija polju
  - `GetAllMappingsAsync()` - Vraća sva mapiranja (keširano)

### 3. **DocumentMappingRepository** (Repository Implementation)
- **Lokacija**: `SqlServer.Infrastructure\Implementation\DocumentMappingRepository.cs`
- **Opis**: Implementacija sa SQL indeksima i selektivnim keširanje
- **Optimizacije**:
  - Direktni SQL upiti sa WHERE klauzulama (NE LINQ kroz 70,000 zapisa!)
  - SQL covering indeksi za ultra-brze pretrage (5-10ms)
  - Per-item caching (keširaju se samo traženi zapisi, ne cela tabela)
  - NOLOCK hint za bolje performanse i konkurentnost
  - TTL: 30 minuta za pojedinačne cache entries

### 4. **IDocumentMappingService** (Service Interface)
- **Lokacija**: `Migration.Abstraction\Interfaces\IDocumentMappingService.cs`
- **Metode** (identične starom HeimdallDocumentMapper-u):
  - `FindByOriginalNameAsync()`
  - `FindByOriginalCodeAsync()`
  - `WillReceiveMigrationSuffixAsync()`
  - `CodeWillChangeAsync()`
  - `GetMigratedCodeAsync()`
  - `GetMigratedNameAsync()`
  - `GetDossierTypeAsync()`
  - `GetSerbianNameAsync()`
  - `GetAllMappingsAsync()`

### 5. **DocumentMappingService** (Service Implementation)
- **Lokacija**: `Migration.Infrastructure\Implementation\DocumentMappingService.cs`
- **Opis**: Business logic sloj koji koristi repository

### 6. **OpisToTipMapperV2** (Novi mapper)
- **Lokacija**: `Migration.Infrastructure\Implementation\OpisToTipMapperV2.cs`
- **Opis**: Zamenjuje `OpisToTipMapper`, koristi `IDocumentMappingService`
- **Razlika**: Instancirana klasa umesto statičke, asinhroni API
- **Namespace**: `Migration.Infrastructure.Implementation`

### 7. **DocumentStatusDetectorV2** (Novi detektor)
- **Lokacija**: `Migration.Infrastructure\Implementation\DocumentStatusDetectorV2.cs`
- **Opis**: Zamenjuje `DocumentStatusDetector`, koristi `IDocumentMappingService`
- **Razlika**: Instancirana klasa umesto statičke, asinhroni API
- **Namespace**: `Migration.Infrastructure.Implementation`

## Dependency Injection Setup

Dodaj sledeće u `Program.cs` ili `Startup.cs`:

```csharp
// Repository
services.AddScoped<IDocumentMappingRepository, DocumentMappingRepository>();

// Service
services.AddScoped<IDocumentMappingService, DocumentMappingService>();

// Mappers
services.AddScoped<OpisToTipMapperV2>();
services.AddScoped<DocumentStatusDetectorV2>();

// Memory Cache (ako već ne postoji)
services.AddMemoryCache();
```

## SQL Indeksi - OBAVEZNO!

**VAŽNO**: Pre korišćenja repository-ja, **MORA** se pokrenuti SQL skripta za kreiranje indeksa:

```bash
SQL_Scripts/CREATE_DOCUMENTMAPPINGS_INDEXES.sql
```

Bez ovih indeksa, pretrage će biti **VEOMA SPORE** (100-1000x sporije)!

Ova skripta kreira 4 **covering indeksa** za optimalne performanse:
- `IX_DocumentMappings_NAZIV` - za FindByOriginalNameAsync()
- `IX_DocumentMappings_sifraDokumenta` - za FindByOriginalCodeAsync()
- `IX_DocumentMappings_NazivDokumenta` - za FindBySerbianNameAsync()
- `IX_DocumentMappings_NazivDokumenta_migracija` - za FindByMigratedNameAsync()

Za detaljnije informacije o performance optimizacijama, pogledaj: `PERFORMANCE_OPTIMIZATION.md`

## Primer upotrebe

### Stari način (HeimdallDocumentMapper):

```csharp
// Statički API - sinhroni
var mapping = HeimdallDocumentMapper.FindByOriginalName("Personal Notice");
var code = HeimdallDocumentMapper.GetMigratedCode("00253");
var willReceiveSuffix = HeimdallDocumentMapper.WillReceiveMigrationSuffix("Personal Notice");
```

### Novi način (DocumentMappingService):

```csharp
// Dependency Injection - asinhroni
public class MyService
{
    private readonly IDocumentMappingService _mappingService;

    public MyService(IDocumentMappingService mappingService)
    {
        _mappingService = mappingService;
    }

    public async Task ProcessDocumentAsync(string documentName, CancellationToken ct)
    {
        var mapping = await _mappingService.FindByOriginalNameAsync("Personal Notice", ct);
        var code = await _mappingService.GetMigratedCodeAsync("00253", ct);
        var willReceiveSuffix = await _mappingService.WillReceiveMigrationSuffixAsync("Personal Notice", ct);
    }
}
```

### OpisToTipMapperV2:

```csharp
// Stari način
var tipDokumenta = OpisToTipMapper.GetTipDokumenta("KYC Questionnaire");

// Novi način
public class MyService
{
    private readonly OpisToTipMapperV2 _mapper;

    public MyService(OpisToTipMapperV2 mapper)
    {
        _mapper = mapper;
    }

    public async Task<string> GetDocumentTypeAsync(string opis, CancellationToken ct)
    {
        return await _mapper.GetTipDokumentaAsync(opis, ct);
    }
}
```

### DocumentStatusDetectorV2:

```csharp
// Stari način
var info = DocumentStatusDetector.GetMigrationInfoByDocDesc("KYC Questionnaire", "00130", "validiran");

// Novi način
public class MyService
{
    private readonly DocumentStatusDetectorV2 _detector;

    public MyService(DocumentStatusDetectorV2 detector)
    {
        _detector = detector;
    }

    public async Task<DocumentMigrationInfo> GetInfoAsync(string docDesc, string code, string status, CancellationToken ct)
    {
        return await _detector.GetMigrationInfoByDocDescAsync(docDesc, code, status, ct);
    }
}
```

## Migracija postojećeg koda

### Korak 1: Dodaj dependency injection

U klasi koja koristi `HeimdallDocumentMapper`:

```csharp
// Staro
public class MyClass
{
    public void DoSomething()
    {
        var mapping = HeimdallDocumentMapper.FindByOriginalName("Personal Notice");
    }
}

// Novo
public class MyClass
{
    private readonly IDocumentMappingService _mappingService;

    public MyClass(IDocumentMappingService mappingService)
    {
        _mappingService = mappingService;
    }

    public async Task DoSomethingAsync(CancellationToken ct)
    {
        var mapping = await _mappingService.FindByOriginalNameAsync("Personal Notice", ct);
    }
}
```

### Korak 2: Promeni sinhrone metode u asinhrone

Svi pozivi treba da budu asinhroni:

```csharp
// Staro
public void ProcessDocument(string name)
{
    var code = HeimdallDocumentMapper.GetMigratedCode(name);
}

// Novo
public async Task ProcessDocumentAsync(string name, CancellationToken ct)
{
    var code = await _mappingService.GetMigratedCodeAsync(name, ct);
}
```

### Korak 3: Testiranje

Nakon refaktorisanja, testiraj:

1. **Unit testovi**: Mockuj `IDocumentMappingService`
2. **Integration testovi**: Proveri da li se podaci pravilno čitaju iz DocumentMappings tabele
3. **Performance testovi**: Cache bi trebao da obezbedi slične performanse kao statički mapper

## Prednosti novog pristupa

1. **Fleksibilnost**: Mapiranja se mogu menjati bez redeployment-a
2. **Skalabilnost**: Dodavanje novih mapiranja je jednostavno
3. **Centralizacija**: Svi sistemi mogu koristiti istu tabelu
4. **Testabilnost**: Lakše se testira sa dependency injection
5. **Caching**: Automatsko keširanje za performanse
6. **Audit trail**: Mogućnost dodavanja audit kolona u tabelu

## Pitanja?

Za dodatna pitanja ili probleme, kontaktirajte tim za migraciju.
