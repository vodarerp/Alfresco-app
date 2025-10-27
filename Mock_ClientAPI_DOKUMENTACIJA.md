# Mock Client API - Dokumentacija

## Pregled

Mock Client API je RESTful servis koji simulira pozive ka pravom Client API-u. API omogućava preuzimanje informacija o klijentima, proveru postojanja klijenata i upravljanje podacima o klijentima.

## Base URL

```
https://localhost:{port}/api/Client
```

Port zavisi od konfiguracije vašeg projekta (obično 5001 za HTTPS ili 5000 za HTTP).

## Autentifikacija

Trenutno API ne zahteva autentifikaciju (mock verzija).

---

## Endpointi

### 1. Get Client Detail

Vraća osnovne informacije o klijentu na osnovu CoreId-a.

**Endpoint:** `GET /api/Client/GetClientDetail/{coreId}`

**Parametri:**
- `coreId` (string, required) - Jedinstveni identifikator klijenta u Core sistemu

**Odgovor 200 OK:**
```json
{
  "coreId": "CORE1234",
  "identityNumber": "ID123456",
  "firstName": "Marko",
  "lastName": "Markovic",
  "email": "marko.markovic@example.com",
  "phoneNumber": "+381 11 1234567",
  "dateOfBirth": "1985-05-15T00:00:00",
  "address": "Bulevar Kralja Aleksandra 123",
  "city": "Beograd",
  "country": "Srbija",
  "postalCode": "11000",
  "clientStatus": "Active",
  "registrationDate": "2020-01-15T00:00:00"
}
```

**Greške:**
- `404 Not Found` - Klijent sa datim CoreId nije pronađen
- `500 Internal Server Error` - Greška na serveru

**Primer poziva (C#):**
```csharp
using var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://localhost:5001");

var response = await httpClient.GetAsync("/api/Client/GetClientDetail/CORE1234");
if (response.IsSuccessStatusCode)
{
    var clientDetail = await response.Content.ReadFromJsonAsync<ClientDetailInfo>();
}
```

---

### 2. Get Client Detail Extended

Vraća proširene informacije o klijentu sa dodatnim poljima.

**Endpoint:** `GET /api/Client/GetClientDetailExtended/{coreId}`

**Parametri:**
- `coreId` (string, required) - Jedinstveni identifikator klijenta

**Odgovor 200 OK:**
```json
{
  "coreId": "CORE1234",
  "identityNumber": "ID123456",
  "firstName": "Marko",
  "lastName": "Markovic",
  "middleName": "Petrovic",
  "email": "marko.markovic@example.com",
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
  "clientType": "Premium",
  "registrationDate": "2020-01-15T00:00:00",
  "lastModifiedDate": "2025-10-22T00:00:00",
  "taxNumber": "123456789",
  "bankAccount": "160-123456-78",
  "notes": "VIP client with excellent payment history",
  "isActive": true,
  "creditLimit": 50000.00,
  "preferredLanguage": "sr-RS"
}
```

**Greške:**
- `404 Not Found` - Klijent nije pronađen
- `500 Internal Server Error` - Greška na serveru

**Primer poziva (JavaScript/Fetch):**
```javascript
const response = await fetch('https://localhost:5001/api/Client/GetClientDetailExtended/CORE1234');
if (response.ok) {
  const clientDetail = await response.json();
  console.log(clientDetail);
}
```

---

### 3. Get Client Data

Vraća poslovne podatke o klijentu uključujući balans, porudžbine i metadata.

**Endpoint:** `GET /api/Client/GetClientData/{clientId}`

**Parametri:**
- `clientId` (string, required) - Jedinstveni identifikator klijenta

**Odgovor 200 OK:**
```json
{
  "clientId": "CLIENT123",
  "coreId": "CORE5678",
  "identityNumber": "ID654321",
  "fullName": "Marko Markovic",
  "email": "marko.markovic@example.com",
  "phoneNumber": "+381 64 1234567",
  "clientStatus": "Active",
  "registrationDate": "2020-01-15T00:00:00",
  "totalBalance": 15750.50,
  "totalOrders": 47,
  "lastOrderDate": "2025-10-24T00:00:00",
  "preferredPaymentMethod": "Credit Card",
  "tags": ["VIP", "Frequent Buyer", "Premium"],
  "metadata": {
    "loyaltyPoints": 1250,
    "preferredCategory": "Electronics",
    "newsletter": true,
    "referralCode": "MKV123"
  }
}
```

**Greške:**
- `404 Not Found` - Klijent nije pronađen
- `500 Internal Server Error` - Greška na serveru

**Primer poziva (Python):**
```python
import requests

response = requests.get('https://localhost:5001/api/Client/GetClientData/CLIENT123')
if response.status_code == 200:
    client_data = response.json()
    print(client_data)
```

---

### 4. Client Exists

Proverava da li klijent sa datim matičnim brojem postoji i vraća CoreId ako postoji.

**Endpoint:** `GET /api/Client/ClientExists/{identityNumber}`

**Parametri:**
- `identityNumber` (string, required) - Matični broj klijenta

**Odgovor 200 OK:**
```json
"CORE5678"
```

**Greške:**
- `404 Not Found` - Klijent sa datim matičnim brojem ne postoji
- `500 Internal Server Error` - Greška na serveru

**Primer poziva (C#):**
```csharp
var response = await httpClient.GetAsync("/api/Client/ClientExists/ID123456");
if (response.IsSuccessStatusCode)
{
    var coreId = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Client exists with CoreId: {coreId}");
}
else if (response.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine("Client does not exist");
}
```

---

### 5. Get Client Identity Number

Vraća matični broj klijenta na osnovu CoreId-a.

**Endpoint:** `GET /api/Client/GetClientIdentityNumber/{coreId}`

**Parametri:**
- `coreId` (string, required) - Core identifikator klijenta

**Odgovor 200 OK:**
```json
"ID123456"
```

**Greške:**
- `404 Not Found` - Klijent nije pronađen
- `500 Internal Server Error` - Greška na serveru

**Primer poziva (cURL):**
```bash
curl -X GET "https://localhost:5001/api/Client/GetClientIdentityNumber/CORE1234" -H "accept: application/json"
```

---

## Testiranje Grešaka

Za testiranje različitih scenarija greške, koristite sledeće vrednosti:

| Parametar | Rezultat |
|-----------|----------|
| `notfound` | Vraća 404 Not Found |
| `0` ili `0000000000` | Vraća 404 Not Found |
| `error` | Vraća 500 Internal Server Error |
| Bilo koja druga vrednost | Vraća 200 OK sa mock podacima |

**Primer:**
```
GET /api/Client/GetClientDetail/notfound  → 404 Not Found
GET /api/Client/GetClientDetail/error     → 500 Internal Server Error
GET /api/Client/GetClientDetail/CORE123   → 200 OK (mock podaci)
```

---

## Integracija u Drugu Aplikaciju

### Korak 1: Pokretanje Mock API-a

1. Otvorite projekat u Visual Studio ili VS Code
2. Pokrenite aplikaciju:
   ```bash
   dotnet run
   ```
3. API će biti dostupan na:
   - HTTPS: `https://localhost:5001`
   - HTTP: `http://localhost:5000`
   - Swagger UI: `https://localhost:5001/swagger`

### Korak 2: Instalacija u Klijent Aplikaciji

#### .NET Aplikacija

**NuGet paketi:**
```bash
dotnet add package Microsoft.Extensions.Http
dotnet add package System.Net.Http.Json
```

**Konfiguracija (appsettings.json):**
```json
{
  "ClientApi": {
    "BaseUrl": "https://localhost:5001",
    "Timeout": 30
  }
}
```

**Dependency Injection (Program.cs):**
```csharp
builder.Services.AddHttpClient("ClientApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ClientApi:BaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

**Service implementacija:**
```csharp
public class ClientApiService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClientApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ClientDetailInfo?> GetClientDetailAsync(string coreId)
    {
        var client = _httpClientFactory.CreateClient("ClientApi");
        var response = await client.GetAsync($"/api/Client/GetClientDetail/{coreId}");

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<ClientDetailInfo>();
        }

        return null;
    }
}
```

#### JavaScript/TypeScript Aplikacija

**Instalacija Axios (opciono):**
```bash
npm install axios
```

**API Service (TypeScript):**
```typescript
import axios from 'axios';

const API_BASE_URL = 'https://localhost:5001';

export interface ClientDetailInfo {
  coreId: string;
  identityNumber: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  dateOfBirth: string;
  address: string;
  city: string;
  country: string;
  postalCode: string;
  clientStatus: string;
  registrationDate: string;
}

export class ClientApiService {

  async getClientDetail(coreId: string): Promise<ClientDetailInfo | null> {
    try {
      const response = await axios.get<ClientDetailInfo>(
        `${API_BASE_URL}/api/Client/GetClientDetail/${coreId}`
      );
      return response.data;
    } catch (error) {
      console.error('Error fetching client detail:', error);
      return null;
    }
  }

  async clientExists(identityNumber: string): Promise<string | null> {
    try {
      const response = await axios.get<string>(
        `${API_BASE_URL}/api/Client/ClientExists/${identityNumber}`
      );
      return response.data;
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.status === 404) {
        return null;
      }
      throw error;
    }
  }
}
```

#### Python Aplikacija

**Instalacija:**
```bash
pip install requests
```

**API Client:**
```python
import requests
from typing import Optional, Dict, Any

class ClientApiClient:
    def __init__(self, base_url: str = "https://localhost:5001"):
        self.base_url = base_url
        self.session = requests.Session()

    def get_client_detail(self, core_id: str) -> Optional[Dict[str, Any]]:
        try:
            response = self.session.get(
                f"{self.base_url}/api/Client/GetClientDetail/{core_id}"
            )
            response.raise_for_status()
            return response.json()
        except requests.exceptions.HTTPError as e:
            if e.response.status_code == 404:
                return None
            raise

    def client_exists(self, identity_number: str) -> Optional[str]:
        try:
            response = self.session.get(
                f"{self.base_url}/api/Client/ClientExists/{identity_number}"
            )
            response.raise_for_status()
            return response.json()
        except requests.exceptions.HTTPError as e:
            if e.response.status_code == 404:
                return None
            raise

# Upotreba
client = ClientApiClient()
detail = client.get_client_detail("CORE1234")
if detail:
    print(f"Client: {detail['firstName']} {detail['lastName']}")
```

### Korak 3: CORS Konfiguracija (ako koristite web aplikaciju)

Dodajte CORS u `Program.cs` Mock API-a:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Pre app.MapControllers()
app.UseCors("AllowAll");
```

### Korak 4: SSL Sertifikat (Development)

Za development, trebaćete da poverite self-signed sertifikat:

**Windows:**
```bash
dotnet dev-certs https --trust
```

**Linux/Mac:**
```bash
dotnet dev-certs https --trust
# ili ignorišite SSL validaciju u test okruženju
```

---

## Best Practices za Integraciju

### 1. Error Handling
Uvek proveravajte HTTP status kodove i hendlujte greške:
```csharp
try
{
    var response = await client.GetAsync(url);

    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        // Klijent ne postoji
    }
    else if (!response.IsSuccessStatusCode)
    {
        // Druga greška
        var error = await response.Content.ReadAsStringAsync();
        _logger.LogError($"API Error: {error}");
    }
}
catch (HttpRequestException ex)
{
    // Mrežna greška
    _logger.LogError(ex, "Network error");
}
```

### 2. Retry Policy (Polly)
```bash
dotnet add package Polly
dotnet add package Microsoft.Extensions.Http.Polly
```

```csharp
builder.Services.AddHttpClient("ClientApi")
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
```

### 3. Timeout Configuration
```csharp
client.Timeout = TimeSpan.FromSeconds(30);
```

### 4. Logging
Logujte sve API pozive za debugging:
```csharp
_logger.LogInformation($"Calling API: {url}");
_logger.LogInformation($"Response: {response.StatusCode}");
```

---

## Swagger/OpenAPI Dokumentacija

Pristupite interaktivnoj API dokumentaciji na:
```
https://localhost:5001/swagger
```

Swagger UI omogućava:
- Pregled svih endpointa
- Testiranje API-a direktno iz browsera
- Preuzimanje OpenAPI specifikacije
- Generisanje klijent koda

---

## Deployment za Produkciju

### Kada prelazite na pravi API:

1. **Promenite Base URL** u konfiguraciji:
```json
{
  "ClientApi": {
    "BaseUrl": "https://production-api.example.com"
  }
}
```

2. **Dodajte autentifikaciju** (JWT, API Key, itd.)

3. **Uklonite simulaciju grešaka** iz kontrolera

4. **Implementirajte rate limiting**

5. **Konfigurisite monitoring i logging**

---

## Kontakt i Podrška

Za pitanja i podršku:
- GitHub Issues
- Email: support@example.com
- Dokumentacija: https://docs.example.com

---

## Verzija

**Trenutna verzija:** 1.0.0
**Datum:** 2025-10-27
