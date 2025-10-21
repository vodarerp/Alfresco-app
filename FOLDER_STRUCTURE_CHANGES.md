# Folder Structure Changes - Summary

## Pregled Promena

Refaktorisana je logika za rad sa folderima kako bi se omogućila fleksibilnija i strukturiranija organizacija dokumenata.

## Šta je Novo?

### 1. Nova Struktura Foldera

**Stara struktura:**
```
ROOT -> dosie-PL -> PL-10101010 -> dokumenti
```

**Nova struktura:**
```
ROOT -> dosie-PL -> PL10101010 -> dokumenti
```

**Ključna razlika:** Uklonjenja crtica između tipa klijenta i CoreId (`PL-10101010` → `PL10101010`)

### 2. Novi Servisi

#### `IFolderPathService` / `FolderPathService`
- Generiše relativne putanje foldera
- Validira tipove klijenata (PL, FL, ACC)
- Parse-uje folder putanje nazad u komponente
- **Lokacija:** `Migration.Infrastructure.Implementation.FolderPathService`

#### `IFolderManager` / `FolderManager`
- Kreira fizičke foldere na file sistemu
- Kombinuje ROOT putanju sa relativnim putanjama
- Osigurava da folder struktura postoji pre upotrebe
- **Lokacija:** `Migration.Infrastructure.Implementation.FolderManager`

### 3. Konfigurisanje ROOT Putanje

ROOT folder putanja sada se konfiguriše u `appsettings.json`:

```json
{
  "Migration": {
    "RootDocumentPath": "C:\\DocumentsRoot"
  }
}
```

**Važno:** Za production okruženje, preporučuje se korišćenje environment varijable umesto hardkodovane vrednosti u appsettings.json.

```bash
# Windows
set Migration__RootDocumentPath=C:\DocumentsRoot

# Linux/Mac
export Migration__RootDocumentPath=/mnt/documents
```

## Fajlovi Koji su Dodani

1. **Migration.Abstraction/Interfaces/IFolderPathService.cs** - Interface za folder path servis
2. **Migration.Abstraction/Interfaces/IFolderManager.cs** - Interface za folder manager
3. **Migration.Infrastructure/Implementation/FolderPathService.cs** - Implementacija folder path servisa
4. **Migration.Infrastructure/Implementation/FolderManager.cs** - Implementacija folder managera
5. **FOLDER_STRUCTURE_GUIDE.md** - Detaljna dokumentacija sa primerima
6. **FOLDER_STRUCTURE_CHANGES.md** - Ovaj fajl (summary promena)
7. **CA_MockData/README.md** - Dokumentacija za CA_MockData aplikaciju

## Fajlovi Koji su Izmenjeni

### Migration System

1. **Alfresco.Contracts/Options/MigrationOptions.cs**
   - Dodato `RootDocumentPath` property sa dokumentacijom

2. **Migration.Infrastructure/Implementation/Document/DocumentResolver.cs**
   - Dodat `IFolderManager` dependency injection
   - Priprema za korišćenje nove logike kreiranja foldera

3. **Alfresco.App/App.xaml.cs**
   - Registrovan `IFolderPathService` kao Singleton
   - Registrovan `IFolderManager` kao Singleton

4. **Alfresco.App/appsettings.json**
   - Dodato `RootDocumentPath: "C:\\DocumentsRoot"` u Migration sekciju

### CA_MockData Application

5. **CA_MockData/Config.cs**
   - Dodato `UseNewFolderStructure` flag
   - Dodato `ClientTypes` array
   - Dodato `StartingCoreId` property

6. **CA_MockData/Program.cs**
   - Implementirana logika za kreiranje dosie foldera
   - Dodata distribucija foldera po client tipovima
   - Dodato `GetOrCreateFolderAsync` metoda
   - Ažurirano generisanje imena foldera i dokumenata

## Kako Koristiti Nove Servise

### Primer 1: Generisanje Folder Putanje

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
        // Generiši relativnu putanju
        var path = _pathService.GenerateFolderPath("PL", "10101010");
        // Rezultat: "dosie-PL/PL10101010"

        // Generiši samo dosie folder
        var dosieFolder = _pathService.GenerateDosieFolder("PL");
        // Rezultat: "dosie-PL"

        // Generiši samo client folder
        var clientFolder = _pathService.GenerateClientFolder("PL", "10101010");
        // Rezultat: "PL10101010"
    }
}
```

### Primer 2: Kreiranje Fizičkih Foldera

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
        // Osiguraj da folder struktura postoji (kreira ako ne postoji)
        var folderPath = await _folderManager.EnsureFolderStructureAsync(clientType, coreId, ct);
        // folderPath = "C:\DocumentsRoot\dosie-PL\PL10101010"

        // Proveri da li folder postoji (bez kreiranja)
        bool exists = _folderManager.FolderStructureExists(clientType, coreId);

        // Dobij putanju bez kreiranja
        var path = _folderManager.GetClientFolderPath(clientType, coreId);
    }
}
```

## Migration Checklist

Kada deploy-uješ ove promene:

- [x] ✅ Build projekta je uspešan
- [x] ✅ Novi servisi su registrovani u DI kontejneru
- [x] ✅ `appsettings.json` je ažuriran
- [ ] ⏳ Postaviti ROOT folder putanju za production okruženje
- [ ] ⏳ Testirati kreiranje foldera za sve tipove klijenata
- [ ] ⏳ Verificirati da se folderi kreiraju sa novim formatom (bez crtice)
- [ ] ⏳ Ažurirati deployment dokumentaciju

## Validni Tipovi Klijenata

- **PL** - Pravno Lice (Legal Entity)
- **FL** - Fizičko Lice (Natural Person)
- **ACC** - Račun (Account)

## Dodatni Resursi

Za detaljnije informacije:
- **FOLDER_STRUCTURE_GUIDE.md** - Kompletan guide sa svim primerima
- **INTEGRATION_INSTRUCTIONS.md** - Setup uputstva
- **IMPLEMENTATION_SUMMARY.md** - Pregled implementacije

## Kompatibilnost

- ✅ Kompatibilno sa postojećom logikom
- ✅ Nema breaking changes za postojeće servise
- ✅ Stari kod može nastaviti da radi dok se ne ažurira da koristi nove servise
- ⚠️ Za nove implementacije, koristi `IFolderManager` umesto direktnog kreiranja foldera

## Napomene

1. **Thread Safety:** Svi novi servisi su thread-safe
2. **Performance:** Directory.CreateDirectory automatski kreira sve parent foldere, tako da nema potrebe za ručnim kreiranjem
3. **Error Handling:** Servisi bacaju jasne exception poruke za lakše debugovanje
4. **Logging:** Sve operacije se loguju sa relevantnim informacijama

## CA_MockData - Test Data Generator

CA_MockData aplikacija je takođe ažurirana da kreira folder strukturu prema novom formatu.

### Nova Funkcionalnost

```csharp
var cfg = new Config()
{
    // ... connection settings ...
    UseNewFolderStructure = true,           // Nova struktura
    ClientTypes = new[] { "PL", "FL", "ACC" },
    StartingCoreId = 10000000,
    FolderCount = 1000,
    DocsPerFolder = 3
};
```

**Rezultat:** Kreira `dosie-PL/PL10000000`, `dosie-FL/FL10000001`, `dosie-ACC/ACC10000002`, itd.

### Distribucija Foldera

Folderi se ravnomerno distribuiraju između tipova:
- Folder 0 → `dosie-PL/PL10000000`
- Folder 1 → `dosie-FL/FL10000001`
- Folder 2 → `dosie-ACC/ACC10000002`
- Folder 3 → `dosie-PL/PL10000003`
- ... i tako dalje

### Pokretanje

```bash
# Build projekta
dotnet build CA_MockData

# Pokreni
dotnet run --project CA_MockData
```

Za više detalja, pogledaj `CA_MockData/README.md`.

## Status

✅ **IMPLEMENTATION COMPLETE**
- Svi servisi implementirani
- Build je uspešan (Migration + CA_MockData)
- Dokumentacija kreirana
- Spreman za testiranje i deployment

### Completed Components

- ✅ Migration.Infrastructure folder servisi
- ✅ Migration.Abstraction interfejsi
- ✅ DI registracija u App.xaml.cs
- ✅ Configuration u appsettings.json
- ✅ CA_MockData aplikacija ažurirana
- ✅ Dokumentacija (Guide + Changes + README)

---

**Datum:** 2025-10-20
**Status:** Ready for Testing
**Build Status:**
- Migration System: ✅ Successful
- CA_MockData: ✅ Successful
