# Folder Naming Fix - Dodavanje Crtice u Dossier IDs

## Datum: 2025-01-21

## Problem
Folderi u novom Alfresco sistemu **MORAJU** imati crticu između prefiksa i CoreId:
- **Potreban format**: `PI-102206`, `LE-500342`, `ACC-13001926`
- **Stari kod**: Brisao crticu → `PI102206`, `LE500342`, `ACC13001926` ❌

## Razlog za Promenu
Stari Alfresco folderi mogu biti u različitim formatima:
- Sa crticom: `PI-102206` (već ispravan format)
- Bez crtice: `PI102206` (treba dodati crticu)

Novi Alfresco sistem **zahteva** format sa crticom za pretragu i organizaciju.

---

## Šta Je Izmenjeno

### Fajl: `Alfresco.Contracts\Mapper\DossierIdFormatter.cs`

#### 1. **Class-level dokumentacija**
**STARO**:
```csharp
/// Standard dossiers:
/// - OLD format: {Prefix}-{CoreId} (with hyphen) - Example: PI-102206, LE-500342
/// - NEW format: {Prefix}{CoreId} (without hyphen) - Example: PI102206, LE500342
```

**NOVO**:
```csharp
/// Standard dossiers:
/// - OLD format (in old Alfresco): May or may not have hyphen (PI-102206 or PI102206)
/// - NEW format (required in new Alfresco): {Prefix}-{CoreId} WITH hyphen - Example: PI-102206, LE-500342, ACC-13001926
```

---

#### 2. **`ConvertToNewFormat()` - Glavna Metoda**

**STARO (brisalo crticu)**:
```csharp
public static string ConvertToNewFormat(string oldDossierId)
{
    if (string.IsNullOrWhiteSpace(oldDossierId))
        return string.Empty;

    return oldDossierId.Replace("-", ""); // ❌ BRISALO CRTICU
}

// Primeri:
// "PI-102206" → "PI102206" (uklonjena crtica)
// "PI102206"  → "PI102206" (ista vrednost)
```

**NOVO (dodaje crticu)**:
```csharp
public static string ConvertToNewFormat(string oldDossierId)
{
    if (string.IsNullOrWhiteSpace(oldDossierId))
        return string.Empty;

    // Ako već ima crticu, vrati kao je
    if (oldDossierId.Contains("-"))
        return oldDossierId.ToUpperInvariant();

    // Ekstraktuj prefix i CoreId, pa ih spoji SA crticom
    var prefix = ExtractPrefix(oldDossierId);
    var coreId = ExtractCoreId(oldDossierId);

    if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(coreId))
        return oldDossierId.ToUpperInvariant(); // Fallback

    return CreateNewDossierId(prefix, coreId); // Vraća: "PREFIX-COREID"
}

// Primeri:
// "PI-102206" → "PI-102206" (već ima crticu, bez promene)
// "PI102206"  → "PI-102206" (dodaje crticu)
// "LE500342"  → "LE-500342" (dodaje crticu)
// "ACC13001926" → "ACC-13001926" (dodaje crticu)
```

---

#### 3. **`CreateNewDossierId()` - Helper Metoda**

**STARO (bez crtice)**:
```csharp
public static string CreateNewDossierId(string prefix, string coreId)
{
    if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(coreId))
        return string.Empty;

    return $"{prefix.ToUpperInvariant()}{coreId}"; // ❌ BEZ CRTICE
}

// Primeri:
// CreateNewDossierId("ACC", "500342") → "ACC500342"
// CreateNewDossierId("PI", "102206")  → "PI102206"
```

**NOVO (SA crticom)**:
```csharp
public static string CreateNewDossierId(string prefix, string coreId)
{
    if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(coreId))
        return string.Empty;

    return $"{prefix.ToUpperInvariant()}-{coreId}"; // ✅ SA CRTICOM
}

// Primeri:
// CreateNewDossierId("ACC", "500342") → "ACC-500342"
// CreateNewDossierId("PI", "102206")  → "PI-102206"
// CreateNewDossierId("D", "500342")   → "D-500342"
```

---

#### 4. **`IsOldFormat()` i `IsNewFormat()` - Promenjena Logika**

**STARO**:
```csharp
// OLD format = SA crticom ❌
public static bool IsOldFormat(string dossierId)
{
    return dossierId.Contains("-");
}

// NEW format = BEZ crtice ❌
public static bool IsNewFormat(string dossierId)
{
    return !dossierId.Contains("-");
}
```

**NOVO (inverzna logika)**:
```csharp
// OLD format = BEZ crtice ✅
public static bool IsOldFormat(string dossierId)
{
    return !dossierId.Contains("-");
}

// NEW format = SA crticom ✅
public static bool IsNewFormat(string dossierId)
{
    return dossierId.Contains("-");
}
```

---

#### 5. **`CreateDepositDossierId()` - Deposit Folderi**

**STARO**:
```csharp
return $"DE{coreId}-{productType}_{contractNumber}";
// Primer: "DE500342-00008_12345" ❌ (nema crtice posle DE)
```

**NOVO**:
```csharp
return $"DE-{coreId}-{productType}_{contractNumber}";
// Primer: "DE-500342-00008_12345" ✅ (crtica posle DE)
```

---

#### 6. **`ParseDepositDossierId()` - Backward Compatibility**

Sada podržava **oba formata** (sa i bez crtice posle `DE`):

```csharp
// Remove "DE" or "DE-" prefix
var withoutPrefix = depositDossierId.StartsWith("DE-", StringComparison.OrdinalIgnoreCase)
    ? depositDossierId.Substring(3)  // "DE-500342-00008_12345" → "500342-00008_12345"
    : depositDossierId.Substring(2);  // "DE500342-00008_12345" → "500342-00008_12345"

// Oba formata rade:
// ParseDepositDossierId("DE-500342-00008_12345") → ("500342", "00008", "12345") ✅
// ParseDepositDossierId("DE500342-00008_12345")  → ("500342", "00008", "12345") ✅
```

---

#### 7. **`ConvertForTargetType()` - ACC Konverzija**

Sada dodaje crticu u svim slučajevima:

**Primeri**:
```csharp
ConvertForTargetType("PI-102206", 300) → "ACC-102206"  // ✅ (sa crticom)
ConvertForTargetType("PI102206", 300)  → "ACC-102206"  // ✅ (dodaje crticu)
ConvertForTargetType("LE-500342", 300) → "ACC-500342"  // ✅ (sa crticom)
ConvertForTargetType("PI-102206", 500) → "PI-102206"   // ✅ (zadržava crticu)
```

**STARO (brisalo crticu)**:
```csharp
ConvertForTargetType("PI-102206", 300) → "ACC102206"  // ❌ (bez crtice)
ConvertForTargetType("PI102206", 300)  → "ACC102206"  // ❌ (bez crtice)
```

---

## Gde Se Koristi `DossierIdFormatter`?

### **DocumentDiscoveryService** (linija 575):
```csharp
doc.DossierDestFolderId = DossierIdFormatter.ConvertForTargetType(
    folder.Name,  // npr. "PI-102206" ili "PI102206"
    doc.TargetDossierType ?? (int)DossierType.Unknown);

// Rezultat:
// folder.Name = "PI-102206" + TargetType = 500 → "PI-102206"  ✅
// folder.Name = "PI102206"  + TargetType = 500 → "PI-102206"  ✅
// folder.Name = "PI-102206" + TargetType = 300 → "ACC-102206" ✅
```

---

## Implikacije Promene

### ✅ **Pozitivno**:
1. **Konzistentnost**: Svi folderi u novom Alfresco-u imaju crticu
2. **Backward Compatibility**: Podržava i stari format (bez crtice) i novi format (sa crticom)
3. **Pretraga**: Novi Alfresco može da pretražuje foldere sa predvidljivim formatom
4. **Deposit Folderi**: Sada koriste konzistentan format `DE-{CoreId}-{ProductType}_{ContractNumber}`

### ⚠️ **Potencijalni Problemi**:
1. **Postojeći Podaci**: Ako u DocStaging tabeli već postoje DossierDestFolderId **BEZ** crtice, moguće neusaglašenosti
2. **Folder Pretraga**: FolderPreparationService mora kreirati foldere SA crticom
3. **Testing**: Potrebno testirati oba scenarija (stari folderi sa/bez crtice)

---

## Test Cases

### **Test 1: Folderi koji već imaju crticu**
```csharp
Input:  "PI-102206"
Output: "PI-102206"
Status: ✅ Nema promene, već ispravan format
```

### **Test 2: Folderi bez crtice**
```csharp
Input:  "PI102206"
Output: "PI-102206"
Status: ✅ Dodaje crticu
```

### **Test 3: ACC konverzija sa crticom**
```csharp
Input:  "PI-102206" + TargetType = 300 (ACC)
Output: "ACC-102206"
Status: ✅ Menja prefix, zadržava crticu
```

### **Test 4: ACC konverzija bez crtice**
```csharp
Input:  "PI102206" + TargetType = 300 (ACC)
Output: "ACC-102206"
Status: ✅ Menja prefix, dodaje crticu
```

### **Test 5: Deposit folderi**
```csharp
Input:  CreateDepositDossierId("500342", "00008", "12345")
Output: "DE-500342-00008_12345"
Status: ✅ Crtica posle DE prefiksa
```

### **Test 6: Deposit parsing (novi format)**
```csharp
Input:  ParseDepositDossierId("DE-500342-00008_12345")
Output: ("500342", "00008", "12345")
Status: ✅ Parsira novi format
```

### **Test 7: Deposit parsing (stari format)**
```csharp
Input:  ParseDepositDossierId("DE500342-00008_12345")
Output: ("500342", "00008", "12345")
Status: ✅ Parsira stari format (backward compatibility)
```

---

## Sledeći Koraci

### Pre Testiranja:
1. ✅ **Build uspešan** - nema compile errora
2. ⏳ **Unit testovi** - testirati sve metode u `DossierIdFormatter`
3. ⏳ **Integration test** - pokrenuti FAZU 2 (DocumentDiscoveryService) na malom skupu

### Pre Production Deploy:
1. ⏳ **Provera DocStaging tabele**:
   ```sql
   SELECT DossierDestFolderId, COUNT(*)
   FROM DocStaging
   GROUP BY DossierDestFolderId
   ORDER BY COUNT(*) DESC
   ```
   - Proveriti koliko foldera već ima crticu
   - Proveriti koliko ih nema crticu

2. ⏳ **Data Migration (opciono)**:
   Ako u bazi već postoje folderi bez crtice, možda treba ažurirati:
   ```sql
   UPDATE DocStaging
   SET DossierDestFolderId =
       CASE
           WHEN DossierDestFolderId LIKE '%-%' THEN DossierDestFolderId
           ELSE STUFF(DossierDestFolderId, 3, 0, '-') -- Ubaci crticu posle 2. karaktera
       END
   WHERE DossierDestFolderId NOT LIKE '%-%'
     AND DossierDestFolderId IS NOT NULL;
   ```

3. ⏳ **FAZA 3 Test**: Proveriti da FolderPreparationService kreira foldere SA crticom

4. ⏳ **FAZA 4 Test**: Proveriti da MoveService koristi ispravan DestinationFolderId

---

## Files Changed

### Izmenjeno:
- `Alfresco.Contracts\Mapper\DossierIdFormatter.cs` (+42 linija izmena)

**Sve promene**: 1 fajl, ~50 linija dokumentacije + 10 linija koda

---

## Build Status

✅ **Compilation: Successful**
- Nema errora
- Samo standardni nullability warnings

---

## Zaključak

Promena je **uspešno primenjena**:
- ✅ Kod **dodaje crticu** gde je potrebno
- ✅ **Backward compatible** (podržava oba formata)
- ✅ **Build uspešan**
- ⏳ Potrebno testiranje na test okruženju

**Preporuka**: Test na malom skupu dokumenata (~100) pre punog deploy-a.
