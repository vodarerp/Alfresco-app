# Document Status Determination V3 - Implementacija

**Datum:** 2025-11-24
**Verzija:** 3.0
**Status:** ✅ Implementirano

---

## 📋 Pregled

Nova logika za određivanje statusa dokumenta nakon migracije, bazirana na prioritetima i novoj koloni `PolitikaCuvanja`.

---

## 🎯 Biznis pravila

### Prioriteti (od najvišeg ka najnižem):

#### **PRIORITET 1: Šifra dokumenta 00824**
- **Uslev:** `SifraDokumentaMigracija = "00824"`
- **Rezultat:** AKTIVAN
  - `ecm:status = "validiran"`
  - `ecm:active = true`
- **Razlog:** Posebna šifra koja je uvek aktivna bez obzira na druge uslove

#### **PRIORITET 2: Politika čuvanja**
- **Uslov:** `PolitikaCuvanja IN ("Nova verzija", "Novi dokument")`
- **Rezultat:** NEAKTIVAN
  - `ecm:status = "poništen"`
  - `ecm:active = false`
- **Razlog:** Dokumenti sa ovom politikom čuvanja se automatski označavaju kao neaktivni

#### **PRIORITET 3: Sufiks "- migracija"**
- **Uslov 3a:** `NazivDokumentaMigracija` završava sa `"- migracija"` ili `"– migracija"`
  - **Rezultat:** NEAKTIVAN
    - `ecm:status = "poništen"`
    - `ecm:active = false`

- **Uslov 3b:** `NazivDokumentaMigracija` NE završava sa `"- migracija"`
  - **Rezultat:** AKTIVAN
    - `ecm:status = "validiran"`
    - `ecm:active = true`

#### **DEFAULT: Aktivan**
- **Uslov:** Ako nijedna od gornjih provera nije primenjena
- **Rezultat:** AKTIVAN
  - `ecm:status = "validiran"`
  - `ecm:active = true`

---

## 🔧 Implementirane promene

### 1. **Baza podataka**

#### Dodavanje kolone `PolitikaCuvanja`

**Fajl:** `Alfresco.Contracts\Oracle\Models\DocumentMapping.cs:94-102`

```csharp
/// <summary>
/// Politika čuvanja dokumenta - utiče na određivanje statusa
/// Moguće vrednosti: "Nova verzija", "Novi dokument", null/empty
/// </summary>
[Column("PolitikaCuvanja")]
[MaxLength(100)]
public string? PolitikaCuvanja { get; set; }
```

#### SQL Migration Script

**Fajl:** `SQL_Scripts\ADD_POLITIKACUVANJA_COLUMN.sql`

- Dodaje kolonu `PolitikaCuvanja NVARCHAR(100) NULL`
- Kreira view `vw_DocumentMappingStatusCheck` za testiranje logike
- Proverava da li kolona već postoji pre dodavanja

**Pokretanje:**
```sql
USE [AlfrescoStagingDb]
GO

-- Izvršiti skript
EXEC sp_executesql N'...' -- Kompletan sadržaj iz fajla
```

---

### 2. **Repository sloj**

#### Ažuriranje SQL upita

**Fajl:** `SqlServer.Infrastructure\Implementation\DocumentMappingRepository.cs`

Sve SELECT upite ažurirane da uključuju novu kolonu:

- `GetAllMappingsAsync()` - linija 35-48
- `FindByOriginalNameAsync()` - linija 75-89
- `FindByOriginalCodeAsync()` - linija 123-137
- `FindBySerbianNameAsync()` - linija 171-185
- `FindByMigratedNameAsync()` - linija 219-233

**Primer:**
```sql
SELECT TOP 1
    ID, NAZIV, BROJ_DOKUMENATA, sifraDokumenta,
    NazivDokumenta, TipDosijea, TipProizvoda,
    SifraDokumentaMigracija, NazivDokumentaMigracija,
    ExcelFileName, ExcelFileSheet,
    PolitikaCuvanja  -- ← NOVA KOLONA
FROM DocumentMappings WITH (NOLOCK)
WHERE UPPER(NAZIV) = UPPER(@originalName)
```

---

### 3. **Detektor statusa V3**

#### Nova klasa: DocumentStatusDetectorV3

**Fajl:** `Migration.Infrastructure\Implementation\DocumentStatusDetectorV3.cs`

**Glavna metoda:** `DetermineStatus(DocumentMapping? mapping, string? existingStatus)`

**Primer korišćenja:**
```csharp
var fullMapping = await _opisToTipMapper.GetFullMappingAsync(docDesc, ct);
var statusInfo = DocumentStatusDetectorV3.DetermineStatus(fullMapping, existingStatus);

doc.IsActive = statusInfo.IsActive;
doc.NewAlfrescoStatus = statusInfo.Status;
```

**Vraćeni objekat: DocumentStatusInfo**
```csharp
{
    IsActive = true/false,
    Status = "validiran"/"poništen",
    DeterminationReason = "Prioritet 1: SifraDokumentaMigracija = '00824'",
    Priority = 1,
    MappingCode = "00824",
    MappingName = "KDP za fizicka lica - migracija",
    PolitikaCuvanja = "Nova verzija",
    HasMigrationSuffix = true/false
}
```

---

### 4. **Integracija u DocumentDiscoveryService**

**Fajl:** `Migration.Infrastructure\Implementation\Services\DocumentDiscoveryService.cs:483-523`

**Promena:**
```csharp
// STARO (V2):
mappedDocType = await _opisToTipMapper.GetTipDokumentaAsync(docDesc, ct);
var statusInfo = DocumentStatusDetector.GetStatusInfoByOpis(docDesc, existingStatus);

// NOVO (V3):
fullMapping = await _opisToTipMapper.GetFullMappingAsync(docDesc, ct);
mappedDocType = fullMapping?.SifraDokumentaMigracija;
var statusInfo = DocumentStatusDetectorV3.DetermineStatus(fullMapping, existingStatus);
```

**Prednosti:**
- Samo JEDAN SQL upit umesto dva
- Pristup svim poljima mapiranja (uključujući PolitikaCuvanja)
- Bolja performance

---

## 📊 Primeri

### Primer 1: Prioritet 1 - Šifra 00824
```
Input:
  SifraDokumentaMigracija = "00824"
  NazivDokumentaMigracija = "KDP za fizicka lica - migracija"
  PolitikaCuvanja = NULL

Output:
  IsActive = TRUE
  Status = "validiran"
  DeterminationReason = "Prioritet 1: SifraDokumentaMigracija = '00824'"
  Priority = 1
```

### Primer 2: Prioritet 2 - Politika čuvanja
```
Input:
  SifraDokumentaMigracija = "00849"
  NazivDokumentaMigracija = "GDPR saglasnost"
  PolitikaCuvanja = "Nova verzija"

Output:
  IsActive = FALSE
  Status = "poništen"
  DeterminationReason = "Prioritet 2: PolitikaCuvanja = 'Nova verzija'"
  Priority = 2
```

### Primer 3: Prioritet 3 - Sufiks migracija
```
Input:
  SifraDokumentaMigracija = "00841"
  NazivDokumentaMigracija = "KYC upitnik - migracija"
  PolitikaCuvanja = NULL

Output:
  IsActive = FALSE
  Status = "poništen"
  DeterminationReason = "Prioritet 3: NazivDokumentaMigracija ima sufiks '- migracija'"
  Priority = 3
```

### Primer 4: Prioritet 3 - Nema sufiks
```
Input:
  SifraDokumentaMigracija = "00135"
  NazivDokumentaMigracija = "Potvrda o prijemu kartice"
  PolitikaCuvanja = NULL

Output:
  IsActive = TRUE
  Status = "validiran"
  DeterminationReason = "Prioritet 3: NazivDokumentaMigracija NEMA sufiks '- migracija'"
  Priority = 3
```

---

## 🧪 Testiranje

### 1. Testiranje SQL-a

```sql
-- Pregled svih statusa
SELECT * FROM vw_DocumentMappingStatusCheck ORDER BY ID

-- Filtriranje po šifri 00824
SELECT * FROM vw_DocumentMappingStatusCheck
WHERE SifraDokumentaMigracija = '00824'

-- Filtriranje po PolitikaCuvanja
SELECT * FROM vw_DocumentMappingStatusCheck
WHERE PolitikaCuvanja IS NOT NULL

-- Filtriranje po statusu
SELECT * FROM vw_DocumentMappingStatusCheck
WHERE [ecm:status] = 'poništen'
```

### 2. Unit testovi (TODO)

**Preporučeni testovi:**

```csharp
[Fact]
public void DetermineStatus_Priority1_ShouldReturnActive()
{
    var mapping = new DocumentMapping
    {
        SifraDokumentaMigracija = "00824",
        NazivDokumentaMigracija = "KDP - migracija"
    };

    var result = DocumentStatusDetectorV3.DetermineStatus(mapping);

    Assert.True(result.IsActive);
    Assert.Equal("validiran", result.Status);
    Assert.Equal(1, result.Priority);
}

[Fact]
public void DetermineStatus_Priority2_ShouldReturnInactive()
{
    var mapping = new DocumentMapping
    {
        SifraDokumentaMigracija = "00841",
        PolitikaCuvanja = "Nova verzija"
    };

    var result = DocumentStatusDetectorV3.DetermineStatus(mapping);

    Assert.False(result.IsActive);
    Assert.Equal("poništen", result.Status);
    Assert.Equal(2, result.Priority);
}
```

---

## 📝 Checklist za deployment

- [x] Dodati kolonu `PolitikaCuvanja` u DocumentMapping model
- [x] Kreirati SQL migration script
- [x] Ažurirati DocumentMappingRepository
- [x] Kreirati DocumentStatusDetectorV3
- [x] Ažurirati DocumentDiscoveryService
- [ ] Pokrenuti SQL migration script na staging bazi
- [ ] Popuniti PolitikaCuvanja kolonu sa vrednostima iz Excel-a
- [ ] Testirati na malom skupu dokumenata (10-20)
- [ ] Verifikovati log fajlove za DeterminationReason
- [ ] Testirati na većem skupu dokumenata (1000+)
- [ ] Code review
- [ ] Deployment na produkciju

---

## 🔍 Debugging

### Kako verifikovati da nova logika radi?

1. **Proveriti log fajlove:**
```
Status determination: ecm:docDesc 'KYC upitnik', Old Status: 'validiran' →
IsActive: False, New Status: 'poništen', Reason: 'Prioritet 3: NazivDokumentaMigracija ima sufiks '- migracija'', Priority: 3
```

2. **Proveriti staging tabelu:**
```sql
SELECT
    DocDescription,
    DocumentType,
    IsActive,
    NewAlfrescoStatus,
    OldAlfrescoStatus
FROM DocStaging
WHERE DocDescription LIKE '%migracija%'
```

3. **Koristiti SQL view za quick check:**
```sql
SELECT * FROM vw_DocumentMappingStatusCheck
WHERE StatusReason LIKE '%Prioritet 2%'
```

---

## 🚨 Poznati problemi i ograničenja

1. **Stara logika još postoji** u `DocumentStatusDetectorV2` - označena kao Obsolete, ali nije obrisana za kompatibilnost
2. **Cache nije invalidiran** - Nakon dodavanja `PolitikaCuvanja` vrednosti u bazu, potrebno je invalidirati cache:
   ```csharp
   OptimizedOpisToTipMapper.InvalidateCache();
   ```
3. **Excel import** - Potrebno je ažurirati Excel import tool da čita i popunjava `PolitikaCuvanja` kolonu

---

## 📚 Dodatni resursi

- **Original zahtev:** [Issue link ili dokumentacija korisnika]
- **SQL Script:** `SQL_Scripts\ADD_POLITIKACUVANJA_COLUMN.sql`
- **Implementacija:** `Migration.Infrastructure\Implementation\DocumentStatusDetectorV3.cs`
- **Integracija:** `DocumentDiscoveryService.cs:483-523`
- **Refaktoring:** `REFACTORING_DOCUMENTSTATUSINFO.md` - DocumentStatusInfo prebačen u Alfresco.Contracts

---

## ✅ Zaključak

Nova logika za određivanje statusa dokumenta je implementirana i spremna za testiranje.

**Ključne prednosti:**
- Prioritetna logika je jasna i lako razumljiva
- Bolja performance (jedan SQL upit umesto dva)
- Bolji debugging (DeterminationReason i Priority u log-ovima)
- Ekstenzibilna (lako dodati nove prioritete)

**Sledeći koraci:**
1. Pokrenuti SQL migration
2. Popuniti PolitikaCuvanja vrednostima
3. Testirati na staging okruženju
4. Code review i deployment
