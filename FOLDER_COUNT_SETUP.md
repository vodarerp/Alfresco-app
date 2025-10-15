# Folder Count Setup - Progress Bar za FolderDiscoveryWorker

## Šta je implementirano?

FolderDiscoveryService sada može da prebroji ukupan broj foldera **pre** nego što krene sa obradom, što omogućava prikaz progress bara sa tačnim procentom.

## Kako radi?

### Automatski fallback sistem:
```
1. Prvo pokušaj: Direktan SQL query na Alfresco PostgreSQL bazu (najbrži i najtačniji)
   ↓ Ako ne uspe
2. Drugi pokušaj: CMIS API (Alfresco.List.Pagination.TotalItems)
   ↓ Ako ni to ne radi
3. Fallback: Prikazuje samo broj obrađenih (bez progress bara)
```

---

## Konfiguracija

### Opcija 1: Direktan SQL na Alfresco bazu (PREPORUČENO)

#### Korak 1: Dodaj connection string u `appsettings.json`

```json
{
  "AlfrescoDatabase": {
    "ConnectionString": "Host=localhost;Port=5432;Database=alfresco;Username=alfresco;Password=alfresco"
  },
  "Alfresco": {
    "BaseUrl": "http://localhost:8080",
    "Username": "admin",
    "Password": "admin"
  },
  // ... ostalo
}
```

**NAPOMENA:** Connection string zavisi od tvoje Alfresco PostgreSQL baze:
- `Host`: Server gde je Alfresco baza
- `Port`: Obično 5432 za PostgreSQL
- `Database`: Ime baze (default: `alfresco`)
- `Username` i `Password`: Kredencijali za pristup bazi

#### Korak 2: Registruj `AlfrescoDbReader` u DI container

Pronađi fajl gde registruješ servise (npr. `Program.cs` ili neki extension method), i dodaj:

```csharp
// Dodaj Alfresco DB options
builder.Services.Configure<AlfrescoDbOptions>(
    builder.Configuration.GetSection(AlfrescoDbOptions.SectionName));

// Registruj AlfrescoDbReader
builder.Services.AddSingleton<IAlfrescoDbReader, AlfrescoDbReader>();

// VAŽNO: Ažuriraj FolderReader registraciju da koristi IAlfrescoDbReader
builder.Services.AddScoped<IFolderReader, FolderReader>(); // Već registrovan, samo proveri
```

#### Korak 3: Proveri usinge u startup fajlu

Dodaj na vrhu fajla:
```csharp
using Migration.Infrastructure.Implementation.Alfresco;
using Migration.Abstraction.Interfaces;
```

---

### Opcija 2: Bez direktnog SQL-a (bez connection stringa)

Ako **ne dodaš** connection string, aplikacija će automatski pokušati CMIS API count. Ako ni to ne radi, prikazaće samo "Obrađeno: X" bez progress bara.

---

## SQL Query koji se izvršava

```sql
WITH RECURSIVE folder_hierarchy AS (
    SELECT id, uuid
    FROM alf_node
    WHERE uuid = @rootUuid  -- '8ccc0f18-5445-4358-8c0f-185445235836'

    UNION ALL

    SELECT n.id, n.uuid
    FROM alf_node n
    JOIN alf_child_assoc ca ON n.id = ca.child_node_id
    JOIN folder_hierarchy fh ON ca.parent_node_id = fh.id
    WHERE n.type_qname_id = (SELECT id FROM alf_qname WHERE local_name = 'folder')
)
SELECT COUNT(*) AS total_filtered_folders
FROM folder_hierarchy fh
JOIN alf_node_properties np ON fh.id = np.node_id
JOIN alf_qname q ON np.qname_id = q.id
WHERE q.local_name = 'name'
AND np.string_value LIKE '%-%';  -- NameFilter iz appsettings.json
```

---

## Testiranje

### 1. Pokreni aplikaciju
```bash
dotnet run --project Alfresco.App
```

### 2. Proveri logove pri startu FolderDiscoveryWorkera:

**Uspešan count (SQL):**
```
[INFO] Attempting to count total folders...
[INFO] Alfresco DB count query returned: 1250 folders
[INFO] Total folders to discover: 1250
```

**CMIS fallback:**
```
[INFO] Attempting to count total folders...
[WARN] Alfresco DB connection string not configured, count unavailable
[INFO] Total folders to discover: 0  (CMIS ne radi)
```

**Bez count-a:**
```
[WARN] Failed to count total folders, continuing without total count
```

### 3. UI prikaz

**Sa count-om:**
```
Folder Discovery Worker
Status: Running
[==============>      ] 68.5%
Obrađeno: 857 / Ukupno: 1250
Preostalo: 393
Message: "Discovered 100 folders in batch 9"
```

**Bez count-a:**
```
Folder Discovery Worker
Status: Running
[                     ] 0%
Obrađeno: 857 / Ukupno: 0
Preostalo: N/A
Message: "Discovered 100 folders in batch 9"
```

---

## Troubleshooting

### Problem: "Npgsql.NpgsqlException: Connection refused"
**Rešenje:**
- Proveri da li je PostgreSQL pokrenut
- Proveri `Host` i `Port` u connection stringu
- Testuj connection string sa `psql` ili pgAdmin

### Problem: "Authentication failed for user"
**Rešenje:** Proveri `Username` i `Password` u connection stringu

### Problem: "Database does not exist"
**Rešenje:** Proveri ime baze (`Database` u connection stringu)

### Problem: Query timeout
**Rešenje:** Query timeout je postavljen na 120 sekundi. Ako imaš mnogo foldera (100k+), možda treba povećati timeout u `AlfrescoDbReader.cs`:
```csharp
command.CommandTimeout = 300; // 5 minuta
```

---

## Performance

- **SQL query:** ~500ms - 2s (zavisi od broja foldera)
- **CMIS API:** 100-500ms (ako radi)
- Query se izvršava **samo jednom** pri startu workera

---

## Implementacioni detalji

### Fajlovi koji su izmenjeni/kreirani:

1. **IAlfrescoDbReader.cs** - Interfejs za čitanje iz Alfresco baze
2. **AlfrescoDbReader.cs** - Implementacija sa SQL queryjem
3. **IFolderReader.cs** - Dodat `CountTotalFoldersAsync`
4. **FolderReader.cs** - Implementiran fallback (SQL → CMIS)
5. **FolderDiscoveryService.cs** - Poziva count na startu
6. **WorkerProgress.cs** - Model sa `TotalItems`, `ProcessedItems`, itd.
7. **WorkerMonitorCard.xaml** - UI kontrola sa progress barom

### DI Container dependency graf:
```
FolderDiscoveryService
  ↓ depends on
IFolderReader (FolderReader)
  ↓ depends on
IAlfrescoDbReader (AlfrescoDbReader) [optional]
  ↓ depends on
AlfrescoDbOptions
```
