# 🎯 Document Status V3 - Kratak pregled

## Šta je promenjeno?

Nova logika za određivanje statusa dokumenta (`ecm:status` i `ecm:active`) sa **prioritetima**.

---

## 📊 Prioritetna logika

| Prioritet | Uslov | ecm:status | ecm:active |
|-----------|-------|------------|------------|
| **1** 🥇 | `SifraDokumentaMigracija = "00824"` | `validiran` | `true` |
| **2** 🥈 | `PolitikaCuvanja IN ("Nova verzija", "Novi dokument")` | `poništen` | `false` |
| **3a** 🥉 | `NazivDokumentaMigracija` ima sufiks `"- migracija"` | `poništen` | `false` |
| **3b** 🥉 | `NazivDokumentaMigracija` NEMA sufiks `"- migracija"` | `validiran` | `true` |
| **Default** | Sve ostalo | `validiran` | `true` |

---

## 🔧 Izmenjeni fajlovi

### 1. **Baza podataka**
- ✅ **Model:** `Alfresco.Contracts\Oracle\Models\DocumentMapping.cs`
  - Dodato polje: `public string? PolitikaCuvanja { get; set; }`

- ✅ **SQL Script:** `SQL_Scripts\ADD_POLITIKACUVANJA_COLUMN.sql`
  - Dodaje kolonu `PolitikaCuvanja NVARCHAR(100) NULL`
  - Kreira view `vw_DocumentMappingStatusCheck` za testiranje

### 2. **Repository**
- ✅ **Fajl:** `SqlServer.Infrastructure\Implementation\DocumentMappingRepository.cs`
  - Svi SELECT upiti ažurirani da uključuju `PolitikaCuvanja`

### 3. **Business Logic**
- ✅ **Novi fajl:** `Migration.Infrastructure\Implementation\DocumentStatusDetectorV3.cs`
  - Statička klasa sa metodom `DetermineStatus()`
  - Vraća `DocumentStatusInfo` sa razlogom i prioritetom

### 4. **Integracija**
- ✅ **Fajl:** `Migration.Infrastructure\Implementation\Services\DocumentDiscoveryService.cs`
  - Linija 483-523: Ažurirana metoda `ApplyDocumentMappingAsync()`
  - Koristi `GetFullMappingAsync()` umesto `GetTipDokumentaAsync()`
  - Poziva `DocumentStatusDetectorV3.DetermineStatus()`

---

## 🚀 Deployment koraci

### 1. Pokrenuti SQL migration
```sql
USE [AlfrescoStagingDb]
GO

-- Pokrenuti skript
.\SQL_Scripts\ADD_POLITIKACUVANJA_COLUMN.sql
```

### 2. Popuniti PolitikaCuvanja vrednostima
```sql
-- Primer: Update vrednosti iz Excel-a
UPDATE DocumentMappings
SET PolitikaCuvanja = 'Nova verzija'
WHERE NAZIV IN ('Personal Notice', 'KYC Questionnaire', ...)

UPDATE DocumentMappings
SET PolitikaCuvanja = 'Novi dokument'
WHERE NAZIV IN ('Admission Card', ...)
```

### 3. Build i deploy aplikacije
```bash
dotnet build
dotnet publish -c Release
```

### 4. Testirati
```sql
-- Provera statusa
SELECT * FROM vw_DocumentMappingStatusCheck
WHERE SifraDokumentaMigracija = '00824'

-- Provera PolitikaCuvanja
SELECT * FROM vw_DocumentMappingStatusCheck
WHERE PolitikaCuvanja IS NOT NULL
```

---

## 🧪 Testiranje

### Quick test query:
```sql
SELECT
    ID,
    Naziv,
    SifraDokumentaMigracija,
    NazivDokumentaMigracija,
    PolitikaCuvanja,
    [ecm:status],
    [ecm:active],
    StatusReason
FROM vw_DocumentMappingStatusCheck
ORDER BY Priority, ID
```

### Očekivani rezultati:
- Svi dokumenti sa šifrom `00824` → **Aktivan** (Prioritet 1)
- Svi sa `PolitikaCuvanja = "Nova verzija"` → **Neaktivan** (Prioritet 2)
- Svi sa sufiksom `"- migracija"` → **Neaktivan** (Prioritet 3)
- Ostali → **Aktivan**

---

## 📝 Logovanje

Novi format log poruka:
```
Status determination: ecm:docDesc 'KYC upitnik', Old Status: 'validiran' →
IsActive: False, New Status: 'poništen',
Reason: 'Prioritet 3: NazivDokumentaMigracija ima sufiks '- migracija'',
Priority: 3
```

**Ključne log poruke za pretragu:**
- `"Status determination:"` - Sve odluke o statusu
- `"Prioritet 1:"` - Šifra 00824
- `"Prioritet 2:"` - PolitikaCuvanja
- `"Prioritet 3:"` - Sufiks migracija

---

## ⚠️ Važne napomene

1. **Cache invalidacija:** Posle popunjavanja `PolitikaCuvanja`, restartovati aplikaciju ili pozvati:
   ```csharp
   OptimizedOpisToTipMapper.InvalidateCache();
   ```

2. **Excel import:** Ažurirati Excel import tool da čita `PolitikaCuvanja` kolonu

3. **Backward compatibility:** Stara logika (`DocumentStatusDetectorV2`) je označena kao `[Obsolete]` ali još postoji

4. **Null values:** Ako `PolitikaCuvanja` je NULL, sistem nastavlja sa Prioritetom 3 (sufiks check)

---

## 📚 Detaljnija dokumentacija

Za detaljnije informacije pogledati:
- **`DOCUMENT_STATUS_V3_IMPLEMENTATION.md`** - Potpuna dokumentacija
- **`SQL_Scripts\ADD_POLITIKACUVANJA_COLUMN.sql`** - SQL migration script
- **`Migration.Infrastructure\Implementation\DocumentStatusDetectorV3.cs`** - Source kod

---

## ✅ Gotovo!

Sve izmene su implementirane i spremne za testiranje.

**Pitanja?** Kontaktirajte development team.
