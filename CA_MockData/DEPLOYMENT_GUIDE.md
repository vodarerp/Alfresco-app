# Deployment Guide - Banking Content Model

## Overview

Ovaj guide objašnjava kako deploy-ovati `bankContentModel.xml` u Alfresco da bi custom properties radile.

## Prerequisites

- Alfresco Server (verzija 6.x ili novija)
- Admin pristup Alfresco Share UI ili REST API
- `bankContentModel.xml` fajl (nalazi se u ovom folderu)

---

## Metod 1: Deploy preko Alfresco Share UI (Preporučeno za Development)

### Step 1: Login u Alfresco Share

1. Otvori browser i idi na `http://localhost:8080/share` (ili tvoj Alfresco URL)
2. Logiraj se kao **admin** korisnik

### Step 2: Navigate do Models folder

1. Klikni na **Repository** (gornji meni)
2. Navigate: **Data Dictionary → Models**

### Step 3: Upload Content Model

1. Klikni na **Upload** dugme
2. Izaberi `bankContentModel.xml` fajl
3. Klikni **Upload**

### Step 4: Aktiviraj Model

1. Refresh stranicu (`F5`)
2. Pronađi `bankContentModel.xml` u listi
3. Klikni na **More → Model Manager Actions → Activate**
4. Potvrdi aktivaciju

### Step 5: Verifikuj Aktivaciju

1. Model bi trebao da ima status **Active**
2. Možeš videti properties u **Model Details**

---

## Metod 2: Deploy preko REST API (Preporučeno za Production)

### Step 1: Pripremi Content Model

```bash
# Navigate do CA_MockData foldera
cd "C:\Users\Nikola Preradov\source\repos\Alfresco\CA_MockData"
```

### Step 2: Upload Content Model

```bash
# Windows PowerShell
$base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:admin"))

Invoke-RestMethod -Uri "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/models" `
  -Method POST `
  -Headers @{ Authorization = "Basic $base64Auth" } `
  -ContentType "application/xml" `
  -InFile "bankContentModel.xml"
```

```bash
# Linux/Mac/WSL
curl -X POST "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm" \
  -H "Authorization: Basic YWRtaW46YWRtaW4=" \
  -H "Content-Type: application/xml" \
  -d @bankContentModel.xml
```

### Step 3: Aktiviraj Model

```bash
# PowerShell
Invoke-RestMethod -Uri "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm/bank:contentModel?select=status" `
  -Method PUT `
  -Headers @{ Authorization = "Basic $base64Auth" } `
  -ContentType "application/json" `
  -Body '{"status":"ACTIVE"}'
```

```bash
# Linux/Mac/WSL
curl -X PUT "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm/bank:contentModel?select=status" \
  -H "Authorization: Basic YWRtaW46YWRtaW4=" \
  -H "Content-Type: application/json" \
  -d '{"status":"ACTIVE"}'
```

---

## Metod 3: Deploy preko Filesystem (Advanced)

### Step 1: Copy Model File

```bash
# Copy bankContentModel.xml to Alfresco extension folder
# Default location: {ALFRESCO_HOME}/tomcat/shared/classes/alfresco/extension/model

# Windows
copy bankContentModel.xml "C:\alfresco\tomcat\shared\classes\alfresco\extension\model\"

# Linux
cp bankContentModel.xml /opt/alfresco/tomcat/shared/classes/alfresco/extension/model/
```

### Step 2: Kreirati Bootstrap Context

Kreiraj `custom-model-context.xml` u `{ALFRESCO_HOME}/tomcat/shared/classes/alfresco/extension`:

```xml
<?xml version='1.0' encoding='UTF-8'?>
<beans xmlns="http://www.springframework.org/schema/beans"
       xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
       xsi:schemaLocation="http://www.springframework.org/schema/beans
       http://www.springframework.org/schema/beans/spring-beans-3.0.xsd">

    <!-- Bootstrap the bank content model -->
    <bean id="bank.dictionaryBootstrap" parent="dictionaryModelBootstrap" depends-on="dictionaryBootstrap">
        <property name="models">
            <list>
                <value>alfresco/extension/model/bankContentModel.xml</value>
            </list>
        </property>
    </bean>
</beans>
```

### Step 3: Restart Alfresco

```bash
# Windows
# Stop Alfresco service and start it again

# Linux
sudo systemctl restart alfresco
```

---

## Verifikacija Deployment-a

### 1. Provera preko Share UI

1. Login u Alfresco Share
2. Repository → Data Dictionary → Models
3. Potvrdi da je `bankContentModel.xml` **Active**

### 2. Provera preko REST API

```bash
# Get model status
curl -X GET "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm/bank:contentModel" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="
```

Response:
```json
{
  "entry": {
    "name": "bank:contentModel",
    "status": "ACTIVE",
    "description": "Banking content model for client folders and documents",
    "namespaceUri": "http://www.bank.com/model/content/1.0",
    "namespacePrefix": "bank"
  }
}
```

### 3. Test sa CA_MockData

```bash
# Pokreni CA_MockData sa properties enabled
cd CA_MockData
dotnet run
```

U CA_MockData `Program.cs`:
```csharp
var cfg = new Config()
{
    // ... other settings ...
    AddFolderProperties = true  // Mora biti true!
};
```

### 4. Proveri Properties na Folderu

```bash
# Get folder properties via REST API
curl -X GET "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{folder-id}" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="
```

Response bi trebao da sadrži:
```json
{
  "entry": {
    "id": "...",
    "name": "PL10000000",
    "nodeType": "cm:folder",
    "properties": {
      "cm:title": "PL Client 10000000",
      "cm:description": "Mock folder for PL client with CoreId 10000000",
      "bank:clientType": "PL",
      "bank:coreId": "10000000",
      "bank:clientName": "Privredno Društvo 10000000",
      "bank:mbrJmbg": "12345678",
      "bank:uniqueFolderId": "DE-10000000-00010-10123456_20250115120000",
      "bank:depositProcessedDate": "2024-03-15T10:30:00.000Z",
      // ... sve ostale properties
    }
  }
}
```

---

## Troubleshooting

### Problem: Model nije vidljiv u Share UI

**Rešenje:**
1. Proveri da li je fajl upload-ovan u **Data Dictionary → Models**
2. Refresh stranicu (F5)
3. Clear browser cache

### Problem: "Model already exists"

**Rešenje:**
```bash
# Delete existing model prvo
curl -X DELETE "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/cmm/bank:contentModel" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="

# Zatim upload novi
```

### Problem: "bank:clientType isn't a valid QName"

**Uzrok:** Model nije deploy-ovan ili aktiviran u Alfresco

**Rešenje:**
1. Proveri da li je `bankContentModel.xml` upload-ovan u **Data Dictionary → Models**
2. Proveri da li je model **ACTIVE** (ne DRAFT)
3. Ako nije aktiviran:
   - Share UI → Repository → Data Dictionary → Models
   - Pronađi `bankContentModel.xml`
   - Klikni **More → Model Manager Actions → Activate**

### Problem: "Constraint violation"

**Uzrok:** Vrednost ne zadovoljava constraint (npr. client type mora biti PL, FL, ili ACC)

**Rešenje:**
Proveri `GenerateFolderProperties` metodu - sve vrednosti moraju biti validne prema Content Model constraints.

### Problem: Model se ne učitava nakon restart-a

**Rešenje:**
1. Proveri da li je `custom-model-context.xml` pravilno konfigurisan
2. Proveri Alfresco log fajlove za greške:
   - `{ALFRESCO_HOME}/tomcat/logs/catalina.out` (Linux)
   - `{ALFRESCO_HOME}\tomcat\logs\catalina.out` (Windows)

---

## Banking Content Model - Property Lista

Evo kompletne liste svih properties definisanih u `bankContentModel.xml`:

| Property Name | Type | Description | Example |
|--------------|------|-------------|---------|
| `bank:clientType` | text | Tip klijenta (PL/FL/ACC) | "PL" |
| `bank:creator` | text | Kreator | "Migration System" |
| `bank:uniqueFolderId` | text | Jedinstveni identifikator | "DE-10000000-00010-10123456_20250115120000" |
| `bank:barclex` | text | Barclex | "BX12345" |
| `bank:collaborator` | text | Saradnik | "Partner Bank A" |
| `bank:mbrJmbg` | text | MBR/JMBG | "12345678" |
| `bank:coreId` | text | Core ID | "10000000" |
| `bank:clientName` | text | Naziv klijenta | "Privredno Društvo" |
| `bank:batch` | text | Partija | "BATCH-2025-01-001" |
| `bank:clientSubtype` | text | Podtip klijenta | "SME" |
| `bank:staff` | text | Staff indikator | "Y" / "N" |
| `bank:opuUser` | text | OPU korisnika | "OPU-123" |
| `bank:opuRealization` | text | OPU/ID realizacije | "OPU-123/ID-4567" |
| `bank:productType` | text | Tip proizvoda | "00010" |
| `bank:contractNumber` | text | Broj ugovora | "10123456" |
| `bank:depositProcessedDate` | datetime | Datum procesiranja | "2024-03-15T10:30:00.000Z" |
| `bank:residency` | text | Rezidentnost | "Resident" |
| `bank:source` | text | Izvor | "Migration" |
| `bank:status` | text | Status | "ACTIVE" |
| `bank:archiveDate` | datetime | Datum arhiviranja | "2024-06-01T00:00:00.000Z" |
| `bank:segment` | text | Segment | "Retail" |

---

## Next Steps

1. ✅ Deploy `bankContentModel.xml`
2. ✅ Aktiviraj model
3. ✅ Verifikuj deployment
4. ✅ Pokreni CA_MockData sa `AddFolderProperties = true`
5. ✅ Proveri da folderi imaju sve properties

---

**Datum:** 2025-10-20
**Status:** Ready to Deploy
