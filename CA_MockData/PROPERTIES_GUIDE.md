# Alfresco Custom Properties Guide

## Overview

CA_MockData aplikacija sada podržava dodavanje custom properties (metadata) na foldere tokom kreiranja. Properties omogućavaju čuvanje dodatnih informacija o folderima koje možete koristiti za pretragu, filtriranje, i business logiku.

## Šta su Properties?

U Alfresku, properties su metadata koja se vezuju za node-ove (folderi i dokumenti). Svaki property ima:
- **Namespace** - prefiks koji grupiše povezane properties (npr. `cm:`, `myapp:`, `bank:`)
- **Property Name** - naziv property-ja
- **Value** - vrednost (string, number, date, boolean, itd.)

**Format:** `namespace:propertyName`

**Primeri:**
- `cm:title` - Naslov (built-in Alfresco property)
- `cm:description` - Opis (built-in Alfresco property)
- `bank:clientId` - Custom property za Client ID
- `myapp:coreId` - Custom property za Core ID

## Konfigurisanje

### Enable Properties u Config

```csharp
var cfg = new Config()
{
    // ... ostale opcije ...
    UseNewFolderStructure = true,
    AddFolderProperties = true,     // ← Omogući properties
    ClientTypes = new[] { "PL", "FL", "ACC" },
    StartingCoreId = 10000000
};
```

### Trenutne Properties (Built-in)

Aplikacija automatski dodaje sledeće **built-in** properties:

```csharp
// Ove properties rade out-of-the-box, bez dodatne konfiguracije
properties["cm:title"] = "PL Client 10000000";
properties["cm:description"] = "Mock folder for PL client with CoreId 10000000";
```

## Dodavanje Custom Properties

### Step 1: Definiši Content Model u Alfrescos

Pre nego što možeš koristiti custom properties, moraš definisati **Content Model** u Alfrescos.

#### Primer Content Model XML

Kreiraj fajl `customModel.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<model name="myapp:contentModel" xmlns="http://www.alfresco.org/model/dictionary/1.0">
    <description>Custom content model for migration</description>
    <author>Your Name</author>
    <version>1.0</version>

    <!-- Import Alfresco standard models -->
    <imports>
        <import uri="http://www.alfresco.org/model/dictionary/1.0" prefix="d"/>
        <import uri="http://www.alfresco.org/model/content/1.0" prefix="cm"/>
    </imports>

    <!-- Define namespace -->
    <namespaces>
        <namespace uri="http://www.mycompany.com/model/content/1.0" prefix="myapp"/>
    </namespaces>

    <!-- Define types -->
    <types>
        <type name="myapp:clientFolder">
            <title>Client Folder</title>
            <parent>cm:folder</parent>
            <properties>
                <property name="myapp:coreId">
                    <title>Core ID</title>
                    <type>d:text</type>
                    <mandatory>false</mandatory>
                </property>
                <property name="myapp:clientType">
                    <title>Client Type</title>
                    <type>d:text</type>
                    <mandatory>false</mandatory>
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
                <property name="myapp:createdDate">
                    <title>Created Date</title>
                    <type>d:datetime</type>
                    <mandatory>false</mandatory>
                </property>
                <property name="myapp:migrationBatch">
                    <title>Migration Batch</title>
                    <type>d:text</type>
                    <mandatory>false</mandatory>
                </property>
            </properties>
        </type>
    </types>
</model>
```

#### Deploy Content Model

1. **Upload u Alfresco Share:**
   - Login u Alfresco Share kao admin
   - Repository → Data Dictionary → Models
   - Upload `customModel.xml`
   - Aktiviraj model

2. **Ili koristi Alfresco Repository REST API:**
   ```bash
   curl -X POST "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/models" \
     -H "Authorization: Basic YWRtaW46YWRtaW4=" \
     -H "Content-Type: application/json" \
     -d @customModel.xml
   ```

### Step 2: Ažuriraj GenerateFolderProperties

U `Program.cs`, odkomentiraj i prilagodi custom properties:

```csharp
private static Dictionary<string, object> GenerateFolderProperties(string clientType, int coreId)
{
    var properties = new Dictionary<string, object>();

    // Built-in properties (rade uvek)
    properties["cm:title"] = $"{clientType} Client {coreId}";
    properties["cm:description"] = $"Mock folder for {clientType} client with CoreId {coreId}";

    // Custom properties (zahtevaju Content Model)
    properties["myapp:coreId"] = coreId.ToString();
    properties["myapp:clientType"] = clientType;
    properties["myapp:createdDate"] = DateTime.UtcNow.ToString("o");
    properties["myapp:migrationBatch"] = "BATCH-2025-01";

    return properties;
}
```

### Step 3: (Optional) Promeni Node Type

Ako želiš da koristiš custom type umesto `cm:folder`:

```csharp
private static async Task<string> CreateFolderAsync(
    HttpClient http,
    Config cfg,
    string parentId,
    string name,
    CancellationToken ct,
    Dictionary<string, object>? properties = null)
{
    var url = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children";

    object payload;
    if (properties != null && properties.Count > 0)
    {
        payload = new
        {
            name,
            nodeType = "myapp:clientFolder",  // ← Custom type umesto cm:folder
            properties
        };
    }
    else
    {
        payload = new { name, nodeType = "cm:folder" };
    }

    // ... rest of code
}
```

## Primeri Custom Properties

### Banking/Financial Domain

```csharp
// Example Content Model: bank:contentModel
properties["bank:clientId"] = coreId.ToString();
properties["bank:clientType"] = clientType;
properties["bank:accountStatus"] = "ACTIVE";
properties["bank:branchCode"] = "001";
properties["bank:openingDate"] = DateTime.UtcNow.ToString("o");
properties["bank:manager"] = "John Doe";
```

### Document Management

```csharp
// Example Content Model: dms:contentModel
properties["dms:documentCategory"] = clientType;
properties["dms:retentionPeriod"] = "7";  // years
properties["dms:securityLevel"] = "CONFIDENTIAL";
properties["dms:owner"] = "Migration System";
```

### Generic Metadata

```csharp
// Example Content Model: meta:contentModel
properties["meta:source"] = "Migration";
properties["meta:version"] = "1.0";
properties["meta:tags"] = new[] { clientType, "migrated", "2025" };
properties["meta:externalId"] = coreId.ToString();
```

## Verifikacija Properties u Alfrescos

### 1. Preko Alfresco Share UI

1. Login u Alfresco Share
2. Navigate do svog foldera (npr. `dosie-PL/PL10000000`)
3. Klikni na folder
4. Klikni "View Details" ili "Edit Properties"
5. Proveri da li vidiš properties:
   - Title: "PL Client 10000000"
   - Description: "Mock folder for PL client with CoreId 10000000"
   - Custom properties (ako si ih dodao)

### 2. Preko REST API

```bash
# Get folder properties
curl -X GET "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1/nodes/{folder-id}" \
  -H "Authorization: Basic YWRtaW46YWRtaW4="
```

Response:
```json
{
  "entry": {
    "id": "abc-123-def",
    "name": "PL10000000",
    "nodeType": "cm:folder",
    "properties": {
      "cm:title": "PL Client 10000000",
      "cm:description": "Mock folder for PL client with CoreId 10000000",
      "myapp:coreId": "10000000",
      "myapp:clientType": "PL"
    }
  }
}
```

### 3. Preko CMIS Query

```sql
SELECT * FROM cm:folder
WHERE cm:title LIKE '%PL Client%'
  AND myapp:clientType = 'PL'
```

## Troubleshooting

### Problem: "Unknown property: myapp:coreId"

**Uzrok:** Content Model nije deploy-ovan ili nije aktiviran

**Rešenje:**
1. Proveri da li je Content Model upload-ovan u Repository → Data Dictionary → Models
2. Proveri da li je model aktiviran
3. Restartuj Alfresco server nakon deploy-a modela

### Problem: "Property type mismatch"

**Uzrok:** Tip vrednosti ne odgovara tipu definisanom u Content Model

**Rešenje:**
```csharp
// Pogrešno - int umesto string
properties["myapp:coreId"] = coreId;  // ❌

// Ispravno
properties["myapp:coreId"] = coreId.ToString();  // ✅

// Za datetime
properties["myapp:createdDate"] = DateTime.UtcNow.ToString("o");  // ISO 8601

// Za boolean
properties["myapp:isActive"] = true;  // Boolean direktno

// Za int/number
properties["myapp:count"] = 100;  // Number direktno
```

### Problem: "Constraint violation"

**Uzrok:** Vrednost ne zadovoljava constraint definisan u Content Model

**Rešenje:**
```csharp
// Content Model definiše constraint: allowedValues = [PL, FL, ACC]

// Pogrešno
properties["myapp:clientType"] = "INVALID";  // ❌

// Ispravno
properties["myapp:clientType"] = "PL";  // ✅
```

## Best Practices

### 1. Koristi Namespace-ove

```csharp
// Dobro - jasno grupisanje
properties["myapp:coreId"] = ...;
properties["myapp:clientType"] = ...;

// Loše - mesovito
properties["coreId"] = ...;  // Može konfliktovati sa drugim modelima
```

### 2. Validacija Pre Slanja

```csharp
private static Dictionary<string, object> GenerateFolderProperties(string clientType, int coreId)
{
    var properties = new Dictionary<string, object>();

    // Validacija
    if (!new[] { "PL", "FL", "ACC" }.Contains(clientType))
    {
        throw new ArgumentException($"Invalid client type: {clientType}");
    }

    properties["myapp:clientType"] = clientType;
    properties["myapp:coreId"] = coreId.ToString();

    return properties;
}
```

### 3. Mandatory vs Optional Properties

```csharp
// Uvek dodaj mandatory properties
properties["myapp:coreId"] = coreId.ToString();  // mandatory

// Optional properties - dodaj samo ako postoje
if (!string.IsNullOrEmpty(branchCode))
{
    properties["myapp:branchCode"] = branchCode;
}
```

### 4. Dokumentuj Svoj Content Model

Kreiraj documentation za svoj Content Model:

```
Content Model: myapp:contentModel

Properties:
- myapp:coreId (text, mandatory) - Client Core ID from core banking system
- myapp:clientType (text, mandatory) - Type: PL, FL, or ACC
- myapp:createdDate (datetime, optional) - Date when folder was created
- myapp:migrationBatch (text, optional) - Migration batch identifier
```

## Advanced: Aspects

Umesto korišćenja custom type, možeš koristiti **Aspects** za dodavanje properties:

```xml
<!-- In Content Model -->
<aspects>
    <aspect name="myapp:clientAspect">
        <title>Client Aspect</title>
        <properties>
            <property name="myapp:coreId">
                <title>Core ID</title>
                <type>d:text</type>
            </property>
        </properties>
    </aspect>
</aspects>
```

```csharp
// U CreateFolderAsync
payload = new
{
    name,
    nodeType = "cm:folder",
    aspectNames = new[] { "myapp:clientAspect" },  // Apply aspect
    properties
};
```

## Summary

- ✅ Built-in properties (`cm:title`, `cm:description`) rade odmah
- ⚙️ Custom properties zahtevaju Content Model
- 📝 Namespace format: `namespace:propertyName`
- 🔍 Možeš pretraživati foldere po properties
- 🎯 Koristi za business logiku i metadata

---

**Datum:** 2025-10-20
**Status:** Ready to Use
