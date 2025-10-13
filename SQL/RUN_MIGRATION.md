# Kako Izvršiti SQL Migraciju

## Preduslovi

1. **Oracle SQL*Plus** ili **Oracle SQL Developer** instaliran
2. Pristup Oracle bazi (FREEPDB1 na localhost:1521)
3. User: `APPUSER` sa privilegijama za ALTER TABLE i CREATE INDEX

## Opcija 1: Izvršavanje putem SQL*Plus (Command Line)

### Korak 1: Povežite se na bazu
```bash
sqlplus APPUSER/appPass@localhost:1521/FREEPDB1
```

### Korak 2: Izvršite migracionu skriptu
```sql
@C:\Users\Nikola Preradov\source\repos\Alfresco\SQL\001_Extend_Staging_Tables.sql
```

### Korak 3: Proverite rezultate
Skripta automatski izvršava verification queries na kraju koje pokazuju:
- Broj novih kolona u DOC_STAGING (treba biti 16)
- Broj novih kolona u FOLDER_STAGING (treba biti 20)
- Listu kreiranih indeksa

## Opcija 2: Izvršavanje putem Oracle SQL Developer (GUI)

### Korak 1: Otvorite SQL Developer
1. Otvorite Oracle SQL Developer
2. Kreirajte novu konekciju:
   - Name: `Alfresco_Migration`
   - Username: `APPUSER`
   - Password: `appPass`
   - Hostname: `localhost`
   - Port: `1521`
   - SID ili Service name: `FREEPDB1`
3. Test Connection → Connect

### Korak 2: Učitajte i izvršite skriptu
1. File → Open → Izaberite `001_Extend_Staging_Tables.sql`
2. Kliknite na "Run Script" (F5) ili zelenu play ikonu sa dokumentom
3. Pratite output u "Script Output" panelu

### Korak 3: Proverite rezultate
Pogledajte output u "Script Output" panelu:
```
Adding new columns to DOC_STAGING table...
DOC_STAGING table columns added successfully.
Adding column comments to DOC_STAGING...
Creating indexes on DOC_STAGING...
DOC_STAGING indexes created successfully.
Adding new columns to FOLDER_STAGING table...
FOLDER_STAGING table columns added successfully.
Adding column comments to FOLDER_STAGING...
Creating indexes on FOLDER_STAGING...
FOLDER_STAGING indexes created successfully.
Running verification queries...

DOC_STAGING_NEW_COLUMNS
-----------------------
16

FOLDER_STAGING_NEW_COLUMNS
--------------------------
20

INDEX_NAME                     TABLE_NAME        UNIQUENESS
------------------------------ ----------------- ----------
IDX_DOC_STAGING_ACTIVE         DOC_STAGING       NONUNIQUE
IDX_DOC_STAGING_CONTRACT       DOC_STAGING       NONUNIQUE
IDX_DOC_STAGING_COREID_TYPE    DOC_STAGING       NONUNIQUE
IDX_DOC_STAGING_DUT_OFFER      DOC_STAGING       NONUNIQUE
IDX_DOC_STAGING_SOURCE         DOC_STAGING       NONUNIQUE
IDX_DOC_STAGING_TRANS_FLAG     DOC_STAGING       NONUNIQUE
IDX_FOLDER_STAGING_CLIENT_TYPE FOLDER_STAGING    NONUNIQUE
IDX_FOLDER_STAGING_CONTRACT    FOLDER_STAGING    NONUNIQUE
IDX_FOLDER_STAGING_COREID      FOLDER_STAGING    NONUNIQUE
IDX_FOLDER_STAGING_SOURCE      FOLDER_STAGING    NONUNIQUE
IDX_FOLDER_STAGING_UNIQUE_ID   FOLDER_STAGING    NONUNIQUE

Migration script completed successfully!
```

## Opcija 3: Izvršavanje iz C# koda (ako želite automatizaciju)

Možete kreirati helper metodu u C# projektu:

```csharp
using Oracle.ManagedDataAccess.Client;

public class DatabaseMigrationRunner
{
    private readonly string _connectionString;

    public DatabaseMigrationRunner(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task RunMigrationAsync(string scriptPath)
    {
        var sqlScript = await File.ReadAllTextAsync(scriptPath);

        // Split script by GO or semicolon statements
        var statements = sqlScript.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);

        using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var statement in statements)
        {
            var trimmed = statement.Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                trimmed.StartsWith("--") ||
                trimmed.StartsWith("/*") ||
                trimmed.StartsWith("PROMPT"))
                continue;

            try
            {
                using var command = new OracleCommand(trimmed, connection);
                command.CommandTimeout = 300; // 5 minutes
                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"Executed: {trimmed.Substring(0, Math.Min(50, trimmed.Length))}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing statement: {ex.Message}");
                Console.WriteLine($"Statement: {trimmed}");
                throw;
            }
        }
    }
}

// Usage:
var runner = new DatabaseMigrationRunner(
    "User Id=APPUSER;Password=appPass;Data Source=localhost:1521/FREEPDB1;");
await runner.RunMigrationAsync(@"C:\Users\Nikola Preradov\source\repos\Alfresco\SQL\001_Extend_Staging_Tables.sql");
```

## Verifikacija Nakon Izvršavanja

Nakon uspešnog izvršavanja skripte, možete proveriti strukturu tabela:

### Proverite DOC_STAGING kolone:
```sql
SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, NULLABLE
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'DOC_STAGING'
ORDER BY COLUMN_ID;
```

### Proverite FOLDER_STAGING kolone:
```sql
SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, NULLABLE
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'FOLDER_STAGING'
ORDER BY COLUMN_ID;
```

### Proverite indekse:
```sql
SELECT INDEX_NAME, TABLE_NAME, COLUMN_NAME, COLUMN_POSITION
FROM USER_IND_COLUMNS
WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING')
  AND INDEX_NAME LIKE 'IDX_%'
ORDER BY TABLE_NAME, INDEX_NAME, COLUMN_POSITION;
```

### Proverite komentare kolona:
```sql
SELECT TABLE_NAME, COLUMN_NAME, COMMENTS
FROM USER_COL_COMMENTS
WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING')
  AND COMMENTS IS NOT NULL
ORDER BY TABLE_NAME, COLUMN_NAME;
```

## Rollback (Ako Nešto Pođe Po Zlu)

Ako morate vratiti izmene, odkomentirajte i izvršite rollback sekciju na kraju skripte:

1. Otvorite `001_Extend_Staging_Tables.sql`
2. Skrolujte do linije 297 (ROLLBACK SCRIPT sekcija)
3. Odkomentirajte ceo rollback blok (obrišite `/*` na početku i `*/` na kraju)
4. Izvršite samo rollback sekciju

**UPOZORENJE:** Rollback će obrisati sve nove kolone i sve podatke u njima!

## Česta Pitanja

### Q: Šta ako dobijem grešku "table or view does not exist"?
A: Proverite da li tabele DOC_STAGING i FOLDER_STAGING postoje:
```sql
SELECT TABLE_NAME FROM USER_TABLES WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING');
```

### Q: Šta ako dobijem grešku "column already exists"?
A: Neke kolone su već dodate. Možete:
1. Proveriti koje kolone već postoje
2. Ručno ukloniti te kolone iz ALTER TABLE naredbi
3. Ili izvršiti rollback i ponovo pokrenuti

### Q: Koliko vremena traje izvršavanje?
A: Na praznim tabelama: ~1-2 sekunde
Na tabelama sa milionima redova: može trajati nekoliko minuta zbog kreiranja indeksa

### Q: Da li ovo utiče na postojeće podatke?
A: Ne! Samo se dodaju nove kolone. Postojeći podaci ostaju netaknuti.

### Q: Da li aplikacija mora biti zaustavljena tokom migracije?
A: Preporučuje se zaustavljanje aplikacije dok se migracija izvršava, posebno ako se kreiraju indeksi na velikim tabelama.

## Sledeći Koraci Nakon Migracije

Nakon uspešnog izvršavanja SQL migracije:

1. ✅ Verifikujte da su sve kolone dodate
2. ✅ Verifikujte da su svi indeksi kreirani
3. ⏳ Konfigurišite ClientAPI i DUT API endpoints u appsettings.json
4. ⏳ Odkomentirajte service registrations u Program.cs
5. ⏳ Odkomentirajte integration kod u DocumentDiscoveryService i FolderDiscoveryService
6. ⏳ Testirajte aplikaciju sa malim batch-om

Za detaljne sledeće korake, pogledajte **INTEGRATION_INSTRUCTIONS.md**.
