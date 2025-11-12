# Dosije Depozita (Deposit Dossier) - Dokumentacija

## 📋 Pregled

Ova dokumentacija opisuje kako CA_MockData projekat kreira Dosije Depozita (DE foldere) sa specifičnim formatom imena koji uključuje broj ugovora.

---

## 🏗️ Struktura Foldera

### Format Imena: `DE-{CoreId}-{ContractNumber}`

Gde je:
- **DE** - oznaka za Deposit Dossier (Dosije depozita)
- **{CoreId}** - jedinstveni ID klijenta (npr. 102206)
- **{ContractNumber}** - datum kreiranja dokumenta u formatu **YYYYMMDD** (npr. 20241112)

### Primer:
```
DOSSIERS-DE/
   ├── DE-102206-20241112/
   │   ├── PiAnuitetniPlan.pdf
   │   └── PiObavezniElementiUgovora.pdf
   ├── DE-102211-20240825/
   │   └── SmeUgovorOroceniDepozitPreduzetnici.pdf
   └── DE-102216-20230615/
       ├── PiAnuitetniPlan.pdf
       └── PiObavezniElementiUgovora.pdf
```

---

## 🔄 Logika Kreiranja

### Kada se Kreiraju DE Folderi?

Dosije Depozita folderi se kreiraju **svaki 5-ti folder** (i % 5 == 0):
- Folder #0 → Kreira se DE folder
- Folder #5 → Kreira se DE folder
- Folder #10 → Kreira se DE folder
- itd.

### Proces Kreiranja

1. **Kreiranje DOSSIERS-DE glavnog foldera**
   ```csharp
   var allClientTypes = cfg.ClientTypes.Concat(new[] { "DE" }).ToArray();
   // Kreira DOSSIERS-PI, DOSSIERS-LE, DOSSIERS-DE
   ```

2. **Generisanje Contract Number-a**
   ```csharp
   var contractDate = DateTime.UtcNow.AddDays(-new Random(coreId).Next(1, 365));
   var contractNumber = contractDate.ToString("yyyyMMdd"); // Format: YYYYMMDD
   ```

3. **Kreiranje Foldera**
   ```csharp
   var depositFolderName = $"DE-{coreId}-{contractNumber}";
   // Primer: DE-102206-20241112
   ```

4. **Dodavanje Depozitnih Dokumenata**
   - Za **PI** klijente: PiAnuitetniPlan, PiObavezniElementiUgovora
   - Za **LE** klijente: SmeUgovorOroceniDepozitPreduzetnici

---

## 📄 Depozitni Dokumenti

### Tip 1: Fizička Lica - Depozitni proizvodi (PI)

| Dokument | Šifra | Naziv | Tip |
|----------|-------|-------|-----|
| PiAnuitetniPlan | 00163 | Plan isplate depozita | Dosije depozita |
| PiObavezniElementiUgovora | 00757 | Obavezni elementi ugovora | Dosije depozita |

### Tip 2: SB - Depozitni proizvodi (LE)

| Dokument | Šifra | Naziv | Tip |
|----------|-------|-------|-----|
| SmeUgovorOroceniDepozitPreduzetnici | 00166 | Ugovor o orocenom depozitu | Dosije depozita |

---

## 🏷️ Properties za DE Foldere

### Ključna Polja:

```csharp
{
    "ecm:uniqueFolderId": "DE-102206-20241112",
    "ecm:folderId": "DE-102206-20241112",
    "ecm:bnkDossierType": "Dosije depozita",
    "ecm:coreId": "102206",
    "ecm:bnkNumberOfContract": "20241112",   // ← KRITIČNO: Broj ugovora u formatu YYYYMMDD
    "ecm:source": "DUT",
    "ecm:bnkSource": "DUT",
    "ecm:productType": "00008", // 00008 za PI, 00010 za LE
    "ecm:clientType": "PI",
    "ecm:status": "ACTIVE",
    "ecm:depositProcessedDate": "2024-11-12T00:00:00Z"
}
```

---

## 🏷️ Properties za Depozitne Dokumente

### Ključna Polja:

```csharp
{
    "ecm:docDesc": "PiAnuitetniPlan",        // ← Naziv iz HeimdallDocumentMapper
    "ecm:docType": "00163",                   // ← Šifra dokumenta
    "ecm:docDossierType": "Dosije depozita",
    "ecm:coreId": "102206",
    "ecm:contractNumber": "20241112",         // ← ISTO kao u folderu
    "ecm:source": "DUT",                      // ← VAŽNO: DUT za depozite
    "ecm:status": "validiran",
    "ecm:docClientType": "PI",
    "ecm:docCreationDate": "2024-11-12T00:00:00Z"
}
```

---

## 🎯 Test Scenario

### Scenario: Kreiranje 10 Foldera

```
Folder #0  → PI-102206    + DE-102206-20241112 (PI dokumenti)
Folder #1  → LE-102207    (bez DE)
Folder #2  → PI-102208    (bez DE)
Folder #3  → LE-102209    (bez DE)
Folder #4  → PI-102210    (bez DE)
Folder #5  → LE-102211    + DE-102211-20240825 (LE dokumenti)
Folder #6  → PI-102212    (bez DE)
Folder #7  → LE-102213    (bez DE)
Folder #8  → PI-102214    (bez DE)
Folder #9  → LE-102215    (bez DE)
```

**Rezultat**:
- 10 regularnih PI/LE foldera
- 2 Dosije Depozita foldera (svaki 5-ti)

---

## ⚙️ Konfiguracija

### Config.cs

```csharp
var cfg = new Config()
{
    UseNewFolderStructure = true,
    ClientTypes = new[] { "PI", "LE" }, // DE se dodaje automatski
    StartingCoreId = 102206,
    FolderCount = 10
};
```

---

## 📋 Checklist za Migraciju

- [ ] DOSSIERS-DE folder kreiran
- [ ] DE folderi imaju format `DE-{CoreId}-{ContractNumber}`
- [ ] Contract number je u formatu YYYYMMDD
- [ ] Svi DE folderi imaju `ecm:source = "DUT"`
- [ ] Svi DE folderi imaju `ecm:bnkNumberOfContract` (contract number u formatu YYYYMMDD)
- [ ] Svi depozitni dokumenti imaju `ecm:contractNumber`
- [ ] Depozitni dokumenti mapiraju se kroz HeimdallDocumentMapper
- [ ] ecm:docDesc je postavljen na Naziv iz mappera

---

**Verzija**: 3.0
**Datum**: 2025-11-12
**Feature**: Dosije Depozita sa Contract Number formatom
