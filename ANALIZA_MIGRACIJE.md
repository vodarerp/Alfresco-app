# ANALIZA MIGRACIJE - Test Case Scenariji i Implementacija

**Datum:** 2025-10-29
**Verzija:** 1.0
**Status:** Analiza i preporuke - BEZ implementacije

---

## 📋 SADRŽAJ

1. [Pregled Test Case Scenarija](#pregled-test-case-scenarija)
2. [Trenutno Stanje Implementacije](#trenutno-stanje-implementacije)
3. [Identifikovani Problemi i Nedostaci](#identifikovani-problemi-i-nedostaci)
4. [Finalna Arhitektura i Dizajn](#finalna-arhitektura-i-dizajn)
5. [Hardkodovani Maperi](#hardkodovani-maperi)
6. [Tok Migracije](#tok-migracije)
7. [Prioritizovana Akciona Lista](#prioritizovana-akciona-lista)
8. [Konfiguracija](#konfiguracija)

---

## PREGLED TEST CASE SCENARIJA

### TC 1: Dokumenti sa sufiksom "-migracija" → NEAKTIVNI
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Naziv dokumenta_migracija" sadrži sufiks "migracija", treba da bude migriran kao **neaktivan** (vrednost u Alfresco-u u polju status **"poništen"**)

**Primeri:**
- CoreID 13001926, naziv dokumenta Account Package
- CoreID 13000667, naziv dokumenta Specimen card
- CoreID 50034220, naziv dokumenta Specimen card for LE
- CoreID 50034220, naziv dokumenta Communication Consent

---

### TC 2: Dokumenti BEZ sufiksa "-migracija" → AKTIVNI
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Naziv dokumenta_migracija" NE sadrži sufiks "migracija", treba da bude migriran kao **aktivan** (vrednost u Alfresco-u u polju status **"validiran"**)

**Primeri:**
- CoreID 13001926, naziv dokumenta Current Accounts Contract
- CoreID 50034220, naziv dokumenta Current Account Contract for LE
- CoreID 13001926, naziv dokumenta Account Package RSD Instruction for Resident
- CoreID 13000667, naziv dokumenta Pre-Contract Info

---

### TC 3: Dosije paket računa → 300
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Tip dosijea" ima vrednost **"Dosije paket računa"**, treba da bude migriran u dosije **300 Dosije paket računa**.

**Primeri:**
- CoreID 13001926, naziv dokumenta TEKUCI DEVIZNI RACUN 13001926001
- CoreID 13001926, naziv dokumenta Current Accounts Contract
- CoreID 50034220, naziv dokumenta Current Account Contract for LE
- CoreID 13000667, naziv dokumenta Specimen card
- CoreID 50034220, naziv dokumenta Specimen card for LE

---

### TC 4: Dosije klijenta FL/PL → 500 ili 400
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Tip dosijea" ima vrednost **"Dosije klijenta FL/PL"**, treba da bude migriran u odgovarajući dosije **500 Dosije fizičkog lica** ili dosije **400 Dosije pravnog lica**, u zavisnosti od toga da li je reč o fizičkom licu ili biznis klijentu (očekivanje je da će se na osnovu dodatnih atributa CoreID i segment/tip klijenta).

**Primeri:**
- CoreID 50034220, Communication Consent (u LE folderu) → **400 Dosije pravnog lica**
- CoreID 50034141, Personal Notice (u LE folderu) → **400 Dosije pravnog lica**
- CoreID 102206, KYC Questionnaire MDOC (u PI folderu) → **500 Dosije fizičkog lica**

**Napomena:**
Da li je dokument migriran u folder PI ili LE, može takođe da bude od pomoći radi migriranja u ispravan dosije 500 ili 400.

---

### TC 5: Dosije klijenta PL → 400
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Tip dosijea" ima vrednost **"Dosije klijenta PL"** (dakle, ne sadrži deo FL), treba da bude migriran u dosije **400 Dosije pravnog lica**.

**Primeri:**
- CoreID 50034220, naziv dokumenta KYC Questionnaire for LE

---

### TC 6: Source = "Heimdall"
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Tip dosijea" ima vrednost **Dosije paket računa** ili **Dosije fizičkog lica** ili **Dosije pravnog lica** ili **Dosije ostalo** treba da ima source **"Heimdall"** (vrednost u Alfresco-u u polju izvor).

---

### TC 7: Source = "DUT"
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Tip dosijea" ima vrednost **Dosije depozita** treba da ima source **"DUT"** (vrednost u Alfresco-u u polju izvor).

---

### TC 8: Kreiranje dosijea ako NE postoji
**Zahtev:**
Ukoliko u novom Alfresco-u **NE postoji** kreiran adekvatan dosije, potrebno je najpre kreirati odgovarajući dosije klijenta. Klijentske podatke na dosijeu (atributi dosijea) popuniti pozivom **ClientAPI-a**. Nakon što se kreira dosije potrebno je migrirati odgovarajući dokument (jedan ili više njih) u odgovarajući dosije. Zajedno sa dokumentom potrebno je migrirati i njegove podatke/atribute iz starog Alfresco-a.

---

### TC 9: Dosije postoji → samo migriraj dokumente
**Zahtev:**
Ukoliko u novom Alfresco-u **postoji** kreiran adekvatan dosije, u tom slučaju **NIJE potrebno** kreirati odgovarajući dosije klijenta, već je potrebno migrirati odgovarajući dokument (jedan ili više njih) u odgovarajući dosije. Zajedno sa dokumentom potrebno je migrirati i njegove podatke/atribute iz starog Alfresco-a.

---

### TC 10: Migracija svih verzija dokumenta
**Zahtev:**
Ukoliko za istog klijenta postoji više verzija istog dokumenta, potrebno je **migrirati sve verzije** tog dokumenta.

**Primeri:**
- Personal Notice, KYC Questionnaire, Communication Consent, Account Package, Specimen card

---

### TC 11: Dokumenti koji su već neaktivni
**Zahtev:**
Očekivanje je da prilikom migracije ne postoje dokumenta koja imaju status neaktivan (vrednost u Alfresco-u u polju status "poništen"), naime, očekivanje Biznisa je da su sva dokumenta aktivna (jer ne postoji frontend da bi ih neko učinio neaktivnim).
Ukoliko ipak postoji ovakva dokumenta, ona takođe treba da bude **migirana kao neaktivan**.

---

### TC 12: Politika čuvanja "nova verzija" za 00099
**Zahtev:**
Ukoliko u novom Alfresco-u na novi način (dakle, deo koji nije predmet migracije, ali je definisano specifikacijom) postoji dokument kojem je politika čuvanja **"nova verzija"**, treba da se označi da je **neaktivan** (bilo da postoji jedan ili više ovih dokumenata).

**Primer:** dokument tipa **00099 KDP za fizička lica**.

**Napomena:** Ovo je bitno zbog finalnog statusa.

---

### TC 13: Politika čuvanja "novi dokument" za 00101
**Zahtev:**
Ukoliko u novom Alfresco-u na novi način (dakle, deo koji nije predmet migracije, ali je definisano specifikacijom) postoje dokumenta **00101 KDP za ovlašćena lica (za fizička lica)** kojem je politika čuvanja **"novi dokument"**, treba da se označi da je **neaktivan** (bilo da postoji jedan ili više ovih dokumenata).

**Napomena:** Ovo je bitno zbog finalnog statusa.

---

### TC 14: Politika čuvanja "nova verzija" za 00100
**Zahtev:**
Ukoliko u novom Alfresco-u na novi način (dakle, deo koji nije predmet migracije, ali je definisano specifikacijom) postoje dokumenta **00100 KDP za pravna lica_iz aplikacije** kojem je politika čuvanja **"nova verzija"**, treba da se označi da je **neaktivan** (bilo da postoji jedan ili više ovih dokumenata).

**Napomena:** Ovo je bitno zbog finalnog statusa.

---

### TC 15: Isključivanje dokumenta 00702
**Zahtev:**
Među migriranim dokumentima sa novog Alfresco-a **ne treba da se nađe** dokument **00702 Ovlašćenje licima za donošenje instrumenata PP-a u Banku** (ovo je više test case za Banku).

---

### TC 16: Broj računa za KDP vlasnika FL (00824)
**Zahtev:**
Za tip dokumenata **KDP vlasnika za FL 00824**, potrebno je nakon migracije iz svih izvora, ako zadovoljava uslov da budu proglašena aktivnim, treba da **postoji popunjen broj računa**.

**Napomena:** Ovo je bitno zbog finalnog statusa.

---

### TC 17: Dosije depozita → 700
**Zahtev:**
Svaki dokument koji je mapiran tako da u koloni "Tip dosijea" ima vrednost **Dosije depozita**, treba da bude migriran u dosije **700 Dosije depozita**.

**Primeri:**
- PiVazeciUgovorOroceniDepozitDvojezicniRSD
- ZahtevZaOtvaranjeRacunaOrocenogDepozita

---

### TC 18: Format jedinstvenog identifikatora za dosije depozita
**Zahtev:**
Za dosije depozita, jedinstveni identifikator treba da bude formiran u formatu:
**`DE-<CoreId>-<SifraTipaProizvoda>-<brojUgovora>`**

**Primer:** `DE-10194302-00008-10104302_20241105154459`

---

### TC 19: Format identifikatora za dosije fizičkog lica
**Zahtev:**
Za dosije fizičkog lica, jedinstveni identifikator treba da bude formiran u formatu:
**`PI-<CoreId>`**

---

### TC 20: Format identifikatora za dosije pravnog lica
**Zahtev:**
Za dosije pravnog lica, jedinstveni identifikator treba da bude formiran u formatu:
**`LE-<CoreId>`**

---

### TC 21: Format identifikatora za dosije paket računa
**Zahtev:**
Za dosije paket računa, jedinstveni identifikator treba da bude formiran u formatu:
**`ACC-<CoreId>`**

---

### TC 22: Migracija svih verzija za dosije depozita
**Zahtev:**
Za dosije depozita, za istog klijenta postoji više verzija istog dokumenta, potrebno je **migrirati sve verzije** tog dokumenta. Dakle, neophodna je migracija svih verzija konkretnog dokumenta za konkretno oročenje, a ne samo zadnje verzije dokumenta.

---

### TC 23: DUT status "Booked"
**Zahtev:**
Za dosije depozita, migraciju je potrebno sprovesti **samo za dokumentaciju** koja u OfferBO tabeli, u okviru DUT aplikacije, ima status **"Booked"**.

---

### TC 24: Minimalni set dokumenata za deposit
**Zahtev:**
Za dosije depozita, u okviru jedinstvenog identifikatora dosijea može biti samo jedno oročenje koje treba da sadrži **minimum sledeća dokumenta**:

**Za Fizička lica - Depozitni proizvodi (00008):**
- Ugovor o oročenom depozitu
- Ponuda
- Plan isplate depozita
- Obavezni elementi Ugovora

**Za SB - Depozitni proizvodi (00010):**
- Ugovor o oročenom depozitu
- Ponuda
- Plan isplate depozita
- Obavezni elementi Ugovora

---

### TC 25: Status migrirane dokumentacije za depozite
**Zahtev:**
Za dosije depozita, status migrirane dokumentacije treba da bude **"Aktivan"**.

---

## TRENUTNO STANJE IMPLEMENTACIJE

### ✅ ŠTA TRENUTNO FUNKCIONIŠE

#### 1. Infrastruktura Worker-a
- `FolderDiscoveryWorker`, `DocumentDiscoveryWorker`, `MoveWorker` su dobro implementirani
- Automatsko stopiranje kada nema više posla
- Progress tracking i UI integracija
- Lokacija: `Migration.Workers/`

#### 2. Jedinstveni identifikatori (TC 18-21)
**Lokacija:** `Migration.Infrastructure/Implementation/UniqueFolderIdentifierService.cs`

✅ **Implementirani formati:**
- `DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}` za depozite
- `PI-{CoreId}` za fizička lica (implicitno)
- `LE-{CoreId}` za pravna lica (implicitno)
- `ACC-{CoreId}` za paket računa (implicitno)

✅ **Metode:**
- `GenerateDepositIdentifier()` - generiše DE format
- `GenerateFolderReference()` - generiše folder referencu
- `ParseIdentifier()` - parsira postojeći identifikator
- `IsValidIdentifier()` - validacija formata

#### 3. DUT integracija (TC 23, 43)
**Lokacija:**
- `Migration.Abstraction/Interfaces/IDutApi.cs`
- `Migration.Abstraction/Models/DutModels.cs`
- `Migration.Infrastructure/Implementation/DutApi.cs`

✅ **Implementirano:**
- `IDutApi` interfejs sa svim potrebnim metodama
- `DutOffer`, `DutDocument`, `DutOfferDetails` modeli
- `GetBookedOffersAsync()` - dohvata samo "Booked" offers
- `IsOfferBookedAsync()` - validacija statusa

#### 4. ClientAPI integracija (TC 8, 9)
**Lokacija:**
- `Migration.Abstraction/Interfaces/IClientApi.cs`
- `Migration.Infrastructure/Implementation/ClientApi.cs`

✅ **Implementirano:**
- `IClientApi` interfejs
- `GetClientDetailExtendedAsync()` metoda
- `EnrichFoldersWithClientDataAsync()` u `FolderDiscoveryService`

#### 5. Document Type Transformation
**Lokacija:** `Migration.Infrastructure/Implementation/DocumentTypeTransformationService.cs`

✅ **Implementirano:**
- TypeMappings dictionary:
  ```csharp
  { "00824", "00099" },  // KDP FL
  { "00825", "00101" },  // KDP ovlašćena lica
  { "00827", "00100" },  // KDP PL
  { "00841", "00130" }   // KYC upitnik
  ```
- `DetermineDocumentTypesAsync()` metoda
- `TransformActiveDocumentsAsync()` metoda
- `HasVersioningPolicy()` provera

---

### ❌ ŠTA NEDOSTAJE - KRITIČNI NEDOSTACI

#### 1. Mapiranje statusa dokumenata (TC 1-2)
**Problem:**
Trenutna implementacija ne proverava **naziv dokumenta** već samo **tip dokumenta**.

**Nedostaje:**
- Provera naziva dokumenta sa sufiksom "-migracija"
- Mapiranje Excel kolone "Naziv dokumenta_migracija" → Alfresco property
- Setovanje statusa: "validiran" vs "poništen"

**Fajlovi za izmenu:**
- `DocumentTypeTransformationService.cs`

---

#### 2. Mapiranje tipova dosijea (TC 3-5)
**Problem:**
Nema mapiranja iz Excel kolone "Tip dosijea" → destination folder u novom Alfresco-u.

**Nedostaje:**
1. Logika za detekciju tipa dosijea iz property-ja `bank:tipDosijea`
2. Mapiranje "Dosije paket računa" → 300
3. Mapiranje "Dosije klijenta FL/PL" + segment → 400 ili 500
4. Mapiranje "Dosije klijenta PL" → 400
5. Mapiranje "Dosije depozita" → 700
6. Hardkodovani folder ID-ovi ili logika kreiranja foldera 300, 400, 500, 700

**Fajlovi za kreiranje:**
- `DossierTypeDetector.cs` (novi fajl)
- Config u `appsettings.json`

---

#### 3. Source atribut (TC 6-7)
**Problem:**
Nema setovanja `Source` atributa tokom migracije.

**Nedostaje:**
1. Property `Source` na `DocStaging` model
2. Logika: Heimdall za dosijee 300/400/500, DUT za 700
3. Čitanje iz Alfresco custom property `bank:source`
4. Fallback na ClientAPI ako nedostaje

**Fajlovi za izmenu:**
- `DocStaging` model
- `DocumentDiscoveryService.cs`
- `MoveService.cs`

---

#### 4. Provera postojanja dosijea (TC 8-9)
**Problem:**
Nema provere da li dosije već postoji u novom Alfresco-u pre migracije.

**Nedostaje:**
1. Poziv Alfresco API: "Da li postoji folder PI-{CoreId} u folderu 500?"
2. Ako NE postoji: kreiraj → ClientAPI → popuni atribute
3. Ako postoji: samo migriraj dokumente

**Fajlovi za kreiranje:**
- `FolderCreationService.cs` (novi servis)
- Izmene u `MoveService.cs`

---

#### 5. Rukovanje verzijama dokumenata (TC 10, 22)
**Problem:**
Nema logike za migraciju više verzija istog dokumenta.

**Nedostaje:**
1. Grupisanje dokumenata po CoreID + DocumentType
2. Sortiranje po datumu kreiranja
3. Alfresco API poziv: `POST /nodes/{nodeId}/versions` umesto `POST /nodes/{parentId}/children`
4. Migracija kao verzije istog dokumenta, ne kao odvojeni dokumenti

**Fajlovi za izmenu:**
- `DocumentDiscoveryService.cs`
- `MoveService.cs`

---

#### 6. Status u starom sistemu (TC 11)
**Problem:**
Ne čita se `status` property iz starog Alfresco-a.

**Nedostaje:**
1. Čitanje `bank:status` property prilikom discovery-ja
2. Mapiranje starog statusa → novi status
3. Ako je bio "poništen" → ostaje "poništen"

**Fajlovi za izmenu:**
- `DocumentDiscoveryService.cs`

---

#### 7. Finalni status cleanup (TC 12-14)
**Problem:**
Nakon migracije, stari dokumenti tipa 00099, 00100, 00101 u novom sistemu treba da postanu neaktivni.

**Nedostaje:**
1. Cleanup faza nakon migracije
2. Query: nađi dokumente 00099, 00100, 00101 koji NISU došli iz migracije
3. Označi ih kao neaktivne

**Fajlovi za kreiranje:**
- `PostMigrationCleanupService.cs` (novi servis)

---

#### 8. Excluded document types (TC 15)
**Problem:**
Nema filtera koji isključuje dokument 00702.

**Nedostaje:**
1. Config: `ExcludedDocumentTypes: ["00702"]`
2. Filter u `DocumentDiscoveryService`: `WHERE document_type NOT IN ('00702')`

**Fajlovi za izmenu:**
- `appsettings.json`
- `DocumentDiscoveryService.cs`

---

#### 9. Validacija broja računa (TC 16)
**Problem:**
Za dokumente tipa 00824 (KDP vlasnika za FL), nema validacije broja računa.

**Nedostaje:**
1. Poziv `ClientAPI.GetActiveAccountsEndpoint`
2. Validacija: ako je 00824 i postaje aktivan → mora imati broj računa
3. Ako nema račun → reportuj warning ili zadrži neaktivnim

**Fajlovi za kreiranje:**
- `AccountValidationService.cs` (novi servis)

---

#### 10. DUT integracija u discovery (TC 23)
**Problem:**
`IDutApi` postoji ali se ne koristi tokom discovery-ja dokumenata.

**Nedostaje:**
1. Poziv `DutApi.IsOfferBookedAsync(offerId)` pre migracije deposit dokumenta
2. Skip dokumenata koji nisu "Booked"

**Fajlovi za izmenu:**
- `DocumentDiscoveryService.cs`

---

#### 11. Validacija kompletnosti deposit dokumenata (TC 24)
**Problem:**
Nema provere da li deposit dosije ima sva 4 obavezna dokumenta.

**Nedostaje:**
1. Pre zatvaranja deposit dosijea: validacija
2. Lista obaveznih: Ugovor, Ponuda, Plan isplate, Obavezni elementi
3. Log warning ako neki nedostaje

**Fajlovi za kreiranje:**
- `DepositValidationService.cs` (novi servis)

---

## IDENTIFIKOVANI PROBLEMI I NEDOSTACI

### Problem 1: CSV mapiranje nije kompletno
**Lokacija fajla:** `maprinje_migracija.csv`

**Nedostajući mapovi:**
```
PiPonuda
PiAnuitetniPlan
PiObavezniElementiUgovora
ZahtevZaOtvaranjeRacunaOrocenogDepozita
PiVazeciUgovorOroceniDepozitOstaleValute
PiVazeciUgovorOroceniDepozitDinarskiTekuci
PiVazeciUgovorOroceniDepozitNa36Meseci
PiVazeciUgovorOroceniDepozitNa24MesecaRSD
PiVazeciUgovorOroceniDepozitNa25Meseci
```

**Rešenje:**
Migriraj u dosije 700 (Dosije depozita) ili u 999 (Unknown).

---

### Problem 2: "null null" dokumenti
12 dokumenata sa oznakom "null null" u CSV-u - nema dovoljno informacija.

**Rešenje:**
Skip sa detaljnim logom ili migriraj u Unknown folder.

---

### Problem 3: Trailing spaces u CSV-u
```
"Dosije paket racuna " vs "Dosije paket racuna"
```

**Rešenje:**
Normalizuj stringove sa `.Trim()` pre poređenja.

---

### Problem 4: Nedostaje provera da li folderi 300/400/500/700 postoje
**Rešenje:**
Pre migracije, proveri i kreiraj foldere ako ne postoje.

---

## FINALNA ARHITEKTURA I DIZAJN

### Princip: Hardcode + Config

| Aspekt | Hardcode u kodu | Config (appsettings.json) |
|--------|----------------|---------------------------|
| **Tipovi dosijea** (300/400/500/700/999) | ✅ Enum vrednosti | ❌ |
| **Pravila detekcije dosijea** | ✅ Biznis logika | ❌ |
| **Folder ID-ovi u novom Alfresco-u** | ❌ | ✅ Environment-specific |
| **Status pravila** (migracija suffix) | ✅ Biznis logika | ❌ |
| **Source pravila** (Heimdall/DUT) | ✅ Biznis logika | ❌ |
| **Mapiranje naziva dokumenata** | ✅ Hardkodovani Dictionary | ❌ |
| **Mapiranje šifri dokumenata** | ✅ Hardkodovani Dictionary | ❌ |
| **Deposit patterns** | ❌ | ✅ Lista stringova |
| **Excluded types** (00702) | ❌ | ✅ Lista stringova |
| **Unknown document handling** | ❌ | ✅ Strategy + opcije |

---

### Enum: DossierType

```csharp
public enum DossierType
{
    ClientFL = 500,           // Dosije fizičkog lica
    ClientPL = 400,           // Dosije pravnog lica
    AccountPackage = 300,     // Dosije paket računa
    Deposit = 700,            // Dosije depozita
    ClientFLorPL = -1,        // Privremeno - čeka segment iz ClientAPI
    Other = -2,               // Dosije ostalo - čeka dodatne informacije
    Unknown = 999             // Nepoznato
}
```

---

## HARDKODOVANI MAPERI

### 1. DocumentNameMapper

**Lokacija:** `Migration.Infrastructure/Implementation/DocumentNameMapper.cs` (NOVI FAJL)

```csharp
public static class DocumentNameMapper
{
    /// <summary>
    /// Mapiranje: originalni naziv → naziv sa sufiksom "-migracija"
    /// Dokumenti u ovom dictionary-u će biti migrirani kao NEAKTIVNI (status "poništen")
    /// Dokumenti koji NISU u ovom dictionary-u ostaju aktivni (status "validiran")
    /// </summary>
    private static readonly Dictionary<string, string> NameMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Srpski nazivi
        { "GDPR saglasnost", "GDPR saglasnost - migracija" },
        { "KYC upitnik", "KYC upitnik - migracija" },
        { "Izjava o kanalima komunikacije", "Izjava o kanalima komunikacije - migracija" },
        { "Izjava o pristupu", "Izjava o pristupu - migracija" },
        { "Izjava o sprečavanju pranja novca", "Izjava o sprečavanju pranja novca - migracija" },
        { "Pristanak za obradu ličnih podataka", "Pristanak za obradu ličnih podataka - migracija" },
        { "KDP za fizička lica", "KDP za fizička lica - migracija" },
        { "KDP za pravna lica_iz aplikacije", "KDP za pravna lica - migracija" },
        { "KDP za ovlašćena lica (za fizička lica)", "KDP za ovlašćena lica (za fizička lica) - migracija" },
        { "Zahtev za otvaranje/izmenu paket računa", "Zahtev za otvaranje/izmenu paket računa - migracija" },
        { "Obaveštenje o predugovornoj fazi", "Obaveštenje o predugovornoj fazi - migracija" },
        { "GL transakcije", "GL transakcije - migracija" },
        { "Zahtev za izmenu SMS info servisa", "Zahtev za izmenu SMS info servisa - migracija" },
        { "Zahtev za izmenu SMS CA servisa", "Zahtev za izmenu SMS CA servisa - migracija" },
        { "FX transakcije", "FX transakcije - migracija" },
        { "GDPR povlačenje saglasnosti", "GDPR povlačenje saglasnosti - migracija" },
        { "Zahtev za promenu email adrese putem mBankinga", "Zahtev za promenu email adrese putem mBankinga - migracija" },
        { "Zahtev za promenu broja telefona putem mBankinga", "Zahtev za promenu broja telefona putem mBankinga - migracija" },

        // Engleski nazivi (iz starog Alfresco-a)
        { "Personal Notice", "GDPR saglasnost - migracija" },
        { "KYC Questionnaire", "KYC upitnik - migracija" },
        { "KYC Questionnaire MDOC", "KYC upitnik - migracija" },
        { "Communication Consent", "Izjava o kanalima komunikacije - migracija" },
        { "Specimen card", "KDP za fizička lica - migracija" },
        { "Specimen card for LE", "KDP za pravna lica - migracija" },
        { "Specimen Card for Authorized Person", "KDP za ovlašćena lica (za fizička lica) - migracija" },
        { "Account Package", "Zahtev za otvaranje/izmenu paket računa - migracija" },
        { "Pre-Contract Info", "Obaveštenje o predugovornoj fazi - migracija" },
        { "GL Transaction", "GL transakcije - migracija" },
        { "SMS info modify request", "Zahtev za izmenu SMS info servisa - migracija" },
        { "SMS card alarm change", "Zahtev za izmenu SMS CA servisa - migracija" },
        { "FX Transaction", "FX transakcije - migracija" },
        { "GDPR Revoke", "GDPR povlačenje saglasnosti - migracija" },
        { "Contact Data Change Email", "Zahtev za promenu email adrese putem mBankinga - migracija" },
        { "Contact Data Change Phone", "Zahtev za promenu broja telefona putem mBankinga - migracija" }
    };

    public static string GetMigratedName(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName))
            return originalName;

        return NameMappings.TryGetValue(originalName.Trim(), out var migratedName)
            ? migratedName
            : originalName;
    }

    public static bool WillReceiveMigrationSuffix(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName))
            return false;

        return NameMappings.ContainsKey(originalName.Trim());
    }
}
```

---

### 2. DocumentCodeMapper

**Lokacija:** `Migration.Infrastructure/Implementation/DocumentCodeMapper.cs` (NOVI FAJL)

```csharp
public static class DocumentCodeMapper
{
    /// <summary>
    /// Mapiranje: originalna šifra → nova šifra
    /// Ako se šifra NE MENJA, mapiranje pokazuje na istu vrednost
    /// </summary>
    private static readonly Dictionary<string, string> CodeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Šifre koje SE MENJAJU
        { "00253", "00849" },  // GDPR saglasnost
        { "00130", "00841" },  // KYC upitnik
        { "00141", "00842" },  // Izjava o kanalima komunikacije
        { "00099", "00824" },  // KDP za fizička lica
        { "00100", "00827" },  // KDP za pravna lica
        { "00101", "00825" },  // KDP za ovlašćena lica
        { "00102", "00834" },  // Zahtev za otvaranje/izmenu paket računa
        { "00109", "00838" },  // Obaveštenje o predugovornoj fazi
        { "00143", "00844" },  // GL transakcije
        { "00103", "00835" },  // Zahtev za izmenu SMS info servisa
        { "00104", "00836" },  // Zahtev za izmenu SMS CA servisa
        { "00142", "00843" },  // FX transakcije
        { "00121", "00840" },  // GDPR povlačenje saglasnosti
        { "00156", "00847" },  // Zahtev za promenu email adrese
        { "00155", "00846" },  // Zahtev za promenu broja telefona

        // Šifre koje se NE MENJAJU
        { "00135", "00135" },
        { "00139", "00845" },
        { "00889", "00889" },
        { "00879", "00879" },
        { "00882", "00882" },
        { "00890", "00890" },
        { "00891", "00891" },
        { "00892", "00892" },
        { "00886", "00886" },
        { "00887", "00887" },
        { "00581", "00581" },
        { "00584", "00584" },
        { "00439", "00439" },
        { "00438", "00438" },
        { "00473", "00473" },
        { "00472", "00472" },
        { "00136", "00136" },
        { "00493", "00493" },
        { "00494", "00494" },
        { "00582", "00582" },
        { "00583", "00583" },
        { "00660", "00660" },
        { "00661", "00661" },
        { "00662", "00662" },
        { "00663", "00663" },
        { "00664", "00664" },
        { "00665", "00665" },
        { "00666", "00666" },
        { "00667", "00667" },
        { "00668", "00668" },
        { "00669", "00669" },
        { "02756", "02756" },
        { "02757", "02757" },
        { "02758", "02758" },
        { "00110", "00110" },
        { "00117", "00117" },
        { "00122", "00122" },
        { "00124", "00124" },
        { "00125", "00125" },
        { "00233", "00233" },
        { "00113", "00113" },
        { "00241", "00241" },
        { "00138", "00138" },
        { "00178", "00178" },
        { "00133", "00133" },
        { "00134", "00134" },
        { "00237", "00237" },
        { "00123", "00123" },
        { "00766", "00766" },
        { "00105", "00105" },
        { "00129", "00129" },
        { "00128", "00128" },
        { "00127", "00127" },
        { "00137", "00137" },
        { "00132", "00132" }
    };

    public static string GetMigratedCode(string originalCode)
    {
        if (string.IsNullOrWhiteSpace(originalCode))
            return originalCode;

        return CodeMappings.TryGetValue(originalCode.Trim(), out var migratedCode)
            ? migratedCode
            : originalCode;
    }

    public static bool CodeWillChange(string originalCode)
    {
        if (string.IsNullOrWhiteSpace(originalCode))
            return false;

        var trimmed = originalCode.Trim();
        return CodeMappings.TryGetValue(trimmed, out var newCode) && newCode != trimmed;
    }
}
```

---

### 3. DossierTypeDetector

**Lokacija:** `Migration.Infrastructure/Implementation/DossierTypeDetector.cs` (NOVI FAJL)

```csharp
public static class DossierTypeDetector
{
    /// <summary>
    /// Određuje tip dosijea na osnovu "Tip dosijea" property-ja iz Alfresco-a
    /// NE koristi folder path jer nije pouzdan indikator
    /// </summary>
    public static DossierType DetectFromTipDosijea(string? tipDosijea)
    {
        if (string.IsNullOrWhiteSpace(tipDosijea))
            return DossierType.Unknown;

        var normalized = tipDosijea.Trim().ToLowerInvariant();

        // TC 3: Dosije paket računa → 300
        if (normalized.Contains("dosije paket racuna") ||
            normalized.Contains("dosije paket računa"))
            return DossierType.AccountPackage;

        // TC 4: Dosije klijenta FL/PL → zavisi od segmenta
        if (normalized.Contains("dosije klijenta fl / pl") ||
            normalized.Contains("dosije klijenta fl/pl"))
        {
            return DossierType.ClientFLorPL; // Čeka ClientAPI segment
        }

        // TC 5: Dosije klijenta PL (samo PL, bez FL) → 400
        if (normalized.Contains("dosije klijenta pl") &&
            !normalized.Contains("fl"))
            return DossierType.ClientPL;

        // TC 17: Dosije depozita → 700
        if (normalized.Contains("dosije depozita"))
            return DossierType.Deposit;

        if (normalized.Contains("dosije ostalo"))
            return DossierType.Other;

        return DossierType.Unknown;
    }

    /// <summary>
    /// Razrešava FL/PL na osnovu segment/tip klijenta iz ClientAPI-a
    /// </summary>
    public static DossierType ResolveFLorPL(string clientSegment)
    {
        var normalized = clientSegment?.Trim().ToUpperInvariant();

        // PI = Personal Individual = Fizičko lice
        if (normalized == "PI" || normalized == "RETAIL" || normalized == "FL")
            return DossierType.ClientFL; // 500

        // LE = Legal Entity = Pravno lice
        if (normalized == "LE" || normalized == "SME" || normalized == "CORPORATE" || normalized == "PL")
            return DossierType.ClientPL; // 400

        return DossierType.Unknown;
    }
}
```

---

### 4. DocumentStatusDetector

**Lokacija:** `Migration.Infrastructure/Implementation/DocumentStatusDetector.cs` (NOVI FAJL)

```csharp
public static class DocumentStatusDetector
{
    /// <summary>
    /// Određuje da li dokument treba da bude aktivan NAKON migracije
    /// Logika:
    /// - TC 1: Ako dokument dobija sufiks "-migracija" → NEAKTIVAN (status "poništen")
    /// - TC 2: Ako dokument NE dobija sufiks → AKTIVAN (status "validiran")
    /// - TC 11: Ako je dokument već bio neaktivan u starom sistemu → ostaje NEAKTIVAN
    /// </summary>
    public static bool ShouldBeActiveAfterMigration(
        string originalDocumentName,
        string? existingStatus = null)
    {
        // TC 11: Provera starog statusa
        if (!string.IsNullOrWhiteSpace(existingStatus))
        {
            var normalized = existingStatus.Trim().ToLowerInvariant();
            if (normalized == "poništen" ||
                normalized == "inactive" ||
                normalized == "cancelled" ||
                normalized == "canceled")
                return false;
        }

        // TC 1 & 2: Provera da li dobija sufiks "-migracija"
        bool willReceiveSuffix = DocumentNameMapper.WillReceiveMigrationSuffix(originalDocumentName);

        return !willReceiveSuffix;
    }

    public static string GetAlfrescoStatus(bool isActive)
    {
        return isActive ? "validiran" : "poništen";
    }

    /// <summary>
    /// Vraća kompletne informacije o migraciji dokumenta
    /// </summary>
    public static DocumentMigrationInfo GetMigrationInfo(
        string originalName,
        string? originalCode = null,
        string? existingStatus = null)
    {
        var newName = DocumentNameMapper.GetMigratedName(originalName);
        var newCode = originalCode != null
            ? DocumentCodeMapper.GetMigratedCode(originalCode)
            : null;

        var isActive = ShouldBeActiveAfterMigration(originalName, existingStatus);
        var status = GetAlfrescoStatus(isActive);

        var willReceiveSuffix = DocumentNameMapper.WillReceiveMigrationSuffix(originalName);
        var codeWillChange = originalCode != null && DocumentCodeMapper.CodeWillChange(originalCode);

        return new DocumentMigrationInfo
        {
            OriginalName = originalName,
            NewName = newName,
            OriginalCode = originalCode,
            NewCode = newCode,
            IsActive = isActive,
            Status = status,
            WillReceiveMigrationSuffix = willReceiveSuffix,
            CodeWillChange = codeWillChange
        };
    }
}

public record DocumentMigrationInfo
{
    public string OriginalName { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string? OriginalCode { get; init; }
    public string? NewCode { get; init; }
    public bool IsActive { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool WillReceiveMigrationSuffix { get; init; }
    public bool CodeWillChange { get; init; }
}
```

---

### 5. SourceDetector

**Lokacija:** `Migration.Infrastructure/Implementation/SourceDetector.cs` (NOVI FAJL)

```csharp
public static class SourceDetector
{
    /// <summary>
    /// Određuje source atribut na osnovu tipa dosijea
    /// TC 6: Heimdall za dosijee 300, 400, 500
    /// TC 7: DUT za dosije 700
    /// </summary>
    public static string GetSource(DossierType dossierType)
    {
        if (dossierType == DossierType.Deposit)
            return "DUT";

        return "Heimdall";
    }
}
```

---

## TOK MIGRACIJE

### Faza 1: FolderDiscovery

```
┌─────────────────────────────────────────────────────────┐
│ FolderDiscoveryService čita folder iz starog Alfresco   │
└─────────────────────────┬───────────────────────────────┘
                          │
                          ▼
          ┌───────────────────────────────────┐
          │ Čita custom properties:           │
          │ - bank:tipDosijea                 │
          │ - bank:source                     │
          │ - bank:clientSegment              │
          │ - bank:coreId                     │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Da li nedostaju properties?       │
          └───────────┬───────────────────────┘
                      │
         ┌────────────┴────────────┐
         │ DA                      │ NE
         ▼                         ▼
┌────────────────────┐    ┌────────────────────┐
│ Pozovi ClientAPI   │    │ Nastavi direktno   │
│ GetClientDetail    │    │                    │
│ Extended           │    │                    │
└────────┬───────────┘    └────────┬───────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
          ┌───────────────────────────────────┐
          │ DossierTypeDetector               │
          │ .DetectFromTipDosijea()           │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Da li je ClientFLorPL?            │
          └───────────┬───────────────────────┘
                      │
         ┌────────────┴────────────┐
         │ DA                      │ NE
         ▼                         ▼
┌────────────────────┐    ┌────────────────────┐
│ ResolveFLorPL()    │    │ Nastavi            │
│ na osnovu segmenta │    │                    │
└────────┬───────────┘    └────────┬───────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
          ┌───────────────────────────────────┐
          │ Sačuvaj u FOLDER_STAGING:         │
          │ - TipDosijea                      │
          │ - TargetDossierType (enum)        │
          │ - ClientSegment                   │
          │ - CoreId                          │
          └───────────────────────────────────┘
```

---

### Faza 2: DocumentDiscovery

```
┌─────────────────────────────────────────────────────────┐
│ DocumentDiscoveryService čita dokument                  │
└─────────────────────────┬───────────────────────────────┘
                          │
                          ▼
          ┌───────────────────────────────────┐
          │ Čita custom properties:           │
          │ - bank:nazivDokumenta             │
          │ - bank:tipDokumenta               │
          │ - bank:status                     │
          │ - bank:tipDosijea                 │
          │ - bank:source                     │
          │ - bank:clientSegment              │
          │ - bank:coreId                     │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Da li nedostaju properties?       │
          └───────────┬───────────────────────┘
                      │
         ┌────────────┴────────────┐
         │ DA                      │ NE
         ▼                         ▼
┌────────────────────┐    ┌────────────────────┐
│ Pozovi ClientAPI   │    │ Nastavi            │
└────────┬───────────┘    └────────┬───────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
          ┌───────────────────────────────────┐
          │ DossierTypeDetector               │
          │ .DetectFromTipDosijea()           │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Da li je ClientFLorPL?            │
          └───────────┬───────────────────────┘
                      │
         ┌────────────┴────────────┐
         │ DA                      │ NE
         ▼                         ▼
┌────────────────────┐    ┌────────────────────┐
│ ResolveFLorPL()    │    │ Nastavi            │
└────────┬───────────┘    └────────┬───────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
          ┌───────────────────────────────────┐
          │ DocumentStatusDetector            │
          │ .GetMigrationInfo()               │
          │ - Mapira naziv                    │
          │ - Mapira šifru                    │
          │ - Određuje status                 │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ SourceDetector.GetSource()        │
          │ - Heimdall ili DUT                │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Sačuvaj u DOC_STAGING:            │
          │ - OriginalName                    │
          │ - NewName                         │
          │ - OriginalCode                    │
          │ - NewCode                         │
          │ - IsActive                        │
          │ - Status                          │
          │ - Source                          │
          │ - TipDosijea                      │
          │ - TargetDossierType               │
          │ - OriginalCreatedAt               │
          └───────────────────────────────────┘
```

---

### Faza 3: Move (Migracija)

```
┌─────────────────────────────────────────────────────────┐
│ MoveService čita batch iz DOC_STAGING                   │
└─────────────────────────┬───────────────────────────────┘
                          │
                          ▼
          ┌───────────────────────────────────┐
          │ Grupiši po TargetDossierType      │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Za svaki dossier type:            │
          │ - Pročitaj NodeId iz config-a     │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Da li folder postoji?             │
          │ (provera u novom Alfresco-u)      │
          └───────────┬───────────────────────┘
                      │
         ┌────────────┴────────────┐
         │ NE                      │ DA
         ▼                         ▼
┌────────────────────┐    ┌────────────────────┐
│ Kreiraj folder     │    │ Nastavi            │
│ Pozovi ClientAPI   │    │                    │
│ Popuni atribute    │    │                    │
└────────┬───────────┘    └────────┬───────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
          ┌───────────────────────────────────┐
          │ Da li dosije CoreId postoji?      │
          │ (npr. PI-{CoreId} u folderu 500)  │
          └───────────┬───────────────────────┘
                      │
         ┌────────────┴────────────┐
         │ NE (TC 8)               │ DA (TC 9)
         ▼                         ▼
┌────────────────────┐    ┌────────────────────┐
│ Kreiraj dosije     │    │ Migriraj samo      │
│ ClientAPI atributi │    │ dokumente          │
└────────┬───────────┘    └────────┬───────────┘
         │                         │
         └────────────┬────────────┘
                      ▼
          ┌───────────────────────────────────┐
          │ Premesti dokument:                │
          │ - Copy content                    │
          │ - Set NewName                     │
          │ - Set properties:                 │
          │   * bank:status = Status          │
          │   * bank:source = Source          │
          │   * bank:tipDosijea = TipDosijea  │
          │   * bank:createdAt = Original     │
          └───────────┬───────────────────────┘
                      │
                      ▼
          ┌───────────────────────────────────┐
          │ Označi kao DONE u DOC_STAGING     │
          └───────────────────────────────────┘
```

---

## PRIORITIZOVANA AKCIONA LISTA

### 🔴 KRITIČNO - Mora da se uradi

#### 1. Kreiranje hardkodovanih mapera
**Prioritet:** Najviši
**Fajlovi za kreiranje:**
- `Migration.Infrastructure/Implementation/DocumentNameMapper.cs`
- `Migration.Infrastructure/Implementation/DocumentCodeMapper.cs`
- `Migration.Infrastructure/Implementation/DossierTypeDetector.cs`
- `Migration.Infrastructure/Implementation/DocumentStatusDetector.cs`
- `Migration.Infrastructure/Implementation/SourceDetector.cs`

**Akcije:**
- [x] Dodati enum `DossierType`
- [ ] Implementirati `DocumentNameMapper` sa dictionary-jem naziva
- [ ] Implementirati `DocumentCodeMapper` sa dictionary-jem šifri
- [ ] Implementirati `DossierTypeDetector.DetectFromTipDosijea()`
- [ ] Implementirati `DossierTypeDetector.ResolveFLorPL()`
- [ ] Implementirati `DocumentStatusDetector.GetMigrationInfo()`
- [ ] Implementirati `SourceDetector.GetSource()`

---

#### 2. Integracija mapera u DocumentDiscoveryService
**Prioritet:** Najviši
**Fajl za izmenu:** `Migration.Infrastructure/Implementation/Services/DocumentDiscoveryService.cs`

**Akcije:**
- [ ] Inject novi servisi u konstruktor
- [ ] Čitanje `bank:tipDosijea`, `bank:source`, `bank:clientSegment` iz Alfresco properties
- [ ] Poziv ClientAPI ako properties nedostaju
- [ ] Poziv `DossierTypeDetector.DetectFromTipDosijea()`
- [ ] Poziv `DossierTypeDetector.ResolveFLorPL()` ako je potrebno
- [ ] Poziv `DocumentStatusDetector.GetMigrationInfo()`
- [ ] Poziv `SourceDetector.GetSource()`
- [ ] Snimanje svih podataka u `DocStaging` tabelu

---

#### 3. Konfiguracija folder ID-ova
**Prioritet:** Visok
**Fajl za izmenu:** `Alfresco.App/appsettings.json`

**Akcije:**
- [ ] Dodati sekciju `DossierFolders` sa ID-ovima za 300, 400, 500, 700, 999
- [ ] Dodati flag `CreateIfMissing` per folder
- [ ] Dodati `ExcludedDocumentTypes: ["00702"]`
- [ ] Dodati `DepositDocumentPatterns` listu

---

#### 4. Provera i kreiranje foldera
**Prioritet:** Visok
**Fajl za kreiranje:** `Migration.Infrastructure/Implementation/FolderCreationService.cs` (NOVI)

**Akcije:**
- [ ] Kreirati `FolderCreationService`
- [ ] Implementirati `EnsureFolderExistsAsync(DossierType type)`
- [ ] Provera da li folder postoji u novom Alfresco-u
- [ ] Kreiranje foldera ako ne postoji
- [ ] Update config-a sa novim NodeId

---

#### 5. Provera postojanja dosijea (TC 8-9)
**Prioritet:** Visok
**Fajl za izmenu:** `Migration.Infrastructure/Implementation/Services/MoveService.cs`

**Akcije:**
- [ ] Pre migracije: provera da li dosije PI-{CoreId}/LE-{CoreId}/ACC-{CoreId} postoji
- [ ] Ako NE postoji:
  - [ ] Kreirati dosije folder
  - [ ] Poziv ClientAPI za atribute
  - [ ] Popuniti custom properties na dosijeu
- [ ] Ako postoji: nastaviti sa migracijom

---

### 🟡 VAŽNO - Trebalo bi da se uradi

#### 6. Rukovanje verzijama dokumenata (TC 10, 22)
**Prioritet:** Srednji
**Fajlovi za izmenu:**
- `DocumentDiscoveryService.cs`
- `MoveService.cs`

**Akcije:**
- [ ] Grupisanje dokumenata po CoreID + DocumentType
- [ ] Sortiranje po `OriginalCreatedAt`
- [ ] Provera da li dokument već postoji u novom Alfresco-u
- [ ] Ako postoji: `POST /nodes/{nodeId}/versions` (nova verzija)
- [ ] Ako ne postoji: `POST /nodes/{parentId}/children` (novi dokument)

---

#### 7. DUT integracija (TC 23)
**Prioritet:** Srednji
**Fajl za izmenu:** `DocumentDiscoveryService.cs`

**Akcije:**
- [ ] Inject `IDutApi` u konstruktor
- [ ] Za deposit dokumente: poziv `DutApi.IsOfferBookedAsync(offerId)`
- [ ] Skip dokumenata koji nisu "Booked"
- [ ] Log warning za preskočene dokumente

---

#### 8. Excluded document types (TC 15)
**Prioritet:** Srednji
**Fajlovi za izmenu:**
- `appsettings.json`
- `DocumentDiscoveryService.cs`

**Akcije:**
- [ ] Config: `ExcludedDocumentTypes: ["00702"]`
- [ ] Filter u discovery: `WHERE document_type NOT IN excluded_list`

---

### 🟢 NICE TO HAVE - Poboljšanja

#### 9. Finalni status cleanup (TC 12-14)
**Prioritet:** Nizak
**Fajl za kreiranje:** `Migration.Infrastructure/Implementation/PostMigrationCleanupService.cs` (NOVI)

**Akcije:**
- [ ] Kreirati cleanup servis
- [ ] Nakon migracije: nađi dokumente 00099, 00100, 00101 koji NISU iz migracije
- [ ] Označi ih kao neaktivne
- [ ] Poziv na kraju celog migration procesa

---

#### 10. Validacija kompletnosti deposit dokumenata (TC 24)
**Prioritet:** Nizak
**Fajl za kreiranje:** `Migration.Infrastructure/Implementation/DepositValidationService.cs` (NOVI)

**Akcije:**
- [ ] Kreirati validation servis
- [ ] Provera da li postoje sva 4 obavezna dokumenta za deposit
- [ ] Log warning ako neki nedostaje
- [ ] CSV report sa incomplete deposits

---

#### 11. Broj računa validacija (TC 16)
**Prioritet:** Nizak
**Fajl za kreiranje:** `Migration.Infrastructure/Implementation/AccountValidationService.cs` (NOVI)

**Akcije:**
- [ ] Poziv `ClientAPI.GetActiveAccountsEndpoint`
- [ ] Za dokumente 00824: validacija broja računa
- [ ] Ako nema račun i treba da bude aktivan → reportuj warning

---

#### 12. Status u starom sistemu (TC 11)
**Prioritet:** Nizak (već pokriveno u `DocumentStatusDetector`)

**Akcije:**
- [x] Čitanje `bank:status` iz starog Alfresco-a (već implementirano)
- [x] Logika u `ShouldBeActiveAfterMigration()` (već implementirano)

---

## KONFIGURACIJA

### appsettings.json - Dodatne sekcije

```json
{
  "Migration": {
    "BatchSize": 500,
    "DegreeOfParallelism": 5,
    "DelayBetweenBatchesInMs": 0,
    "IdleDelayInMs": 100,
    "BreakEmptyResults": 5,
    "StuckItemsTimeoutMinutes": 10,
    "RootDestinationFolderId": "9762d917-995b-42a8-a2d9-17995b22a810",
    "RootDiscoveryFolderId": "d668fffb-ef9f-4908-a8ff-fbef9f690827",
    "RootDocumentPath": "C:\\DocumentsRoot",

    // NOVO - Folder ID-ovi za dosijee
    "DossierFolders": {
      "300": {
        "Name": "Dosije paket računa",
        "NodeId": null,
        "CreateIfMissing": true
      },
      "400": {
        "Name": "Dosije pravnog lica",
        "NodeId": null,
        "CreateIfMissing": true
      },
      "500": {
        "Name": "Dosije fizičkog lica",
        "NodeId": null,
        "CreateIfMissing": true
      },
      "700": {
        "Name": "Dosije depozita",
        "NodeId": null,
        "CreateIfMissing": true
      },
      "999": {
        "Name": "Dosije - Unknown",
        "NodeId": null,
        "CreateIfMissing": true
      }
    },

    // NOVO - Deposit document patterns
    "DepositDocumentPatterns": [
      "PiVazeciUgovorOroceniDepozit",
      "PiPonuda",
      "PiAnuitetniPlan",
      "PiObavezniElementiUgovora",
      "ZahtevZaOtvaranjeRacunaOrocenogDepozita"
    ],

    // NOVO - Excluded document types (TC 15)
    "ExcludedDocumentTypes": ["00702"],
    "ExcludedDocumentNames": ["null null"],

    // NOVO - Unknown document handling
    "UnknownDocumentHandling": {
      "Strategy": "MoveToUnknownFolder",
      "MarkAsInactive": true,
      "LogWarning": true,
      "ExportUnmappedListToCsv": true,
      "CsvPath": "C:\\unmapped_documents.csv"
    },

    "MoveService": {
      "MaxDegreeOfParallelism": 50,
      "BatchSize": 200,
      "DelayBetweenBatchesInMs": 0
    },

    "FolderDiscovery": {
      "DegreeOfParallelism": null,
      "BatchSize": null,
      "DelayBetweenBatchesInMs": null,
      "NameFilter": "-",
      "FolderTypes": ["FL", "PL", "ACC", "D"],
      "TargetCoreIds": []
    },

    "DocumentDiscovery": {
      "DegreeOfParallelism": null,
      "BatchSize": null,
      "DelayBetweenBatchesInMs": null
    }
  }
}
```

---

## STRUKTURA FOLDERA U NOVOM ALFRESCO-U

```
Novi Alfresco Root
├── 300 Dosije paket računa/
│   ├── ACC-13001926/
│   │   ├── Ugovor o tekućem računu.pdf
│   │   ├── KDP za fizička lica - migracija.pdf (NEAKTIVAN)
│   │   └── Potvrda o prijemu kartice.pdf (AKTIVAN)
│   └── ACC-13000667/
│       └── ...
│
├── 400 Dosije pravnog lica/
│   ├── LE-50034220/
│   │   ├── Izjava o kanalima komunikacije - migracija.pdf (NEAKTIVAN)
│   │   └── KYC upitnik - migracija.pdf (NEAKTIVAN)
│   └── LE-50034141/
│       └── ...
│
├── 500 Dosije fizičkog lica/
│   ├── PI-102206/
│   │   ├── KYC upitnik - migracija.pdf (NEAKTIVAN)
│   │   └── GDPR saglasnost - migracija.pdf (NEAKTIVAN)
│   └── PI-13001926/
│       └── ...
│
├── 700 Dosije depozita/
│   ├── DE-10194302-00008-10104302_20241105154459/
│   │   ├── Ugovor o oročenom depozitu.pdf (AKTIVAN)
│   │   ├── Ponuda.pdf (AKTIVAN)
│   │   ├── Plan isplate depozita.pdf (AKTIVAN)
│   │   └── Obavezni elementi Ugovora.pdf (AKTIVAN)
│   └── DE-10194302-00008-10104303_20241106103020/
│       └── ...
│
└── 999 Dosije - Unknown/
    ├── UNKNOWN-{CoreId}/
    │   └── unmapped_document.pdf (NEAKTIVAN)
    └── ...
```

---

## PRIMERI - Kako radi mapiranje

### Primer 1: Dokument sa sufiksom → NEAKTIVAN

**Input (stari Alfresco):**
```
Name: "Personal Notice"
Code: "00253"
Status: null (nije postavljen)
Tip dosijea: "Dosije klijenta FL / PL"
Client segment: "PI"
```

**Mapiranje:**
1. `DocumentNameMapper`: "Personal Notice" → "GDPR saglasnost - migracija"
2. `DocumentCodeMapper`: "00253" → "00849"
3. `WillReceiveSuffix`: TRUE
4. `DossierTypeDetector`: "Dosije klijenta FL / PL" + "PI" → DossierType.ClientFL (500)
5. `SourceDetector`: ClientFL → "Heimdall"

**Output (novi Alfresco):**
```
NewName: "GDPR saglasnost - migracija"
NewCode: "00849"
IsActive: FALSE
Status: "poništen"
TargetDossierType: 500 (Dosije fizičkog lica)
Source: "Heimdall"
Folder path: "500 Dosije fizičkog lica/PI-{CoreId}/GDPR saglasnost - migracija.pdf"
```

---

### Primer 2: Dokument bez sufiksa → AKTIVAN

**Input (stari Alfresco):**
```
Name: "Current Accounts Contract"
Code: "00110"
Status: null
Tip dosijea: "Dosije paket računa"
```

**Mapiranje:**
1. `DocumentNameMapper`: nema mapiranje → zadržava "Current Accounts Contract"
2. `DocumentCodeMapper`: "00110" → "00110" (ne menja se)
3. `WillReceiveSuffix`: FALSE
4. `DossierTypeDetector`: "Dosije paket računa" → DossierType.AccountPackage (300)
5. `SourceDetector`: AccountPackage → "Heimdall"

**Output (novi Alfresco):**
```
NewName: "Current Accounts Contract" (ili "Ugovor o tekućem računu")
NewCode: "00110"
IsActive: TRUE
Status: "validiran"
TargetDossierType: 300 (Dosije paket računa)
Source: "Heimdall"
Folder path: "300 Dosije paket računa/ACC-{CoreId}/Current Accounts Contract.pdf"
```

---

### Primer 3: Dokument koji je već bio neaktivan (TC 11)

**Input (stari Alfresco):**
```
Name: "KYC Questionnaire"
Code: "00130"
Status: "poništen" ← već neaktivan!
Tip dosijea: "Dosije klijenta FL / PL"
Client segment: "LE"
```

**Mapiranje:**
1. `DocumentNameMapper`: "KYC Questionnaire" → "KYC upitnik - migracija"
2. `DocumentCodeMapper`: "00130" → "00841"
3. `WillReceiveSuffix`: TRUE
4. **TC 11 Check:** ExistingStatus = "poništen" → ostaje NEAKTIVAN
5. `DossierTypeDetector`: "Dosije klijenta FL / PL" + "LE" → DossierType.ClientPL (400)
6. `SourceDetector`: ClientPL → "Heimdall"

**Output (novi Alfresco):**
```
NewName: "KYC upitnik - migracija"
NewCode: "00841"
IsActive: FALSE ← ostaje neaktivan zbog TC 11
Status: "poništen"
TargetDossierType: 400 (Dosije pravnog lica)
Source: "Heimdall"
Folder path: "400 Dosije pravnog lica/LE-{CoreId}/KYC upitnik - migracija.pdf"
```

---

### Primer 4: Deposit dokument (TC 17, 23, 25)

**Input (stari Alfresco):**
```
Name: "PiVazeciUgovorOroceniDepozitDvojezicniRSD"
Code: "00008"
Status: null
Tip dosijea: "Dosije depozita"
OfferBO Status: "Booked" ← DUT provera
Contract Number: "10104302_20241105154459"
CoreId: "10194302"
```

**Mapiranje:**
1. `DocumentNameMapper`: nema mapiranje → možda se mapira na "Ugovor o oročenom depozitu"
2. `DocumentCodeMapper`: "00008" → "00008"
3. `WillReceiveSuffix`: FALSE
4. `DossierTypeDetector`: "Dosije depozita" → DossierType.Deposit (700)
5. `SourceDetector`: Deposit → "DUT"
6. **TC 23:** `DutApi.IsOfferBookedAsync()` → TRUE (prođe proveru)
7. **TC 25:** Deposit dokumenti su uvek aktivni

**Output (novi Alfresco):**
```
NewName: "Ugovor o oročenom depozitu"
NewCode: "00008"
IsActive: TRUE ← TC 25
Status: "validiran"
TargetDossierType: 700 (Dosije depozita)
Source: "DUT" ← TC 7
Unique ID: "DE-10194302-00008-10104302_20241105154459" ← TC 18
Folder path: "700 Dosije depozita/DE-10194302-00008-10104302_20241105154459/Ugovor o oročenom depozitu.pdf"
```

---

## ZAKLJUČAK

### ✅ Završeno u ovoj analizi:
- Detaljna analiza svih 25 test case scenarija
- Identifikacija trenutnog stanja implementacije
- Identifikacija kritičnih nedostataka
- Dizajn hardkodovanih mapera (NameMapper, CodeMapper, DossierTypeDetector, itd.)
- Tok migracije kroz 3 faze (FolderDiscovery, DocumentDiscovery, Move)
- Prioritizovana akciona lista
- Primeri rada mapiranja

### ⏭️ Sledeći koraci:
1. **Implementacija hardkodovanih mapera** (DocumentNameMapper, DocumentCodeMapper, itd.)
2. **Integracija u DocumentDiscoveryService**
3. **Config izmene u appsettings.json**
4. **Kreiranje FolderCreationService**
5. **Implementacija provere postojanja dosijea u MoveService**
6. **Testiranje sa realnim podacima**
7. **Implementacija nice-to-have feature-a** (validacije, cleanup, itd.)

---

**Verzija dokumenta:** 1.0
**Datum poslednje izmene:** 2025-10-29
**Autor:** Claude Code Analysis Session

