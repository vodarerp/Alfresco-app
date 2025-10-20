# ClientAPI - Analiza za Migraciju Alfresco Dokumenata

## Pregled Projekta

**Cilj**: Migracija retail klijenata (Division PUG) sa starog Alfresco-a na novi
- Migracija dokumenata iz starih dosijea (format: "PI10227858")
- Kreiranje novih dosijea ako ne postoje
- Popunjavanje klijentskih atributa putem **ClientAPI**

---

## 🎯 KRITIČNI ENDPOINTI ZA MIGRACIJU

### 1. PRETRAGA I VALIDACIJA KLIJENATA

#### **ClientSearch** ⭐⭐⭐⭐⭐
```
GET /api/Client/ClientSearch/{searchValue}
```
**Kada koristiti**:
- Brza validacija da li klijent postoji u sistemu
- Pretraga pre migracije svakog dosijea

**Output**:
```json
{
  "coreId": "string",
  "identityNumber": "string",
  "fullName": "string"
}
```

**Use Case za Migraciju**:
```
1. Parsing starog dosijea: "PI10227858" → izvuci "10227858"
2. Poziv: GET /api/Client/ClientSearch/10227858
3. Validacija da li klijent postoji
4. Dohvatanje CoreID za dalje operacije
```

---

#### **ClientExists** ⭐⭐⭐⭐⭐
```
GET /api/Client/ClientExists/{identityNumber}
```
**Kada koristiti**:
- Pre kreiranja novog dosijea - provera da li klijent uopšte postoji
- Validacija JMBG/PIB brojeva iz starih dosijea

**Output**:
```
string (verovatno coreId ili status)
```

**Use Case za Migraciju**:
```
Pre migracije:
IF ClientExists(identityNumber) == null/false THEN
  → LOG ERROR: Klijent ne postoji u sistemu
  → SKIP ovaj dosije
ELSE
  → Nastavi sa migracijom
```

---

#### **GetClientBasicDataByJmbgOrPib** ⭐⭐⭐⭐⭐
```
GET /api/Client/GetClientBasicDataByJmbgOrPib/{searchValue}
```
**Kada koristiti**:
- Kada imaš JMBG/PIB ali ne i CoreID
- Dohvatanje osnovnih podataka za mapiranje

**Output**: `ClientData` (pun objekat sa svim osnovnim podacima)

**Use Case za Migraciju**:
```
Scenario: Stari dosije ima PIB "12345678"
1. GET /api/Client/GetClientBasicDataByJmbgOrPib/12345678
2. Iz odgovora izvuci: CoreID, clientNumber, clientType
3. Koristi CoreID za kreiranje novog dosijea
```

---

### 2. DETALJNI PODACI KLIJENTA (za atribute dosijea)

#### **GetClientDetailExtended** ⭐⭐⭐⭐⭐ **NAJVAŽNIJI**
```
GET /api/Client/GetClientDetailExtended/{coreId}
```
**Kada koristiti**:
- Popunjavanje svih atributa novog dosijea
- Iz dokumentacije: *"Klijentske podatke na dosijeu (atributi dosijea) popuniti pozivom ClientAPI-a"*

**Output**: `ClientDetailExtended` sadrži:
- **ClientGeneralInfo**: tip klijenta, status, OPU, rezidentnost, datum otvaranja
- **ClientDomicileAddressInfo**: adresa, grad, ZIP
- **ClientContactData**: email, telefoni
- **ClientEmploymentInfo**: zaposlenje (za FL)
- **ClientFinancialInfo**: finansijski podaci
- **ClientLegalRepresentativeInfo**: pravni zastupnici (za PL)
- **AuthorizedDocInfo**: ovlašćena lica

**Mapiranje za Novi Dosije**:
```
ClientDetailExtended Response → Novi Dosije Atributi

clientGeneralInfo.clientNumber    → MBR/JMBG klijenta
clientGeneralInfo.shortName        → Naziv klijenta
clientGeneralInfo.clientType       → Tip klijenta (FL/PL)
clientGeneralInfo.status           → Status
clientGeneralInfo.opu              → OPU korisnika
clientGeneralInfo.residentIndicator→ Rezidentnost
clientGeneralInfo.openingDate      → Datum kreiranja klijenta

clientContactData.email            → Email
clientContactData.mobilePhoneNumber→ Telefon
```

**Implementacioni Pristup**:
```
FOR EACH stari_dosije IN migracija_lista:
  coreId = ExtractCoreId(stari_dosije.name)

  clientData = GET /api/Client/GetClientDetailExtended/{coreId}

  novi_dosije.attributes = MapToNewDossierAttributes(clientData)

  CREATE novi_dosije WITH attributes
```

---

#### **GetClientDetail** ⭐⭐⭐⭐
```
GET /api/Client/GetClientDetail/{coreId}
```
**Kada koristiti**:
- Alternativa ako `GetClientDetailExtended` vraća previše podataka
- Brži poziv za osnovne atribute

**Razlika od Extended**:
- `GetClientDetail` = samo osnovne info
- `GetClientDetailExtended` = SVE (adrese, zaposlenje, dokumenti, itd.)

**Preporuka**: Koristi **Extended** da bi jednim pozivom dobio sve što ti treba

---

### 3. TIP KLIJENTA I SEGMENT (za određivanje dosijea FL/PL)

#### **GetClientData** ⭐⭐⭐⭐⭐
```
GET /api/Client/GetClientData/{clientId}
```
**Kada koristiti**:
- Određivanje da li je klijent **Fizičko lice** ili **Pravno lice**
- Iz dokumentacije: *"CT na osnovu CoreID i segmenta klijenta će imati podatak o tipu klijenta"*

**Output**: `ClientData` sa poljem `customerType` / `clientType`

**Use Case za Migraciju**:
```
Document Mapping Logic:

clientData = GET /api/Client/GetClientData/{coreId}

IF clientData.clientType == "FIZIČKO LICE" THEN
  target_dosije = "Dosije klijenta FL"
  produkt_tip = "Fizička lica – Depozitni proizvodi (00008)"
ELSE IF clientData.clientType == "PRAVNO LICE" THEN
  target_dosije = "Dosije klijenta PL"
  produkt_tip = "SB - Depozitni proizvodi (00010)"
```

---

#### **GetClientDetailedSegment** ⭐⭐⭐⭐
```
GET /api/Client/GetClientDetailedSegment/{clientId}
```
**Kada koristiti**:
- Dodatna segmentacija klijenta (retail, corporate, premium, itd.)
- Određivanje specifičnih pravila migracije po segmentu

**Output**: `string` (npr. "RETAIL", "SMALL BUSINESS", "CORPORATE")

---

### 4. RAČUNI KLIJENTA (za KDP migraciju)

#### **GetClientAccounts** ⭐⭐⭐⭐⭐
```
GET /api/Client/GetClientAccounts/{clientId}
```
**Kada koristiti**:
- Popunjavanje liste računa za KDP dokumente (00824, 00099)
- Iz dokumentacije: *"popuniti listu računa koji su sada aktivni a bili su otvoreni na dan kreiranja dokumenta"*

**Output**: `ClientAccountsData` (lista aktivnih računa)

**Use Case za KDP Migraciju**:
```
Scenario: Migracija KDP dokumenta koji nema popunjen broj računa

1. GET /api/Client/GetClientAccounts/{clientId}
2. Filtriraj račune:
   - accountOpenDate <= kdp_document.creationDate
   - accountStatus == "ACTIVE" ili accountCloseDate == null
3. Formatiraj: "123456,234567,345678" (zarezom odvojeni)
4. Upiši u: kdp_document.docAccountNumbers
```

**SQL Export za Banku**:
```sql
SELECT
  dosije_reference,
  client_number,
  document_reference,
  document_type, -- 00824 ili 00099
  document_creation_date
FROM migrated_documents
WHERE document_type IN ('00824', '00099')
  AND is_active = TRUE
  AND doc_account_numbers IS NULL
```

---

#### **GetClientAccountsDetails** ⭐⭐⭐⭐
```
GET /api/Client/GetClientAccountsDetails/{sourceSystem}/{coreId}
```
**Kada koristiti**:
- Detaljne informacije o računima (datumi otvaranja/zatvaranja)
- Validacija da li je račun bio otvoren na određeni datum

**Use Case**:
```
FOR EACH account IN client_accounts:
  details = GET /api/Client/GetClientAccountsDetails/HEIMDALL/{coreId}

  IF details.openDate <= document.creationDate AND
     (details.closeDate == null OR details.closeDate > document.creationDate) THEN
    account_list.ADD(account.number)
```

---

### 5. IDENTIFIKATORI I CROSS-REFERENCE

#### **GetClientIdentityNumber** ⭐⭐⭐⭐
```
GET /api/Client/GetClientIdentityNumber/{coreId}
```
**Kada koristiti**:
- Kada imaš CoreID ali ti treba JMBG/PIB za validaciju
- Reverse lookup za logovanje

**Output**: `string` (JMBG/PIB)

---

#### **QuickSearchClient** ⭐⭐⭐⭐
```
POST /api/Client/QuickSearchClient?maxRecordCount=10
```
**Kada koristiti**:
- Kompleksne pretrage sa više parametara
- Batch validacija klijenata

**Input**:
```json
{
  "coreId": "10227858",
  "searchParams": {
    "clientNumber": "...",
    "identityNumber": "..."
  }
}
```

**Output**: `QuickSearchClientResponseInfo` (kolekcija klijenata)

---

### 6. KONTAKT PODACI

#### **GetClientContactData** ⭐⭐⭐⭐
```
GET /api/Client/GetClientContactData/{clientId}
```
**Kada koristiti**:
- Popunjavanje kontakt atributa dosijea
- Email, telefoni za notifikacije

**Output**: `ClientContactData`
```json
{
  "email": "...",
  "mobilePhoneNumber": "...",
  "fixedPhoneNumber": "...",
  "faxNumber": "..."
}
```

---

### 7. FINANSIJSKI PODACI (opcionalno)

#### **GetClientFinancialData** ⭐⭐⭐
```
GET /api/Client/GetClientFinancialData/{clientId}
```
**Kada koristiti**:
- Ako novi dosije zahteva finansijske atribute
- Procena rizika prilikom migracije

---

### 8. COMPLIANCE (ako je potrebno)

#### **CheckUserForTerroristAndPEPList** ⭐⭐⭐
```
POST /api/Client/CheckUserForTerroristAndPepList
```
**Kada koristiti**:
- Validacija klijenata pre migracije (da li su na crnim listama)
- Označavanje dosijea sa posebnim statusom

**Input**:
```json
{
  "firstName": "...",
  "lastName": "...",
  "dateOfBirth": "..."
}
```

---

## 📋 PREPORUČENI WORKFLOW ZA MIGRACIJU

### **FAZA 1: Pre-Migracija Validacija**

```
┌─────────────────────────────────────────┐
│ 1. Učitaj listu starih dosijea          │
│    Format: "PI10227858"                  │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 2. Extrakt CoreID ili JMBG/PIB          │
│    "PI10227858" → "10227858"             │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 3. Validacija klijenta                   │
│    GET /ClientExists/{identityNumber}    │
│    GET /ClientSearch/{searchValue}       │
└─────────────────────────────────────────┘
              ↓
         ┌─────────┐
         │ Postoji? │
         └─────────┘
          │        │
         DA       NE → LOG ERROR, SKIP
          │
          ↓
┌─────────────────────────────────────────┐
│ 4. Dohvati CoreID                        │
│    clientData.coreId                     │
└─────────────────────────────────────────┘
```

---

### **FAZA 2: Kreiranje Dosijea**

```
┌─────────────────────────────────────────┐
│ 5. Proveri da li novi dosije postoji     │
│    Query Alfresco: DE-{CoreID}-...       │
└─────────────────────────────────────────┘
              ↓
         ┌─────────┐
         │ Postoji? │
         └─────────┘
          │        │
         DA       NE
          │        │
          │        ↓
          │  ┌─────────────────────────────────────┐
          │  │ 6a. Dohvati SVE podatke klijenta     │
          │  │     GET /GetClientDetailExtended/    │
          │  │         {coreId}                     │
          │  └─────────────────────────────────────┘
          │        ↓
          │  ┌─────────────────────────────────────┐
          │  │ 6b. Mapiraj atribute                 │
          │  │     - Tip klijenta (FL/PL)           │
          │  │     - MBR/JMBG                       │
          │  │     - Naziv klijenta                 │
          │  │     - OPU                             │
          │  │     - Rezidentnost                   │
          │  │     - Status                         │
          │  └─────────────────────────────────────┘
          │        ↓
          │  ┌─────────────────────────────────────┐
          │  │ 6c. Kreiraj novi dosije              │
          │  │     Alfresco API: CREATE             │
          │  └─────────────────────────────────────┘
          │        │
          └────────┘
                   ↓
┌─────────────────────────────────────────┐
│ 7. Reference na dosije spremna           │
└─────────────────────────────────────────┘
```

---

### **FAZA 3: Migracija Dokumenata**

```
┌─────────────────────────────────────────┐
│ 8. Za svaki dokument u starom dosijeu    │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 9. Odredi tip dokumenta                  │
│    - Source: Heimdall                    │
│    - Mapiranje: Excel tabela             │
│    - Politika čuvanja                    │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 10. Odredi tip dosijea                   │
│     GET /GetClientData/{coreId}          │
│     → clientType → FL ili PL             │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 11. Odredi target dosije                 │
│     - Dosije klijenta FL/PL              │
│     - Dosije paket računa                │
│     - Dosije ostalo                      │
│     - Dosije depozita                    │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 12. Migracija dokumenta                  │
│     - Sve verzije                        │
│     - Atributi dokumenta                 │
│     - Status (aktivan/neaktivan)         │
│     - Datum kreacije (originalni!)       │
└─────────────────────────────────────────┘
```

---

### **FAZA 4: Post-Migracija KDP Obrada**

```
┌─────────────────────────────────────────┐
│ 13. Export aktivnih KDP bez računa       │
│     Tipovi: 00824, 00099                 │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 14. Za svaki KDP dokument                │
│     GET /GetClientAccounts/{clientId}    │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 15. Filtriraj račune                     │
│     - openDate <= document.creationDate  │
│     - status == ACTIVE                   │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 16. Formatiraj listu: "R1,R2,R3"         │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│ 17. Update docAccountNumbers             │
└─────────────────────────────────────────┘
```

---

## 🔧 IMPLEMENTACIONI PRISTUP

### **Option 1: Batch Processing (Preporučeno)**

```
PSEUDOCODE:

// Step 1: Load all old dossiers
old_dossiers = LoadFromAlfresco("PI*") // PI10227858, itd.

// Step 2: Validate clients
FOR EACH dossier IN old_dossiers:

  coreId = ExtractCoreId(dossier.name) // "10227858"

  // Validacija
  exists = ClientAPI.ClientExists(coreId)
  IF NOT exists THEN
    LOG_ERROR(dossier, "Client does not exist")
    CONTINUE

  // Dohvat podataka
  clientData = ClientAPI.GetClientDetailExtended(coreId)

  // Provera da li novi dosije postoji
  new_dossier_ref = GenerateDossierReference(clientData)

  IF NOT AlfrescoAPI.DossierExists(new_dossier_ref) THEN
    // Kreiranje novog dosijea
    attributes = MapClientDataToAttributes(clientData)
    AlfrescoAPI.CreateDossier(new_dossier_ref, attributes)

  // Migracija dokumenata
  documents = GetDocumentsFromOldDossier(dossier)

  FOR EACH doc IN documents:
    mapping = GetMappingFromExcel(doc.type)
    target_dossier = DetermineTargetDossier(clientData, mapping)

    MigrateDocument(doc, target_dossier, mapping)

// Step 3: Post-processing KDP
kdp_docs = GetActiveKDPWithoutAccounts()

FOR EACH kdp IN kdp_docs:
  accounts = ClientAPI.GetClientAccounts(kdp.clientId)

  active_accounts = FilterAccountsByDate(accounts, kdp.creationDate)
  account_list = FormatAccountList(active_accounts) // "R1,R2,R3"

  UpdateDocument(kdp, docAccountNumbers: account_list)
```

---

### **Option 2: Event-Driven (za veliki volumen)**

```
ARCHITECTURE:

┌─────────────────┐         ┌─────────────────┐
│  Old Alfresco   │────────>│  Message Queue  │
│   (Source)      │         │   (RabbitMQ)    │
└─────────────────┘         └─────────────────┘
                                     │
                                     ↓
                            ┌─────────────────┐
                            │ Migration Worker│
                            │   (Consumer)    │
                            └─────────────────┘
                                     │
                    ┌────────────────┼────────────────┐
                    ↓                ↓                ↓
           ┌────────────────┐ ┌─────────────┐ ┌─────────────┐
           │   ClientAPI    │ │  New Alfresco│ │   Logging   │
           │   (REST)       │ │   (Target)   │ │     DB      │
           └────────────────┘ └─────────────┘ └─────────────┘

WORKFLOW:

1. Producer čita stare dosijee i stavlja u queue
2. Worker uzima poruku iz queue
3. Worker poziva ClientAPI za validaciju i podatke
4. Worker kreira novi dosije ako ne postoji
5. Worker migrira dokumente
6. Worker loguje rezultat (success/failure)
7. Idempotentnost: ako fail, retry sa istom porukom
```

---

### **Option 3: Hybrid (Batch + Parallel)**

```
APPROACH:

1. Batch učitavanje: Učitaj 1000 starih dosijea
2. Parallel validacija:
   - Thread Pool (10 workers)
   - Svaki worker validira 100 klijenata paralelno
   - Koristi ClientAPI.ClientExists() + ClientSearch()
3. Sequential kreiranje dosijea:
   - GetClientDetailExtended() za svakog
   - Kreiranje dosijea jedan po jedan (zbog konzistencije)
4. Parallel migracija dokumenata:
   - Thread Pool (20 workers)
   - Svaki worker migrira dokumente jednog dosijea

PSEUDOCODE:

batches = SplitIntoBatches(old_dossiers, 1000)

FOR EACH batch IN batches:

  // Parallel validacija
  valid_clients = ParallelMap(batch, lambda d:
    ClientAPI.ClientExists(ExtractCoreId(d))
  )

  // Sequential dohvat podataka i kreiranje dosijea
  FOR EACH client IN valid_clients:
    clientData = ClientAPI.GetClientDetailExtended(client.coreId)
    IF NOT DossierExists(client):
      CreateDossier(clientData)

  // Parallel migracija dokumenata
  ParallelForEach(valid_clients, lambda client:
    MigrateAllDocuments(client)
  )

  // Log progress
  LogBatchCompletion(batch)
```

---

## 📊 MAPIRANJE KLIJENTSKIH PODATAKA

### **ClientDetailExtended → Novi Dosije (FL/PL)**

| **ClientAPI Field**                          | **Novi Dosije Atribut**     | **Obavezno** |
|----------------------------------------------|-----------------------------|--------------|
| `clientGeneralInfo.clientNumber`             | MBR/JMBG klijenta           | ✅           |
| `clientGeneralInfo.shortName`                | Naziv klijenta              | ✅           |
| `clientGeneralInfo.clientType`               | Tip klijenta (FL/PL)        | ✅           |
| `clientGeneralInfo.status`                   | Status                      | ✅           |
| `clientGeneralInfo.opu`                      | OPU korisnika               | ✅           |
| `clientGeneralInfo.residentIndicator`        | Rezidentnost                | ✅           |
| `clientGeneralInfo.openingDate`              | Datum kreiranja             | ⚠️ (migration date!) |
| `clientContactData.email`                    | Email                       | ❌           |
| `clientContactData.mobilePhoneNumber`        | Telefon                     | ❌           |
| `clientDomicileAddressInfo.domicileCity`     | Grad                        | ❌           |
| `clientDomicileAddressInfo.domicileZIPCode`  | Poštanski broj              | ❌           |
| `clientFinancialInfo.totalAssets`            | Ukupna aktiva               | ❌           |

⚠️ **NAPOMENA**: `datum kreiranja` dosijea **NE SME** biti datum migracije, već **originalni datum iz starog Alfresco-a**!

---

### **ClientDetailExtended → Dosije Depozita**

| **ClientAPI Field**                  | **Dosije Depozita Atribut**  | **Obavezno** |
|--------------------------------------|------------------------------|--------------|
| `clientGeneralInfo.clientNumber`     | MBR/JMBG                     | ✅           |
| `coreId` (parametar)                 | Core ID                      | ✅           |
| `clientGeneralInfo.shortName`        | Naziv klijenta               | ✅           |
| `clientGeneralInfo.clientType`       | Tip klijenta                 | ✅           |
| (iz mapinga)                         | Tip proizvoda (00008/00010)  | ✅           |
| (iz starog Alfresco-a)               | Broj ugovora                 | ✅           |
| (iz starog Alfresco-a)               | Partija                      | ❌           |
| `clientGeneralInfo.opu`              | OPU korisnika                | ✅           |
| `clientGeneralInfo.residentIndicator`| Rezidentnost                 | ✅           |

**Jedinstveni Identifikator Dosijea**:
```
Format: DE-{CoreId}-{TipProizvoda}-{BrojUgovora}
Primer: DE-10194302-00008-10104302_20241105154459
```

---

## 🎯 PRIORITET ENDPOINTA PO FAZAMA

### **FAZA 1: Validacija i Priprema** (Dan 1-2)
1. ✅ `ClientExists` - provera da li klijent postoji
2. ✅ `ClientSearch` - pretraga i dohvat CoreID
3. ✅ `GetClientBasicDataByJmbgOrPib` - alternativna pretraga

### **FAZA 2: Kreiranje Dosijea** (Dan 3-5)
1. ✅ `GetClientDetailExtended` - **NAJVAŽNIJI** - svi podaci za dosije
2. ✅ `GetClientData` - tip klijenta (FL/PL)
3. ✅ `GetClientDetailedSegment` - dodatna segmentacija

### **FAZA 3: Migracija Dokumenata** (Dan 6-10)
1. ✅ `GetClientData` - određivanje target dosijea
2. ⚠️ (Excel mapiranje + biznis logika)

### **FAZA 4: Post-Processing KDP** (Dan 11-12)
1. ✅ `GetClientAccounts` - lista računa
2. ✅ `GetClientAccountsDetails` - detalji računa (datumi)

### **FAZA 5: Validacija i Compliance** (Dan 13)
1. ✅ `CheckUserForTerroristAndPEPList` - opcionalno
2. ✅ `IsActiveClient` - validacija statusa

---

## ⚠️ KRITIČNI ZAHTEVI IZ DOKUMENTACIJE

### **1. Datum Kreacije**
```
❌ POGREŠNO: datum_kreacije = DateTime.Now (datum migracije)
✅ ISPRAVNO: datum_kreacije = stari_dokument.creation_date (originalni datum)
```

### **2. Source Field**
```
✅ Source = "Heimdall" (NE MENJATI!)
```

### **3. Status Dokumenta - KDP Logika**

**Specimen Card (KDP) za FL - Tipovi: 00824, 00099**
```
Prilikom migracije:
- 00824 (migracija) → NEAKTIVAN
- 00099 (novi Alfresco) → NEAKTIVAN

Post-migracija:
1. IF EXISTS aktivan 00824 sa izvorom "Depo kartoni_Validan" THEN
     → Ostavi ga aktivnim
     → Svi ostali KDP → NEAKTIVNI

2. ELSE IF NOT EXISTS iz depo kartona THEN
     → Najnoviji KDP (max creationDate) → AKTIVAN
     → Ostali → NEAKTIVNI

3. IF KDP postane AKTIVAN AND tip == 00824 THEN
     → Promeni tip u 00099
     → Politika čuvanja: "nova verzija"
```

**ClientAPI Pozivi za KDP Logiku**:
```
// Nema direktnog poziva, ali možeš koristiti:
GetClientAccounts() → da proveriš ovlašćenja po računu

IF ovlascenje_nivo IN (3, 4) THEN // Zastupnik maloletnog, Zastupnik po rešenju
  → Svi KDP ostaju NEAKTIVNI
```

### **4. Lista Računa za KDP**

```
Scenario: KDP dokument AKTIVAN ali nema popunjen docAccountNumbers

Step 1: Export lista
GET /api/Client/GetClientAccounts/{clientId}

Step 2: Filtriraj
accounts WHERE:
  - account.openDate <= kdp.creationDate
  - account.status == "ACTIVE" OR account.closeDate IS NULL

Step 3: Format
"123456,234567,345678" // zarezom odvojeni

Step 4: Update
UPDATE document SET docAccountNumbers = account_list
```

---

## 🔍 ERROR HANDLING I LOGGING

### **Kritični Scenariji**

#### **Scenario 1: Klijent ne postoji**
```
ERROR LOG:
{
  "dossier": "PI10227858",
  "coreId": "10227858",
  "error": "Client does not exist in ClientAPI",
  "action": "SKIP",
  "timestamp": "2024-11-05T10:00:00Z"
}
```

#### **Scenario 2: ClientAPITimeout**
```
RETRY LOGIC:
1. Retry 3x sa exponential backoff (1s, 2s, 4s)
2. IF still failing THEN
     LOG ERROR
     MARK dossier za manual review
```

#### **Scenario 3: ClientAPI vraća nepotpune podatke**
```
VALIDATION:
IF clientData.clientType IS NULL THEN
  LOG WARNING: "Client type missing for {coreId}"

  // Fallback: Pogledaj JMBG/PIB
  IF clientData.personalID != NULL THEN
    → Fizičko lice
  ELSE IF clientData.companyIDNumber != NULL THEN
    → Pravno lice
```

#### **Scenario 4: Dosije već postoji**
```
CHECK:
IF AlfrescoAPI.DossierExists(new_dossier_ref) THEN
  LOG INFO: "Dossier already exists: {new_dossier_ref}"
  SKIP dosije creation
  CONTINUE sa migracijom dokumenata
```

---

## 📈 PERFORMANCE OPTIMIZACIJE

### **1. Batch ClientAPI Pozivi**

Ako ClientAPI podržava batch:
```
// Umesto:
FOR i = 1 TO 1000:
  clientData[i] = GET /GetClientDetailExtended/{coreId[i]}

// Koristi:
clientDataBatch = POST /GetClientDetailExtendedBatch
{
  "coreIds": ["10227858", "10227859", ...]
}
```
⚠️ Proveri da li ClientAPI podržava batch operacije!

### **2. Caching**

```
CACHE STRATEGY:
- ClientData cache: 1 hour TTL
- Accounts cache: 30 min TTL
- Segment cache: 24 hours TTL

PSEUDOCODE:
cache = {}

FUNCTION GetClientDataCached(coreId):
  IF coreId IN cache AND NOT Expired(cache[coreId]) THEN
    RETURN cache[coreId]

  data = ClientAPI.GetClientDetailExtended(coreId)
  cache[coreId] = (data, DateTime.Now + 1hour)
  RETURN data
```

### **3. Connection Pooling**

```
HTTP CLIENT CONFIG:
- Max connections: 100
- Connection timeout: 30s
- Read timeout: 60s
- Keep-alive: enabled
- Retry: 3x with exponential backoff
```

### **4. Parallel Processing**

```
THREAD POOL:
- Validation phase: 20 threads
- Dossier creation: 5 threads (sequential zbog konzistencije)
- Document migration: 50 threads (paralelno)
- KDP post-processing: 10 threads
```

---

## 🧪 TESTIRANJE

### **Unit Tests**

```
TEST: Client Validation
GIVEN: coreId = "10227858"
WHEN: ClientAPI.ClientExists(coreId)
THEN: RETURN true OR false

TEST: Client Data Mapping
GIVEN: ClientDetailExtended response
WHEN: MapClientDataToAttributes(response)
THEN: attributes.tipKlijenta == "FL" OR "PL"
```

### **Integration Tests**

```
TEST: End-to-End Migration
GIVEN: Stari dosije "PI10227858" sa 5 dokumenata
WHEN: RunMigration(dosije)
THEN:
  - Novi dosije kreiran
  - 5 dokumenata migrirano
  - Atributi popunjeni iz ClientAPI
  - Originalni datumi sačuvani
```

### **Performance Tests**

```
TEST: Throughput
GIVEN: 10,000 dosijea
WHEN: RunMigration(all_dossiers)
THEN:
  - Completion time < 24 hours
  - ClientAPI success rate > 99%
  - No memory leaks
```

---

## 📞 KONTAKT I PODRŠKA

**ClientAPI Pitanja**:
- Da li postoji batch endpoint?
- Rate limiting: koliko poziva/sec?
- SLA: response time garantuje?

**Alfresco API Pitanja**:
- Kako proveriti da li dosije postoji?
- Kako kreirati dosije programski?
- Kako setovati originalni datum kreacije?

---

## ✅ CHECKLIST ZA IMPLEMENTACIJU

### Pre Implementacije
- [ ] Testiraj sve ClientAPI endpointe na TEST okruženju
- [ ] Proveri rate limiting ClientAPI
- [ ] Napravi mapiranje Excel → kod (automatizovati parsing)
- [ ] Setup logging infrastructure
- [ ] Setup monitoring (Grafana/Prometheus)

### Tokom Implementacije
- [ ] Implementiraj retry logiku za ClientAPI
- [ ] Implementiraj caching za često pozivane podatke
- [ ] Implementiraj idempotentnost (može se pokrenuti više puta)
- [ ] Loguj svaki korak (DEBUG mode)

### Post Implementacija
- [ ] Export lista KDP bez računa
- [ ] Dostavi banku za review
- [ ] Implementiraj finalizaciju KDP statusa
- [ ] Obriši/sakrij stare dosijee "PI*"

---

## 🎉 ZAKLJUČAK

**Top 5 Endpointa za Migraciju**:
1. ⭐⭐⭐⭐⭐ `GetClientDetailExtended` - backbone migracije
2. ⭐⭐⭐⭐⭐ `ClientSearch` / `ClientExists` - validacija
3. ⭐⭐⭐⭐⭐ `GetClientData` - tip klijenta (FL/PL)
4. ⭐⭐⭐⭐⭐ `GetClientAccounts` - KDP računi
5. ⭐⭐⭐⭐ `GetClientBasicDataByJmbgOrPib` - fallback pretraga

**Implementacioni Pristup**:
- **Batch processing** sa **parallel validation**
- **Caching** klijentskih podataka
- **Retry logika** za ClientAPI pozive
- **Idempotentnost** za ponovno pokretanje

**Kritični Zahtevi**:
- ✅ Čuvaj originalne datume
- ✅ Source ostaje "Heimdall"
- ✅ KDP logika sa 3 koraka
- ✅ Lista računa za aktivne KDP

