# KDP Document Processing - Implementation Summary

## 📋 Implementirane Komponente

### 1. **SQL Database**

#### Tabele:
- **KdpDocumentStaging** - Staging tabela za sve KDP dokumente
  - Lokacija: `SQL/12_CreateKdpTables_SqlServer.sql`
  - Primary key: Id (BIGINT IDENTITY)
  - Unique index na NodeId
  - Indexes: AccFolderName, DocumentStatus, DocumentType

- **KdpExportResult** - Finalni rezultati obrade
  - Lokacija: `SQL/12_CreateKdpTables_SqlServer.sql`
  - Primary key: Id (BIGINT IDENTITY)
  - Unique index na ReferencaDokumenta (NodeId)
  - Indexes: KlijentskiBroj (CoreId), AccFolderName

#### Stored Procedures:
- **sp_ProcessKdpDocuments** - Procesuira staging podatke
  - Lokacija: `SQL/13_CreateKdpProcedures_SqlServer.sql`
  - Pronalazi foldere sa samo neaktivnim KDP dokumentima
  - Bira najmlađi dokument po folderu
  - Upisuje rezultate u KdpExportResult tabelu
  - Vraća statistiku: totalCandidates, totalDocuments, oldestDocument, newestDocument

### 2. **Model Klase**

#### Alfresco.Contracts/Oracle/Models:
- **KdpDocumentStaging.cs** - Model za staging tabelu
  - Properties: NodeId, DocumentName, DocumentPath, DocumentType, DocumentStatus, CreatedDate, AccountNumbers, AccFolderName, CoreId

- **KdpExportResult.cs** - Model za export rezultate
  - Properties: ReferncaDosijea, KlijentskiBroj, ReferencaDokumenta, TipDokumenta, DatumKreiranjaDokumenta, TotalKdpDocumentsInFolder

### 3. **Repository Layer**

#### SqlServer.Abstraction/Interfaces:
- **IKdpDocumentStagingRepository** - Interface za staging repository
  - Metode: ClearStagingAsync(), CountAsync()

- **IKdpExportResultRepository** - Interface za export repository
  - Metode: ProcessKdpDocumentsAsync(), GetAllExportResultsAsync(), CountAsync(), ClearResultsAsync()

#### SqlServer.Infrastructure/Implementation:
- **KdpDocumentStagingRepository** - Implementacija staging repository-ja
  - Koristi base SqlServerRepository<T, TKey> za CRUD operacije
  - Custom metode za brisanje i brojanje

- **KdpExportResultRepository** - Implementacija export repository-ja
  - Poziva sp_ProcessKdpDocuments stored procedure
  - Bulk operacije sa rezultatima

### 4. **Service Layer**

#### Migration.Abstraction/Interfaces:
- **IKdpDocumentProcessingService** - Service interface
  - LoadKdpDocumentsToStagingAsync() - Učitava dokumente iz Alfresca
  - ProcessKdpDocumentsAsync() - Poziva SP za obradu
  - ExportToExcelAsync() - Placeholder za Excel export
  - ClearStagingAsync() - Briše staging tabelu
  - GetStatisticsAsync() - Vraća statistiku

- **KdpProcessingStatistics** - Model za statistiku
  - TotalDocumentsInStaging, TotalCandidateFolders, InactiveDocumentsCount, ActiveDocumentsCount, Type00824Count, Type00099Count

#### Migration.Infrastructure/Implementation/Services:
- **KdpDocumentProcessingService** - Service implementacija
  - Koristi IAlfrescoReadApi za učitavanje dokumenata
  - AFTS query: `(=ecm\:docType:"00824" OR =ecm\:docType:"00099") AND TYPE:"cm:content"`
  - Bulk insert u staging tabelu (batch size 1000)
  - Poziva repository za obradu

### 5. **UI Layer**

#### Alfresco.App/UserControls:
- **KdpProcessingUC.xaml** - User Control za KDP processing
  - Sekcija sa statistikom (6 metrika)
  - Dugmići: Učitaj Dokumente, Pokreni Obradu, Osveži Statistiku, Eksportuj u Excel (disabled), Očisti Staging
  - Log output sa timestamp-ima

- **KdpProcessingUC.xaml.cs** - Code-behind
  - Dependency injection za IKdpDocumentProcessingService
  - Event handlers za sve akcije
  - Async operacije sa CancellationToken
  - UI thread safe operacije (Dispatcher.Invoke)

#### Main.xaml:
- Dodat novi TabItem: "KDP Processing"

### 6. **Dependency Injection**

#### App.xaml.cs:
- Registrovani repository-ji:
  ```csharp
  services.AddTransient<IKdpDocumentStagingRepository, KdpDocumentStagingRepository>();
  services.AddTransient<IKdpExportResultRepository, KdpExportResultRepository>();
  ```

- Registrovan servis:
  ```csharp
  services.AddSingleton<IKdpDocumentProcessingService, KdpDocumentProcessingService>();
  ```

---

## 🚀 Korišćenje

### **KORAK 1: Kreiranje Baze**

1. Otvori SQL Server Management Studio
2. Selektuj `AlfrescoMigration` bazu
3. Pokreni skripte redom:
   ```sql
   -- 1. Kreiraj tabele
   \SQL\12_CreateKdpTables_SqlServer.sql

   -- 2. Kreiraj stored procedures
   \SQL\13_CreateKdpProcedures_SqlServer.sql
   ```

### **KORAK 2: Pokretanje Aplikacije**

1. Build solution (`Ctrl+Shift+B`)
2. Pokreni aplikaciju (`F5`)
3. Idi na tab **"KDP Processing"**

### **KORAK 3: Obrada Dokumenata**

#### A) Učitavanje dokumenata iz Alfresca:
1. Klikni na **"1. Učitaj KDP Dokumente"**
2. Aplikacija će:
   - Očistiti staging tabelu
   - Izvršiti AFTS query za dokumente tipa 00824 i 00099
   - Bulk insert u KdpDocumentStaging tabelu
   - Prikazati broj učitanih dokumenata

#### B) Obrada dokumenata:
1. Klikni na **"2. Pokreni Obradu (sp_ProcessKdpDocuments)"**
2. Stored procedure će:
   - Pronaći foldere sa samo neaktivnim KDP dokumentima
   - Izabrati najmlađi dokument po folderu
   - Upisati rezultate u KdpExportResult tabelu
   - Prikazati broj kandidat foldera i dokumenata

#### C) Pregled statistike:
- Klikni na **"Osveži Statistiku"** u bilo kom momentu
- Statistika prikazuje:
  - Broj dokumenata u staging tabeli
  - Broj kandidat foldera (folderi sa samo neaktivnim KDP dokumentima)
  - Ukupan broj KDP dokumenata
  - Broj neaktivnih/aktivnih dokumenata
  - Broj dokumenata po tipu (00824/00099)

#### D) Brisanje staging podataka:
- Klikni na **"Očisti Staging Tabelu"** ako želiš da pokreneš proces iznova

---

## 📊 SQL Query-ji za Proveru

### Pregled staging tabele:
```sql
-- Svi KDP dokumenti u staging-u
SELECT * FROM KdpDocumentStaging
ORDER BY AccFolderName, CreatedDate DESC;

-- Broj dokumenata po statusu
SELECT DocumentStatus, COUNT(*) as Count
FROM KdpDocumentStaging
GROUP BY DocumentStatus;

-- Broj dokumenata po tipu
SELECT DocumentType, COUNT(*) as Count
FROM KdpDocumentStaging
GROUP BY DocumentType;
```

### Pregled export rezultata:
```sql
-- Svi kandidat folderi
SELECT * FROM KdpExportResult
ORDER BY AccFolderName;

-- Statistika po folderu
SELECT
    AccFolderName,
    KlijentskiBroj,
    TotalKdpDocumentsInFolder,
    TipDokumenta,
    DatumKreiranjaDokumenta
FROM KdpExportResult
ORDER BY TotalKdpDocumentsInFolder DESC;

-- Broj foldera po broju dokumenata
SELECT
    TotalKdpDocumentsInFolder,
    COUNT(*) as FolderCount
FROM KdpExportResult
GROUP BY TotalKdpDocumentsInFolder
ORDER BY TotalKdpDocumentsInFolder;
```

### Provera validnosti rezultata:
```sql
-- Folderi sa SAMO neaktivnim dokumentima (validacija)
WITH FolderCounts AS (
    SELECT
        AccFolderName,
        SUM(CASE WHEN DocumentStatus = '2' THEN 1 ELSE 0 END) as InactiveCount,
        SUM(CASE WHEN DocumentStatus != '2' THEN 1 ELSE 0 END) as ActiveCount
    FROM KdpDocumentStaging
    WHERE AccFolderName IS NOT NULL
    GROUP BY AccFolderName
)
SELECT * FROM FolderCounts
WHERE InactiveCount > 0 AND ActiveCount = 0
ORDER BY InactiveCount DESC;
```

---

## 🔍 Troubleshooting

### Problem: Build greške nakon dodavanja novih fajlova

**Rešenje:**
1. Rebuild solution (`Ctrl+Shift+B`)
2. Ako greške perzistiraju, restartuj Visual Studio

### Problem: "IKdpDocumentProcessingService nije registrovan"

**Rešenje:**
- Proveri da li je servis registrovan u `App.xaml.cs`
- Proveri da li linija 349 sadrži:
  ```csharp
  services.AddSingleton<IKdpDocumentProcessingService, KdpDocumentProcessingService>();
  ```

### Problem: SQL greška "Invalid object name 'KdpDocumentStaging'"

**Rešenje:**
- Pokreni SQL skriptu `12_CreateKdpTables_SqlServer.sql`
- Proveri connection string u `appsettings.Connections.json`

### Problem: "Null reference exception" u servisu

**Rešenje:**
- Proveri da li su repository-ji registrovani u DI kontejneru
- Proveri connection string za SQL Server bazu

---

## ⚙️ Tehnički Detalji

### AFTS Query Format:
```
(=ecm\:docType:"00824" OR =ecm\:docType:"00099") AND TYPE:"cm:content"
```
- Pretraga dokumenata sa custom property `ecm:docType` = "00824" ili "00099"
- Filtriranje samo dokumenata (ne folderi)

### Bulk Insert Performance:
- Batch size: 1000 dokumenata po batch-u
- Koristi `InsertManyAsync` metodu iz base `SqlServerRepository`
- Optimizovano za velike količine podataka (100K+ dokumenata)

### Regular Expression za ACC Folder:
```regex
ACC-\d+
```
- Ekstrahuje "ACC-123456" iz putanje

### Thread Safety:
- Svi UI update-i koriste `Dispatcher.Invoke`
- CancellationToken support za prekidanje dugačkih operacija

---

## 📝 Buduće Implementacije

### Excel Export (trenutno disabled):
- ExportToExcelAsync() metoda je placeholder
- Potrebno dodati ClosedXML ili EPPlus NuGet package
- Eksport KdpExportResult tabele u Excel format
- Kolone: ReferncaDosijea, KlijentskiBroj, ReferencaDokumenta, TipDokumenta, DatumKreiranjaDokumenta, ListaRacuna

### Excel Import od Banke (ako zatreba):
- Import Excel fajla koji banka popuni sa listom računa
- Validacija podataka
- Update dokumenata u Alfrescu sa novom listom računa

### Aktivacija Dokumenata:
- Automatska aktivacija najmlađih dokumenata
- Update `ecm:docStatus` sa "2" (neaktivan) na "1" (aktivan)
- Update `ecm:docType` sa "00824" na "00099" (ako je potrebno)

---

## ✅ Zaključak

Implementacija KDP Document Processing funkcionalnosti je **završena** i **spremna za testiranje**.

**Kreirane komponente:**
- 2 SQL tabele
- 1 SQL stored procedure
- 2 model klase
- 2 repository interface + implementacije
- 1 service interface + implementacija
- 1 WPF UserControl (UI)
- DI registracija

**Sledeći korak:** Manuelno testiranje procesa sa produkcijskim podacima.

---

**Datum implementacije:** 2025-12-19
**Verzija:** 1.0
