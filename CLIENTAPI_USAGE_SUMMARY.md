# 📞 ClientAPI - Kada i kako se poziva

**Datum:** 2025-11-24
**Status:** ✅ Implementirano

---

## 📋 Pregled

ClientAPI se koristi **SAMO** za dohvatanje podataka o klijentima kada se kreira **NOV dosije** koji **NE POSTOJI** na Alfresco-u.

**KLJUČNO:**
- **Stari naziv dosijea:** `PI-123` (sa crticom)
- **Novi naziv dosijea:** `PI123` (BEZ crtice)
- ClientAPI se poziva SAMO kada dosije `PI123` (novi naziv BEZ crtice) **NE POSTOJI** u destination Alfresco-u
- Ako dosije `PI123` već postoji → ClientAPI se **NE POZIVA**, koristi se postojeći dosije

---

## 🏗️ Arhitektura

```
┌───────────────────────────────────────────────────────────────────┐
│                    MIGRACIJA FLOW                                 │
├───────────────────────────────────────────────────────────────────┤
│                                                                    │
│  1. FolderDiscoveryService                                         │
│     └─> Dohvata folder "PI-123" iz starog Alfresco-a             │
│         (naziv sa crticom)                                        │
│                                                                    │
│  2. MoveService                                                    │
│     └─> Priprema za migraciju                                    │
│         │                                                          │
│         ├─> Uklanja crticu iz naziva: "PI-123" → "PI123"         │
│         │                                                          │
│         ├─> Proverava da li dosije "PI123" postoji               │
│         │   u destination Alfresco-u                              │
│         │                                                          │
│         ├─> AKO DOSIJE "PI123" POSTOJI ✓                         │
│         │   └─> Koristi postojeći dosije                         │
│         │       └─> ClientAPI se NE POZIVA ✗                     │
│         │           └─> Migrira dokumente u postojeći dosije     │
│         │                                                          │
│         └─> AKO DOSIJE "PI123" NE POSTOJI ✗                      │
│             │                                                      │
│             ├─> ClientApi.GetClientDataAsync("123")              │
│             │   └─> GET /api/Client/GetClientDetailExtended/123 │
│             │       └─> Dohvata podatke o klijentu               │
│             │                                                      │
│             ├─> Kreira NOV dosije "PI123" (BEZ crtice)           │
│             │   sa ClientAPI properties (ecm:coreId, ecm:jmbg...) │
│             │                                                      │
│             └─> Migrira dokumente u novi dosije "PI123"          │
│                                                                    │
└───────────────────────────────────────────────────────────────────┘
```

---

## 🎯 Kada se poziva ClientAPI

### **JEDINA TAČKA POZIVA: Kreiranje novog dosijea** 📁

**Endpoint:** `GET /api/Client/GetClientDetailExtended/{coreId}`

**Kada se poziva:**
1. ✅ MoveService pokušava da migrira dokument/folder
2. ✅ Proverava se da li dosije `{DossierType}-{CoreId}` postoji u destination Alfresco-u
3. ✅ **AKO DOSIJE NE POSTOJI** → Poziva se ClientAPI
4. ✅ Dohvata se ClientData
5. ✅ Kreira se nov dosije sa ClientAPI podacima

**Primer scenarija:**

```csharp
// VAŽNO: Stari dosije ima naziv "PI-123" (sa crticom)
//        Novi dosije ima naziv "PI123" (BEZ crtice)

// ═══════════════════════════════════════════════════════════════
// Scenario 1: Dosije PI123 NE postoji na destination Alfresco-u ❌
// ═══════════════════════════════════════════════════════════════
var oldDossierName = "PI-123";  // Stari naziv (sa crticom)
var newDossierName = "PI123";   // Novi naziv (BEZ crtice) - nakon uklanjanja '-'

var exists = await _alfrescoReadApi.FolderExistsAsync(
    parentFolderId,
    newDossierName  // Proverava "PI123"
);

if (!exists)  // false - dosije "PI123" ne postoji
{
    // ✓ POZIVA SE ClientAPI
    var coreId = "123";  // Izvučeno iz "PI-123"
    var clientData = await _clientApi.GetClientDataAsync(coreId, ct);

    // Kreira se NOVI dosije "PI123" sa ClientAPI podacima
    await _alfrescoWriteApi.CreateFolderAsync(
        parentFolderId,
        newDossierName,  // "PI123" - BEZ crtice
        clientData.ToProperties()  // ecm:coreId, ecm:jmbg, ecm:clientName...
    );

    // Rezultat: Dosije "PI123" kreiran sa podacima iz ClientAPI ✓
}

// ═══════════════════════════════════════════════════════════════
// Scenario 2: Dosije PI123 VEĆ POSTOJI na destination Alfresco-u ✓
// ═══════════════════════════════════════════════════════════════
var oldDossierName = "PI-123";
var newDossierName = "PI123";

var exists = await _alfrescoReadApi.FolderExistsAsync(
    parentFolderId,
    newDossierName  // Proverava "PI123"
);

if (exists)  // true - dosije "PI123" već postoji
{
    // ✗ ClientAPI SE NE POZIVA
    // Koristi se postojeći dosije "PI123"
    // Dokumenti se migriraju u postojeći dosije

    // Rezultat: Dosije "PI123" ostaje kakav jeste, bez izmena ✓
}
```

**Šta ClientAPI vraća:**

```json
{
  "coreId": "123",
  "identityNumber": "1234567890123",
  "firstName": "Petar",
  "lastName": "Petrović",
  "clientType": "PI",
  "nationality": "SRB",
  "barCLEXName": "John Doe",
  "barCLEXOpu": "001",
  "barCLEXGroupName": "Group A",
  "barCLEXGroupCode": "GA01",
  "barCLEXCode": "BC123"
}
```

**Properties koje se postavljaju na novi dosije:**

```csharp
{
    "ecm:coreId": "123",
    "ecm:jmbg": "1234567890123",
    "ecm:mbrJmbg": "1234567890123",
    "ecm:clientName": "Petar Petrović",
    "ecm:bnkClientType": "Premium",  // Iz Segment mapiranja
    "ecm:clientSubtype": "Individual",
    "ecm:bnkOfficeId": "001",
    "ecm:staff": "N",
    "ecm:barclex": "GA01 - Group A",
    "ecm:collaborator": "BC123 - John Doe",
    "ecm:residency": "Resident",
    "ecm:bnkResidence": "Resident",
    "ecm:clientType": "PI",
    "ecm:segment": "Premium"
}
```

**Lokacija koda:**
- `ClientApi.cs:43-140` - HTTP poziv ka ClientAPI
- `ClientEnrichmentService.cs:39-162` - Wrapper sa mapiranjem properties

---

## 🔧 Implementacija

### **ClientApi klasa**

**Fajl:** `Migration.Infrastructure\Implementation\ClientApi.cs`

**Metode:**

| Metoda | Endpoint | Opis |
|--------|----------|------|
| `GetClientDataAsync(coreId)` | `GET /api/ClientDetail/{coreId}` | Dohvata kompletan client data |
| `GetActiveAccountsAsync(coreId, date)` | `GET /api/ClientDetail/{coreId}/accounts?asOfDate={date}` | Dohvata aktivne račune na datum |
| `ValidateClientExistsAsync(coreId)` | `GET /api/ClientDetail/{coreId}/exists` | Proverava da li klijent postoji |

**Cache:** ✅ Koristi `IMemoryCache` sa trajanjem od 5 minuta

---

### **ClientEnrichmentService klasa**

**Fajl:** `Migration.Infrastructure\Implementation\ClientEnrichmentService.cs`

**Metode:**

| Metoda | Poziva | Opis |
|--------|--------|------|
| `EnrichFolderWithClientDataAsync(folder)` | `GetClientDataAsync()` | Obogaćuje folder sa client data |
| `EnrichDocumentWithAccountsAsync(document)` | `GetActiveAccountsAsync()` | Dodaje račune u KDP dokumente |
| `ValidateClientAsync(coreId)` | `ValidateClientExistsAsync()` | Validira postojanje klijenta |

---

## 📊 Kada se NE poziva ClientAPI

ClientAPI se **NEĆE** pozvati u sledećim slučajevima:

### **1. ✅ NAJVAŽNIJI RAZLOG: Dosije već postoji**
```csharp
// Stari dosije: "PI-123" (sa crticom)
// Novi dosije: "PI123" (BEZ crtice)

var oldName = "PI-123";
var newName = oldName.Replace("-", "");  // "PI123"

var exists = await _alfrescoReadApi.FolderExistsAsync(parentId, newName);

if (exists)  // Dosije "PI123" postoji
{
    // ✗ ClientAPI SE NE POZIVA
    // ✓ Koristi se postojeći dosije "PI123"
    // Dokumenti se migriraju u postojeći dosije
}
```

**Primer:**
- Prvi put se obrađuje dokument iz dosijea `PI-123`
  - Proverava se: Da li `PI123` postoji? → NE
  - **Poziva se ClientAPI** ✓
  - Kreira se dosije `PI123` sa ClientAPI podacima

- Drugi put se obrađuje drugi dokument iz istog dosijea `PI-123`
  - Proverava se: Da li `PI123` postoji? → **DA** ✓
  - **ClientAPI se NE poziva** ✗
  - Koristi se već kreirani dosije `PI123`

---

### **2. ❌ Folder nema CoreId**
```csharp
if (string.IsNullOrWhiteSpace(folder.CoreId))
    return; // Ne može se kreirati dosije bez CoreId
```

---

### **3. ❌ Folder nema DossierType**
```csharp
if (string.IsNullOrWhiteSpace(folder.DossierType))
    return; // Ne može se kreirati dosije bez tipa (PI, LE, itd.)
```

---

### **4. ❌ ClientAPI integration je disabled**
- Ako `ClientApiOptions.Enabled = false` u konfiguraciji

---

## ⚙️ Konfiguracija

**Fajl:** `appsettings.json`

```json
{
  "ClientApi": {
    "BaseUrl": "https://localhost:7102",
    "GetClientDataEndpoint": "/api/Client/GetClientDetailExtended",
    "GetActiveAccountsEndpoint": "/api/Client",
    "ValidateClientEndpoint": "/api/Client/GetClientDetail",
    "TimeoutSeconds": 30,
    "ApiKey": null,
    "RetryCount": 3
  }
}
```

**Ključni endpoint:**
- **`GetClientDataEndpoint`**: `/api/Client/GetClientDetailExtended/{coreId}`
  - Poziva se samo kada se kreira nov dosije koji ne postoji

**Polly politike:**
- ✅ **Retry:** 3 pokušaja sa exponential backoff
- ✅ **Circuit Breaker:** Posle 5 neuspešnih poziva, pauza 30 sekundi
- ✅ **Timeout:** 30 sekundi po pozivu

---

## 🔄 Flow dijagram

```
┌─────────────────────────────────────────────────┐
│  MoveService.MoveSingleDocumentAsync            │
└──────────────┬──────────────────────────────────┘
               │
               ├─> Dohvata stari folder name iz source
               │   Primer: "PI-123" (sa crticom)
               │
               ├─> Uklanja crticu za novi naziv
               │   "PI-123" → "PI123" (BEZ crtice)
               │
               ├─> Ekstraktuje CoreId: "123"
               │
               ▼
┌─────────────────────────────────────────────────┐
│  Provera: Da li dosije "PI123" postoji?         │
│  (novi naziv BEZ crtice)                        │
└──────────────┬──────────────────────────────────┘
               │
               ├─> DA ✓ (Dosije "PI123" već postoji)
               │   └─> Koristi postojeći dosije "PI123"
               │       └─> ✗ NE POZIVA ClientAPI
               │           └─> Migrira dokument u postojeći dosije
               │
               └─> NE ✗ (Dosije "PI123" NE postoji)
                   │
                   ▼
        ┌────────────────────────────────────────┐
        │  ClientApi.GetClientDataAsync("123")   │
        └──────────────┬─────────────────────────┘
                       │
                       ├─> Cache HIT? → Vrati cached 🚀
                       │
                       └─> Cache MISS → HTTP GET
                           │
                           ▼
                    GET /api/Client/GetClientDetailExtended/123
                           │
                           └─> Cache rezultat (5 min) 💾
                       │
                       ▼
        ┌────────────────────────────────────────┐
        │  CreateFolderAsync(parentId, "PI123",  │
        │                    clientDataProps)    │
        └──────────────┬─────────────────────────┘
                       │
                       ├─> Kreira dosije "PI123" sa properties:
                       │   • ecm:coreId = "123"
                       │   • ecm:jmbg = "1234567890123"
                       │   • ecm:clientName = "Petar Petrović"
                       │   • ecm:bnkClientType = "Premium"
                       │   • ... (ostala polja iz ClientAPI)
                       │
                       ▼
               ✅ Dosije "PI123" kreiran sa ClientAPI podacima
                       │
                       └─> Migrira dokument u novi dosije "PI123"
```

---

## 🚨 Error Handling

ClientAPI pozivi su **non-blocking** - ako poziv ne uspe, migracija nastavlja bez client data.

**Strategija:**
```csharp
try
{
    var clientData = await _clientApi.GetClientDataAsync(coreId, ct);
    // Popuni folder sa client data
}
catch (Exception ex)
{
    _logger.LogError("ClientAPI failed, continuing without client data");
    return folder; // Nastavi bez obogaćivanja ✓
}
```

**Rezultat:**
- ✅ Folder se procesira i migrira
- ⚠️ Client data polja ostaju prazna/null
- 📝 Error se loguje
- 🔄 Proces nastavlja

---

## 📝 Log primeri

### Uspešan poziv:
```
[INFO] Cache MISS for client data CoreId: 123456, fetching from API
[INFO] Successfully enriched folder 789 with client data: CoreId=123456, ClientName=Petar Petrović, ClientType=PI
```

### Cache HIT:
```
[DEBUG] Cache HIT for client data CoreId: 123456
```

### Error:
```
[ERROR] Failed to enrich folder 789 with client data for CoreId: 123456. Continuing without ClientAPI properties.
```

---

## 🧪 Testiranje

### Mock ClientAPI

**Fajl:** `CA_MockData\Program.cs`

Mock API koji simulira ClientAPI za development/testing:
- `/api/ClientDetail/{coreId}` - Vraća mock client data
- `/api/ClientDetail/{coreId}/accounts?asOfDate={date}` - Vraća mock račune
- `/api/ClientDetail/{coreId}/exists` - Uvek vraća `true`

**Pokretanje:**
```bash
cd CA_MockData
dotnet run
# Sluša na https://localhost:5001
```

---

## 📚 Dodatni resursi

- **Implementacija:** `ClientApi.cs`, `ClientEnrichmentService.cs`
- **Interface:** `IClientApi.cs`, `IClientEnrichmentService.cs`
- **Mock API:** `CA_MockData\Program.cs`
- **Dokumentacija:** `ClientAPI_Integration_Guide.md`
- **POST-MIGRATION:** `PostMigrationCommands.cs`

---

## ✅ Zaključak

ClientAPI se poziva **SAMO** kada se kreira nov dosije koji **NE POSTOJI** u destination Alfresco-u.

**Ključne karakteristike:**
- ✅ **Endpoint**: `/api/Client/GetClientDetailExtended/{coreId}`
- ✅ **Poziva se SAMO**: Ako dosije `{DossierType}-{CoreId}` ne postoji
- ✅ **Cached** (5 min) - Smanjuje broj HTTP poziva
- ✅ **Resilient** (Retry + Circuit Breaker) - Otporan na privremene greške
- ✅ **Non-blocking** (Error ne zaustavlja migraciju)
- ✅ **Automatski** - Dešava se tokom MoveService izvršavanja

**Scenario:**
1. Document treba da se migrira u dosije `PI-123`
2. MoveService proverava: Da li `PI-123` postoji?
   - **Postoji** ✓ → Koristi postojeći (ClientAPI se NE poziva)
   - **Ne postoji** ✗ → Poziva ClientAPI → Kreira dosije sa podacima

🎯 **ClientAPI se NE poziva za svaki dokument - samo za kreiranje NOVIH dosijea!**
