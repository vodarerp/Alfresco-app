# Migration Preparation Service - Usage Guide

## 🎯 Svrha

`MigrationPreparationService` priprema bazu podataka pre pokretanja migracije:
- **Briše sve incomplete dokumente** iz `DocStaging` tabele (Status != 'DONE')
- **Briše sve incomplete foldere** iz `FolderStaging` tabele (Status != 'DONE%')
- **Omogućava clean start** migracije bez stuck items-a iz prethodnih pokretanja

---

## ✅ Kada koristiti?

**OBAVEZNO pozovi PRE pokretanja migracije:**
- Na početku `MigrationWorker.RunAsync()`
- Pre `DocumentSearchService.RunSearchAsync()`
- Posle restart-a aplikacije (da očistiš IN_PROGRESS items)

---

## 📦 Instalaacija (već urađeno)

### 1. DI Registration (App.xaml.cs)
```csharp
services.AddSingleton<IMigrationPreparationService, MigrationPreparationService>();
```

### 2. Extension metode (RepositoryExtensions.cs)
```csharp
// Već dodato u Migration.Extensions.SqlServer:
- DeleteIncompleteDocumentsAsync
- DeleteIncompleteFoldersAsync
- CountIncompleteDocumentsAsync
- CountIncompleteFoldersAsync
```

---

## 🚀 Korišćenje

### Opcija A: U MigrationWorker (PREPORUČENO)

```csharp
public class MigrationWorker : IMigrationWorker
{
    private readonly IMigrationPreparationService _preparationService;

    public MigrationWorker(
        ...,
        IMigrationPreparationService preparationService)
    {
        _preparationService = preparationService;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            // 🔴 KORAK 1: OBAVEZNO - Pripremi bazu PRE migracije
            _logger.LogInformation("Preparing database before migration...");
            var prepResult = await _preparationService.PrepareForMigrationAsync(ct);

            if (!prepResult.Success)
            {
                _logger.LogError("Database preparation failed: {Error}", prepResult.ErrorMessage);
                _uiLogger.LogError("Priprema baze nije uspela!");
                return; // Stop migration
            }

            _logger.LogInformation(
                "Database prepared: Deleted {DocCount} documents, {FolderCount} folders (Total: {Total})",
                prepResult.DeletedDocuments, prepResult.DeletedFolders, prepResult.TotalDeleted);

            if (prepResult.TotalDeleted > 0)
            {
                _uiLogger.LogInformation(
                    "Baza očišćena: Uklonjeno {Total} nekompletnih stavki",
                    prepResult.TotalDeleted);
            }

            // 🟢 KORAK 2: Pokreni migraciju
            if (_migrationOptions.MigrationByDocument)
            {
                // DocumentSearch → FolderPreparation → Move
                await RunMigrationByDocumentAsync(ct);
            }
            else
            {
                // FolderDiscovery → DocumentDiscovery → FolderPreparation → Move
                await RunMigrationByFolderAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            _uiLogger.LogError("Migracija failovala: {Error}", ex.Message);
            throw;
        }
    }
}
```

---

### Opcija B: Direktno korišćenje (za testiranje)

```csharp
// Inject servis gde god ti treba
private readonly IMigrationPreparationService _preparationService;

public async Task PrepareAsync()
{
    var result = await _preparationService.PrepareForMigrationAsync(CancellationToken.None);

    if (result.Success)
    {
        Console.WriteLine($"✅ Deleted {result.TotalDeleted} incomplete items");
        Console.WriteLine($"   - Documents: {result.DeletedDocuments}");
        Console.WriteLine($"   - Folders: {result.DeletedFolders}");
    }
    else
    {
        Console.WriteLine($"❌ Preparation failed: {result.ErrorMessage}");
    }
}
```

---

## 📊 Rezultat (MigrationPreparationResult)

```csharp
public class MigrationPreparationResult
{
    public int DeletedDocuments { get; set; }    // Broj obrisanih dokumenata
    public int DeletedFolders { get; set; }      // Broj obrisanih foldera
    public int TotalDeleted { get; }             // Ukupno obrisano (auto calculated)
    public bool Success { get; set; }            // Da li je uspelo
    public string? ErrorMessage { get; set; }    // Error poruka ako je failovalo
}
```

---

## 🔍 Šta se briše?

### DocStaging tabela
```sql
DELETE FROM DocStaging
WHERE Status != 'DONE'
   OR Status IS NULL
```

**Briše:**
- Status = 'READY' (nisu processirani)
- Status = 'IN_PROGRESS' (stuck items)
- Status = 'ERROR' (failovani)
- Status = 'RESETED' (resetovani)
- Status IS NULL (nevalidni)

**NE briše:**
- Status = 'DONE' (uspešno završeni) ✅

---

### FolderStaging tabela
```sql
DELETE FROM FolderStaging
WHERE Status NOT LIKE 'DONE%'
   OR Status IS NULL
```

**Briše:**
- Status = 'READY'
- Status = 'IN_PROGRESS'
- Status = 'ERROR'
- Status IS NULL

**NE briše:**
- Status LIKE 'DONE%' (uspešno završeni) ✅

---

## ⚙️ Internal Flow

```
1. BeginTransaction()
2. CountIncompleteDocumentsAsync()     → Log pre brisanja
3. CountIncompleteFoldersAsync()       → Log pre brisanja
4. DeleteIncompleteDocumentsAsync()    → DELETE WHERE Status != 'DONE'
5. DeleteIncompleteFoldersAsync()      → DELETE WHERE Status NOT LIKE 'DONE%'
6. CommitTransaction()
7. Return MigrationPreparationResult
```

---

## 🛡️ Error Handling

**Ako DELETE faila:**
- Transaction se rollback-uje ✅
- Exception se loguje ✅
- MigrationPreparationResult.Success = false ✅
- ErrorMessage se popunjava ✅
- Exception se rethrow-uje (caller može da odluči šta dalje)

---

## 📝 Logging

Service koristi **3 logger-a:**

### 1. FileLogger (detaljno)
```
🗑️ Starting database preparation - deleting incomplete items
Found 1250 incomplete documents and 47 incomplete folders
Deleting incomplete documents from DocStaging...
✅ Deleted 1250 incomplete documents
Deleting incomplete folders from FolderStaging...
✅ Deleted 47 incomplete folders
✅ Database preparation completed - Deleted 1250 documents and 47 folders (Total: 1297)
ℹ️ Migration will start fresh - DocumentSearchService will repopulate staging tables
```

### 2. DbLogger (za bazu)
```
Starting database preparation
Database preparation completed - deleted 1297 items
```

### 3. UiLogger (za UI)
```
Preparing database for migration...
Database prepared: 1297 incomplete items removed
Ready to start migration from clean state
```

---

## ✅ Prednosti

1. **Clean start** - nema stuck items-a iz prethodnih pokretanja
2. **Idempotentno** - možeš pokrenuti više puta bez problema
3. **Brzo** - DELETE je brži od UPDATE
4. **Jednostavno** - jedan API call
5. **Transparentno** - detaljno logovanje

---

## ⚠️ Napomene

### Da li gubim podatke?
**NE!** Brišu se samo INCOMPLETE items:
- DocumentSearchService će ponovo napuniti DocStaging sa READY statusom
- Svi DONE dokumenti ostaju netaknuti ✅

### Kada NE treba da zovem?
- Ako želiš da nastaviš od checkpoint-a (resume) - ALI za sada nema checkpoint-a implementiranog
- Ako imaš custom statuse koje ne želiš da brišeš

### Alternativa - UPDATE umesto DELETE?
Ako želiš da zadržiš history, možeš koristiti:
```sql
UPDATE DocStaging SET Status = 'RESETED' WHERE Status != 'DONE'
```
Ali DELETE je preporučen jer:
- DocumentSearchService ionako ponovo insertuje
- Manje podataka u bazi = brže query-jevi
- Jednostavnije za debugging

---

## 🎯 Rezime

**UVEK pozovi `PrepareForMigrationAsync()` pre pokretanja migracije!**

```csharp
// ✅ DOBRO
await _preparationService.PrepareForMigrationAsync(ct);
await _migrationWorker.RunAsync(ct);

// ❌ LOŠE (stuck items mogu blokirati migraciju)
await _migrationWorker.RunAsync(ct);
```
