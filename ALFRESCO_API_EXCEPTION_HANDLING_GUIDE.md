# Alfresco API Exception Handling - Implementation Guide

## Pregled

Ovaj guide pokazuje kako da dodaš exception handling za `AlfrescoTimeoutException` i `AlfrescoRetryExhaustedException` u najniži sloj - direktno u `AlfrescoWriteApi` i `AlfrescoReadApi` klase.

---

## Zašto u najnižem sloju?

### ✅ Prednosti:
1. **Dodatni kontekst** - Loguješ tačne parametre operacije (folderId, folderName, itd.)
2. **Centralizovano** - Svi Alfresco API pozivi imaju isti exception handling
3. **Dokumentacija** - XML komentari pokazuju koje exceptione metoda može baciti
4. **Early detection** - Uhvatiš greške odmah, ne čekaš viši sloj

### ❌ Alternativa (viši slojevi):
- Moraš duplirati exception handling u svakom servisu (MigrationWorker, MoveService, itd.)
- Gubiš kontekst o tome šta je tačno failovalo

---

## Pristup 1: Direktan try-catch u svakoj metodi (preporučeno za kritične operacije)

### Primer: CreateFolderAsync

```csharp
/// <summary>
/// Creates a new folder in Alfresco
/// </summary>
/// <exception cref="AlfrescoTimeoutException">Thrown when operation times out after all retries</exception>
/// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
/// <exception cref="AlfrescoException">Thrown for other Alfresco-specific errors</exception>
public async Task<string> CreateFolderAsync(
    string parentFolderId,
    string newFolderName,
    Dictionary<string, object>? properties,
    string? customNodeType,
    CancellationToken ct = default)
{
    try
    {
        // Tvoja postojeća logika...
        return await CreateFolderInternalAsync(parentFolderId, newFolderName, properties, customNodeType, jsonSerializerSettings, ct);
    }
    catch (AlfrescoTimeoutException timeoutEx)
    {
        // Loguj sa dodatnim kontekstom
        _fileLogger.LogError(
            "⏱️ TIMEOUT: CreateFolder failed - Parent: {ParentId}, FolderName: {FolderName}, Timeout: {Timeout}s",
            parentFolderId, newFolderName, timeoutEx.TimeoutDuration.TotalSeconds);

        _dbLogger.LogError(
            "TIMEOUT: CreateFolder({FolderName}) after {Timeout}s",
            newFolderName, timeoutEx.TimeoutDuration.TotalSeconds);

        // Re-throw sa dodatnim kontekstom
        throw new AlfrescoTimeoutException(
            operation: $"CreateFolder-{newFolderName}",
            timeoutDuration: timeoutEx.TimeoutDuration,
            innerException: timeoutEx,
            additionalDetails: $"ParentId: {parentFolderId}, FolderName: {newFolderName}");
    }
    catch (AlfrescoRetryExhaustedException retryEx)
    {
        // Loguj sa dodatnim kontekstom
        _fileLogger.LogError(
            "❌ RETRY EXHAUSTED: CreateFolder failed - Parent: {ParentId}, FolderName: {FolderName}, Retries: {RetryCount}",
            parentFolderId, newFolderName, retryEx.RetryCount);

        _dbLogger.LogError(
            "RETRY EXHAUSTED: CreateFolder({FolderName}) after {RetryCount} attempts",
            newFolderName, retryEx.RetryCount);

        // Re-throw sa dodatnim kontekstom
        throw new AlfrescoRetryExhaustedException(
            operation: $"CreateFolder-{newFolderName}",
            retryCount: retryEx.RetryCount,
            lastException: retryEx.LastException,
            lastStatusCode: retryEx.LastStatusCode,
            additionalDetails: $"ParentId: {parentFolderId}, FolderName: {newFolderName}");
    }
    // Ostali exceptioni (AlfrescoNodeTypeException, AlfrescoPropertyException) se propagiraju dalje
}
```

---

## Pristup 2: Korišćenje AlfrescoExceptionHandler helper-a (za čist kod)

### Primer: MoveNodeAsync

```csharp
/// <summary>
/// Moves a node to a new parent folder
/// </summary>
/// <exception cref="AlfrescoTimeoutException">Thrown when operation times out after all retries</exception>
/// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
public async Task<bool> MoveNodeAsync(string nodeId, string targetParentId, CancellationToken ct = default)
{
    return await AlfrescoExceptionHandler.ExecuteWithExceptionHandlingAsync(
        operation: async () =>
        {
            // Tvoja postojeća logika
            var body = new { targetParentId };
            var json = JsonConvert.SerializeObject(body);
            using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _client.PostAsync(
                $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/move",
                bodyRequest,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new AlfrescoException($"Move failed: {error}", (int)response.StatusCode, error);
            }

            return true;
        },
        operationName: "MoveNode",
        logger: _fileLogger,
        context: new Dictionary<string, object>
        {
            { "NodeId", nodeId },
            { "TargetParentId", targetParentId }
        });
}
```

---

## Pristup 3: Samo dodaj XML dokumentaciju (minimalan pristup)

Ako ne želiš da menjаš logiku, samo dokumentuj da metode mogu baciti te exceptione:

```csharp
/// <summary>
/// Creates a new folder in Alfresco
/// </summary>
/// <param name="parentFolderId">Parent folder ID</param>
/// <param name="newFolderName">New folder name</param>
/// <param name="properties">Optional properties</param>
/// <param name="customNodeType">Optional custom node type</param>
/// <param name="ct">Cancellation token</param>
/// <returns>Created folder ID</returns>
/// <exception cref="AlfrescoTimeoutException">
/// Thrown when the operation times out after all retry attempts.
/// Check Polly configuration in appsettings.json (PollyPolicy.WriteOperations.TimeoutSeconds and RetryCount).
/// </exception>
/// <exception cref="AlfrescoRetryExhaustedException">
/// Thrown when all retry attempts are exhausted due to transient failures (HTTP 429, 503, 500-599, HttpRequestException).
/// Check Polly configuration in appsettings.json (PollyPolicy.WriteOperations.RetryCount).
/// </exception>
/// <exception cref="AlfrescoNodeTypeException">
/// Thrown when the specified node type is not defined in Alfresco content model.
/// </exception>
/// <exception cref="AlfrescoPropertyException">
/// Thrown when required properties are missing or invalid.
/// </exception>
public async Task<string> CreateFolderAsync(
    string parentFolderId,
    string newFolderName,
    Dictionary<string, object>? properties = null,
    string? customNodeType = null,
    CancellationToken ct = default)
{
    // Postojeća logika ostaje ista
    // Polly policies na HttpClient nivou automatski bacaju AlfrescoTimeoutException i AlfrescoRetryExhaustedException
}
```

---

## Preporuka za tvoj projekat

### Minimalan pristup (brz, dovoljno dobar):

**1. Dodaj XML dokumentaciju na sve javne metode u `AlfrescoWriteApi` i `AlfrescoReadApi`**
   - Dokumentuj da metode mogu baciti `AlfrescoTimeoutException` i `AlfrescoRetryExhaustedException`
   - Exceptioni će se automatski bacati iz Polly Fallback policy-a

**2. Dodaj try-catch samo u kritične metode:**
   - `CreateFolderAsync` (WriteApi)
   - `MoveNodeAsync` (WriteApi)
   - `UpdateNodePropertiesAsync` (WriteApi)
   - `SearchAsync` (ReadApi)

**3. Ostale metode neka propagiraju exceptione dalje**
   - Viši slojevi (MigrationWorker, MoveService) će catch-ovati i odlučiti šta dalje

---

## Primer implementacije u AlfrescoWriteApi

### Minimalan pristup za CreateFolderAsync:

```csharp
/// <exception cref="AlfrescoTimeoutException">Thrown when operation times out after all retries</exception>
/// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
public async Task<string> CreateFolderAsync(
    string parentFolderId,
    string newFolderName,
    Dictionary<string, object>? properties,
    string? customNodeType,
    CancellationToken ct = default)
{
    try
    {
        // Existing logic...
        return await CreateFolderInternalAsync(...);
    }
    catch (AlfrescoNodeTypeException nodeTypeEx)
    {
        // Existing fallback logic - ostaje isto
        return await CreateFolderInternalAsync(..., "cm:folder", ...);
    }
    catch (AlfrescoTimeoutException timeoutEx)
    {
        // NEW: Log and re-throw with context
        _fileLogger.LogError(
            "CreateFolder timeout - Parent: {ParentId}, Folder: {FolderName}",
            parentFolderId, newFolderName);
        throw; // Re-throw original exception
    }
    catch (AlfrescoRetryExhaustedException retryEx)
    {
        // NEW: Log and re-throw with context
        _fileLogger.LogError(
            "CreateFolder retry exhausted - Parent: {ParentId}, Folder: {FolderName}, Retries: {RetryCount}",
            parentFolderId, newFolderName, retryEx.RetryCount);
        throw; // Re-throw original exception
    }
}
```

**NAPOMENA:** Ne moraš kreirati novi exception - samo loguj dodatni kontekst i re-throw original!

---

## Implementacija u AlfrescoReadApi

### Primer za SearchAsync:

```csharp
/// <exception cref="AlfrescoTimeoutException">Thrown when search times out after all retries</exception>
/// <exception cref="AlfrescoRetryExhaustedException">Thrown when all retry attempts are exhausted</exception>
public async Task<PostSearchResponse> SearchAsync(PostSearchRequest request, CancellationToken ct = default)
{
    try
    {
        // Existing logic...
        var json = JsonConvert.SerializeObject(request);
        using var bodyRequest = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync(
            "/alfresco/api/-default-/public/search/versions/1/search",
            bodyRequest,
            ct).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<PostSearchResponse>(responseContent);
    }
    catch (AlfrescoTimeoutException timeoutEx)
    {
        _fileLogger?.LogError(
            "Search timeout - Query: {Query}, Language: {Language}",
            request.Query?.Query, request.Query?.Language);
        throw; // Re-throw
    }
    catch (AlfrescoRetryExhaustedException retryEx)
    {
        _fileLogger?.LogError(
            "Search retry exhausted - Query: {Query}, Retries: {RetryCount}",
            request.Query?.Query, retryEx.RetryCount);
        throw; // Re-throw
    }
}
```

---

## Koje metode treba update-ovati?

### AlfrescoWriteApi (prioritet):
1. ✅ `CreateFolderAsync` - najviše se koristi
2. ✅ `MoveNodeAsync` - kritična operacija
3. ✅ `UpdateNodePropertiesAsync` - često failuje
4. ⚠️ `CreateFileAsync` - manje kritično
5. ⚠️ `DeleteNodeAsync` - retko se koristi

### AlfrescoReadApi (prioritet):
1. ✅ `SearchAsync` - kompleksan query, često timeout-uje
2. ✅ `GetFolderByRelative` - koristi search interno
3. ⚠️ `GetNodeById` - jednostavan, retko failuje

---

## Testiranje

### Simuliraj timeout:
```json
// appsettings.json
"PollyPolicy": {
  "WriteOperations": {
    "TimeoutSeconds": 5  // Smanji na 5s za testiranje
  }
}
```

### Simuliraj retry exhausted:
- Ugasi Alfresco server
- Pozovi CreateFolderAsync
- Verifikuj da se loguje 3 retry pokušaja i baca AlfrescoRetryExhaustedException

---

## Zaključak

**Minimalan pristup (preporučeno):**
1. Dodaj XML dokumentaciju na sve javne metode
2. Dodaj try-catch samo u 5-6 najkritičnijih metoda
3. Samo loguj kontekst i re-throw (NE kreiraj novi exception)

**Maksimalan pristup (ako imaš vremena):**
1. Koristi `AlfrescoExceptionHandler.ExecuteWithExceptionHandlingAsync` za sve metode
2. Dodaj kontekst dictionary sa parametrima
3. Automatski se loguje i re-throw-uje sa dodatnim kontekstom

**Rezultat:**
- Polly policies bacaju `AlfrescoTimeoutException` i `AlfrescoRetryExhaustedException`
- AlfrescoWriteApi/ReadApi loguju dodatni kontekst
- Viši slojevi (MigrationWorker) mogu catch-ovati i odlučiti da li zaustaviti migraciju
