# ClientAPI Integration Guide

## Pregled

ClientAPI je uspešno integrisan u Alfresco migraciju sistem. Sistem je konfigurisan da koristi mock API server za development i testiranje.

## Arhitektura

### Projekti

1. **MockClientAPI** - Mock implementacija Client API servera
   - Port: https://localhost:5101
   - Simulira sve endpoint-e iz dokumentacije
   - Vraća mock podatke za testiranje

2. **Migration.Infrastructure** - Implementacija ClientApi klase
   - `ClientApi.cs` - HttpClient wrapper koji poziva API
   - `ClientApiOptions` - Konfiguracija za API (BaseUrl, endpoints, timeout, retry)

3. **Alfresco.App** - WPF aplikacija sa DI konfiguracijom
   - Registrovan `IClientApi` servis sa Polly retry i circuit breaker policies
   - Konfiguracija u `appsettings.json`

## Pokretanje

### 1. Pokrenite Mock API Server

```bash
cd MockClientAPI
dotnet run
```

Server će biti dostupan na:
- HTTP: http://localhost:5100
- HTTPS: https://localhost:5101

### 2. Pokrenite Alfresco.App

```bash
dotnet run --project Alfresco.App
```

Aplikacija će automatski povezati sa Mock API serverom.

## Konfiguracija

### appsettings.json (Alfresco.App)

```json
{
  "ClientApi": {
    "BaseUrl": "https://localhost:5101",
    "GetClientDataEndpoint": "/api/Client/GetClientDetailExtended",
    "GetActiveAccountsEndpoint": "/api/Client",
    "ValidateClientEndpoint": "/api/Client/GetClientDetail",
    "TimeoutSeconds": 30,
    "ApiKey": null,
    "RetryCount": 3
  }
}
```

## API Endpoints

### 1. Get Client Detail Extended

**Endpoint:** `GET /api/Client/GetClientDetailExtended/{coreId}`

**Primer poziva:**
```bash
curl -k https://localhost:5101/api/Client/GetClientDetailExtended/CORE1234
```

**Odgovor:**
```json
{
  "coreId": "CORE1234",
  "identityNumber": "ID636256",
  "firstName": "Marko",
  "lastName": "Markovic",
  "middleName": "Petrovic",
  "email": "marko.markovic.CORE1234@example.com",
  "phoneNumber": "+381 11 1234567",
  "mobileNumber": "+381 64 1234567",
  "dateOfBirth": "1985-05-15T00:00:00",
  "gender": "Male",
  "nationality": "Serbian",
  "address": "Bulevar Kralja Aleksandra 123",
  "city": "Beograd",
  "country": "Srbija",
  "postalCode": "11000",
  "region": "Central Serbia",
  "clientStatus": "Active",
  "clientType": "Standard",
  "registrationDate": "2020-01-15T00:00:00",
  "lastModifiedDate": "2025-10-07T11:09:59.1344363Z",
  "taxNumber": "722848214",
  "bankAccount": "160-360360-94",
  "notes": "VIP client with excellent payment history",
  "isActive": true,
  "creditLimit": 60003,
  "preferredLanguage": "sr-RS"
}
```

### Mapping na ClientData Model

Mock API odgovor se mapira na `ClientData` model:

```csharp
var clientData = new ClientData
{
    CoreId = mockClientData.CoreId,                    // "CORE1234"
    MbrJmbg = mockClientData.IdentityNumber,           // "ID636256"
    ClientName = $"{FirstName} {LastName}",            // "Marko Markovic"
    ClientType = DetermineClientType(ClientType),      // "FL" (Fizičko Lice)
    ClientSubtype = mockClientData.ClientType,         // "Standard"
    Residency = DetermineResidency(Nationality),       // "Resident"
    Segment = DetermineSegment(ClientType),            // "Standard"
    // Optional fields
    Staff = null,
    OpuUser = null,
    OpuRealization = null,
    Barclex = null,
    Collaborator = null
};
```

### Business Rules za Mapping

#### ClientType Mapping (FL/PL)
- **FL (Fizičko Lice):** Premium, VIP, Standard, Regular
- **PL (Pravno Lice):** Corporate, Business

#### Residency Mapping
- **Resident:** Serbian nationality
- **Non-resident:** Ostale nacionalnosti

#### Segment Mapping
- Premium → Premium
- VIP → VIP
- Standard → Standard
- Regular → Retail
- Corporate → Corporate
- Business → SME

## Upotreba u Kodu

### Dependency Injection

```csharp
public class MojService
{
    private readonly IClientApi _clientApi;

    public MojService(IClientApi clientApi)
    {
        _clientApi = clientApi;
    }

    public async Task ProcesujKlijenta(string coreId)
    {
        // Preuzmi podatke o klijentu
        var clientData = await _clientApi.GetClientDataAsync(coreId);

        Console.WriteLine($"Klijent: {clientData.ClientName}");
        Console.WriteLine($"Tip: {clientData.ClientType}");
        Console.WriteLine($"MBR/JMBG: {clientData.MbrJmbg}");

        // Proveri da li klijent postoji
        bool exists = await _clientApi.ValidateClientExistsAsync(coreId);

        if (exists)
        {
            // Nastavi sa obradom...
        }
    }
}
```

### Registracija servisa koji koristi IClientApi

```csharp
// U App.xaml.cs ili Startup.cs
services.AddScoped<IClientEnrichmentService, ClientEnrichmentService>();
```

## Testiranje Error Scenarija

Mock API podržava testiranje različitih error scenarija:

### 404 Not Found
```bash
curl -k https://localhost:5101/api/Client/GetClientDetailExtended/notfound
# ili
curl -k https://localhost:5101/api/Client/GetClientDetailExtended/0
```

### 500 Internal Server Error
```bash
curl -k https://localhost:5101/api/Client/GetClientDetailExtended/error
```

## Polly Resilience Policies

ClientApi koristi Polly policies za resilience:

1. **Retry Policy** - 3 retry pokušaja sa exponential backoff
2. **Circuit Breaker** - Prekida pozive nakon 5 uzastopnih grešaka
3. **Timeout Policy** - 30 sekundi timeout (konfigurabilno)

## Prelazak na Production API

Kada pravi Client API postane dostupan:

### 1. Ažurirajte appsettings.json

```json
{
  "ClientApi": {
    "BaseUrl": "https://production-client-api.example.com",
    "GetClientDataEndpoint": "/api/Client/GetClientDetailExtended",
    "GetActiveAccountsEndpoint": "/api/Client",
    "ValidateClientEndpoint": "/api/Client/GetClientDetail",
    "TimeoutSeconds": 30,
    "ApiKey": "your-api-key-here",
    "RetryCount": 3
  }
}
```

### 2. Ažurirajte ClientApi.cs (ako je potrebno)

Ako production API ima drugačiju strukturu odgovora, ažurirajte `ClientDetailExtendedDto` i mapping logiku.

### 3. Testirajte sa pravim API-jem

```csharp
// Unit test primer
[Fact]
public async Task GetClientDataAsync_ReturnsValidData()
{
    var clientData = await _clientApi.GetClientDataAsync("CORE1234");

    Assert.NotNull(clientData);
    Assert.Equal("CORE1234", clientData.CoreId);
    Assert.NotEmpty(clientData.ClientName);
}
```

## Troubleshooting

### Problem: "Connection refused" greška

**Rešenje:** Proverite da li je Mock API server pokrenut:
```bash
cd MockClientAPI
dotnet run
```

### Problem: SSL/TLS certificate greška

**Rešenje:** Trust-ujte development certificate:
```bash
dotnet dev-certs https --trust
```

### Problem: Timeout greška

**Rešenje:** Povećajte timeout u `appsettings.json`:
```json
{
  "ClientApi": {
    "TimeoutSeconds": 60
  }
}
```

### Problem: 404 greška na validnom CoreId

**Rešenje:** Proverite da li je BaseUrl tačan u konfiguraciji i da li Mock API server radi.

## Logging

ClientApi loguje sve pozive i greške:

```
[INFO] Fetching client data for CoreId: CORE1234
[INFO] Successfully retrieved client data for CoreId: CORE1234, ClientName: Marko Markovic, ClientType: FL
[ERROR] HTTP request failed while fetching client data for CoreId: CORE1234
```

## Dodatne Informacije

- Za detaljnu dokumentaciju Mock API-ja, pogledajte: `Mock_ClientAPI_DOKUMENTACIJA.md`
- Za implementaciju detalje, pogledajte: `Migration.Infrastructure/Implementation/ClientApi.cs`
- Za DI konfiguraciju, pogledajte: `Alfresco.App/App.xaml.cs` (linije 183-214)

## Kontakt

Za pitanja i podršku, kontaktirajte development team.
