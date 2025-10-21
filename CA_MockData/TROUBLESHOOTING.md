# Troubleshooting Guide - CA_MockData

## 400 Bad Request Error

### Problem: SendWithRetryAsync throws HTTP 400 Bad Request

```
[ERROR] HTTP 400 Bad Request
URL: http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{id}/children
```

### Najčešći Uzroci

#### 0. Invalid Where Clause - "InvalidProperty" (REŠENO!)

**Simptom:**
```
[ERROR] HTTP 400 Bad Request
URL: ...?where=(nodeType='cm:folder' AND name='dosie-PL')
Response: {"error":{"errorKey":"framework.exception.InvalidProperty"...
"briefSummary":"The property 'name' with value 'dosie-PL' isn't supported for EQUALS comparison"}}
```

**Uzrok:**
Alfresco ne podržava `name` property u `where` klauzuli za EQUALS comparison.

**Rešenje:**
✅ **OVO JE VEĆ POPRAVLJENO!** (verzija 2025-10-20)

Stari kod:
```csharp
// ❌ Ne radi - name nije podržan u where klauzuli
var searchUrl = $"...?where=(nodeType='cm:folder' AND name='{name}')";
```

Novi kod:
```csharp
// ✅ Radi - listuje sve foldere i filtrira client-side
var searchUrl = $"...?where=(nodeType='cm:folder')";
// ... zatim filtrira po name-u u C# kodu
```

Samo ponovo build-uj projekat i pokreni - ova greška je rešena!

### Najčešći Uzroci (nastavak)

#### 1. Content Model Nije Deploy-ovan (Najčešće!)

**Simptom:**
- `AddFolderProperties = true` u Config
- Properties se šalju u request-u
- Alfresco ne prepoznaje `bank:*` properties jer Content Model nije deploy-ovan

**Rešenje A: Deploy Content Model**

```bash
# Step 1: Deploy bankContentModel.xml u Alfresco
# Pogledaj DEPLOYMENT_GUIDE.md za detaljne instrukcije

# Metod 1: Share UI
1. Login u Alfresco Share
2. Repository → Data Dictionary → Models
3. Upload bankContentModel.xml
4. Activate model

# Metod 2: REST API
curl -X POST "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/models" \
  -H "Authorization: Basic YWRtaW46YWRtaW4=" \
  -H "Content-Type: application/xml" \
  -d @bankContentModel.xml
```

**Rešenje B: Disable Properties (Temporary)**

```csharp
var cfg = new Config()
{
    // ... other settings ...
    AddFolderProperties = false  // ← Disable properties
};
```

#### 2. Nevalidne Property Vrednosti

**Simptom:**
- Content Model je deploy-ovan
- I dalje dobijate 400 Bad Request
- Log pokazuje specifičan property koji fail-uje

**Rešenje:**

Proveri constraints u `bankContentModel.xml`:

```xml
<!-- Example constraint -->
<property name="bank:clientType">
    <constraints>
        <constraint type="LIST">
            <parameter name="allowedValues">
                <list>
                    <value>PL</value>
                    <value>FL</value>
                    <value>ACC</value>
                </list>
            </parameter>
        </constraint>
    </constraints>
</property>
```

Proveri da `GenerateFolderProperties` generiše validne vrednosti:

```csharp
// ✅ Validno - clientType je PL, FL, ili ACC
properties["bank:clientType"] = clientType;

// ❌ Nevalidno - vrednost nije u listi
properties["bank:clientType"] = "INVALID";
```

#### 3. Pogrešan Tip Podatka

**Simptom:**
- Property zahteva `d:datetime` ali šaljemo string

**Rešenje:**

```csharp
// ❌ Pogrešno - šaljemo string
properties["bank:depositProcessedDate"] = "2024-01-15";

// ✅ Ispravno - šaljemo ISO 8601 datetime
properties["bank:depositProcessedDate"] = DateTime.UtcNow.ToString("o");

// ✅ Takođe ispravno
properties["bank:depositProcessedDate"] = "2024-01-15T10:30:00.000Z";
```

### Automatic Fallback Mode

Aplikacija automatski pokušava bez properties ako fail-uje sa properties:

```csharp
try
{
    // Prvo pokušaj sa properties
    folderId = await CreateFolderAsync(http, cfg, parentId, folderName, cts.Token, properties);
}
catch (HttpRequestException ex) when (ex.Message.Contains("400") && properties != null)
{
    // Ako fail-uje, probaj bez properties
    Console.WriteLine($"[WARNING] Failed to create folder with properties. Trying without properties...");
    folderId = await CreateFolderAsync(http, cfg, parentId, folderName, cts.Token, null);
}
```

Ako vidiš ovo warning, znači da properties ne rade - deploy Content Model!

---

## 401 Unauthorized Error

### Problem: HTTP 401 Unauthorized

**Uzrok:** Neispravni credentials

**Rešenje:**

```csharp
var cfg = new Config()
{
    BaseUrl = "http://localhost:8080/",
    Username = "admin",  // ← Proveri username
    Password = "admin"   // ← Proveri password
};
```

---

## 404 Not Found Error

### Problem: HTTP 404 Not Found

**Uzrok 1:** Pogrešan `RootParentId`

**Rešenje:**

```csharp
// Proveri da li folder postoji
curl -X GET "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{RootParentId}" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="

// Ili u Alfresco Share:
1. Navigate do foldera
2. Klikni "View Details"
3. Kopiraj Node ID (bez workspace://SpacesStore/ prefiksa)
```

**Uzrok 2:** Pogrešan Base URL

**Rešenje:**

```csharp
// ✅ Ispravno
BaseUrl = "http://localhost:8080/"

// ❌ Pogrešno
BaseUrl = "http://localhost:8080/share"  // Share UI URL, ne Repository URL
```

---

## 409 Conflict Error

### Problem: HTTP 409 Conflict - "A node with the name already exists"

**Uzrok:** Folder sa istim imenom već postoji

**Rešenje 1: Obriši postojeće foldere**

```bash
# Obriši sve foldere u ROOT folderu
# WARNING: Ovo će obrisati SVE foldere!

# Ili koristi Alfresco Share UI da ručno obrišeš foldere
```

**Rešenje 2: Promeni `StartingCoreId`**

```csharp
var cfg = new Config()
{
    // ... other settings ...
    StartingCoreId = 20000000  // ← Promeni na drugi range
};
```

**Rešenje 3: Koristi `GetOrCreateFolderAsync` (već implementirano za dosie foldere)**

---

## 429 Too Many Requests Error

### Problem: HTTP 429 Too Many Requests

**Uzrok:** Previše zahteva ka Alfresco serveru

**Rešenje 1: Smanji Parallelism**

```csharp
var cfg = new Config()
{
    // ... other settings ...
    DegreeOfParallelism = 4,  // ← Smanji sa 8 na 4
};
```

**Rešenje 2: Povećaj Retry Delay**

```csharp
var cfg = new Config()
{
    // ... other settings ...
    MaxRetries = 10,           // ← Povećaj retries
    RetryBaseDelayMs = 200     // ← Povećaj delay
};
```

---

## Folder se Kreira ali bez Properties

### Problem: Folder se kreira ali nema properties

**Provera:**

```bash
# Get folder properties
curl -X GET "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{folder-id}" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="
```

**Uzrok 1:** `AddFolderProperties = false`

**Rešenje:**

```csharp
var cfg = new Config()
{
    // ... other settings ...
    AddFolderProperties = true  // ← Mora biti true!
};
```

**Uzrok 2:** Content Model nije aktiviran

**Rešenje:**

```bash
# Proveri status modela
curl -X GET "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm/bank:contentModel" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="

# Aktiviraj model
curl -X PUT "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm/bank:contentModel?select=status" \
  -H "Authorization: Basic YWRtaW46YWRtaW4=" \
  -H "Content-Type: application/json" \
  -d '{"status":"ACTIVE"}'
```

---

## Connection Refused / Cannot Connect

### Problem: Cannot connect to Alfresco

**Provera:**

```bash
# Test connection
curl http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/-root-
```

**Uzrok 1:** Alfresco nije pokrenut

**Rešenje:**

```bash
# Windows
# Start Alfresco service

# Linux
sudo systemctl start alfresco

# Docker
docker start alfresco
```

**Uzrok 2:** Pogrešan port

**Rešenje:**

```csharp
// Proveri port - default je 8080
BaseUrl = "http://localhost:8080/"

// Ako koristiš drugi port:
BaseUrl = "http://localhost:9090/"
```

---

## Debugging Tips

### 1. Enable Detailed Logging

Aplikacija sada automatski loguje sve 400 Bad Request greške:

```
[ERROR] HTTP 400 Bad Request
URL: http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{id}/children
Method: POST
Request Body: {"name":"PL10000000","nodeType":"cm:folder","properties":{...}}
Response: {"error":{"errorKey":"...","statusCode":400,...}}
```

### 2. Test Properties Ručno

```bash
# Test kreiranje foldera sa properties
curl -X POST "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children" \
  -H "Authorization: Basic YWRtaW46YWRtaW4=" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "TestFolder",
    "nodeType": "cm:folder",
    "properties": {
      "cm:title": "Test Title",
      "bank:clientType": "PL",
      "bank:coreId": "12345"
    }
  }'
```

### 3. Proveri Alfresco Logs

```bash
# Alfresco log fajlovi
# Linux
tail -f /opt/alfresco/tomcat/logs/catalina.out

# Windows
# C:\alfresco\tomcat\logs\catalina.out
```

### 4. Test bez Properties

```csharp
// Privremeno disable properties da proveriš da li osnovna logika radi
var cfg = new Config()
{
    // ... other settings ...
    AddFolderProperties = false
};
```

Ako radi bez properties ali ne sa properties → Content Model problem!

---

## Quick Checklist

Kada dobiješ 400 Bad Request:

- [ ] Da li je `bankContentModel.xml` upload-ovan?
- [ ] Da li je Content Model **aktiviran** (status = ACTIVE)?
- [ ] Da li je `AddFolderProperties = true`?
- [ ] Da li su credentials ispravni (`admin:admin`)?
- [ ] Da li je `RootParentId` validan?
- [ ] Da li Alfresco server radi?

---

## Getting Help

Ako i dalje imaš problem:

1. **Pokreni sa logging-om** - aplikacija će ispisati detalje greške
2. **Kopiraj error output** - Request Body i Response
3. **Proveri Alfresco logs** - možda ima više detalja
4. **Test ručno sa curl** - proveri da li problem je u aplikaciji ili Alfrescos

---

**Datum:** 2025-10-20
**Status:** Complete
