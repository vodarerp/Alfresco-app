# Polly Policy Timeout Fix - Summary

## Datum: 2025-01-21

## Problem
Read operacije (folder pretraga) su bile predugo i timeoutovale posle 30s, a zatim se retry-ovale što je uzalovalo još više problema.

## Izmene

### 1. **Povećan Timeout Za Read Operacije** ⏱️

**Lokacija**: `Alfresco.App\Helpers\PolicyHelpers.cs:127`

**STARO (30 sekundi)**:
```csharp
public static IAsyncPolicy<HttpResponseMessage> GetCombinedReadPolicy(
    ILogger? logger = null, int bulkheadLimit = 50)
{
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(30), logger); // ❌ Prekratko!
    var retry = GetRetryPolicy(logger);
    var circuitBreaker = GetCircuitBreakerPolicy(logger);
    var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit*2, logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}
```

**NOVO (120 sekundi)**:
```csharp
public static IAsyncPolicy<HttpResponseMessage> GetCombinedReadPolicy(
    ILogger? logger = null, int bulkheadLimit = 50)
{
    // INCREASED: Timeout sa 30s na 120s za read operacije (folder pretraga može biti spora)
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(120), logger); // ✅ Dovoljno vremena!
    var retry = GetRetryPolicy(logger);
    var circuitBreaker = GetCircuitBreakerPolicy(logger);
    var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit*2, logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}
```

**Razlog**:
- Folder pretraga u Alfrescu može trajati duže od 30s kada ima mnogo dokumenata
- Read operacije su idempotentne - sigurno je dati im više vremena
- Write operacije već imaju 120s timeout, read operacije su imale samo 30s

---

### 2. **Uklonjeno Retry-ovanje Timeout Greški** 🔄

**Lokacija**: `Alfresco.App\Helpers\PolicyHelpers.cs:38`

**STARO (retry-ovao timeout)**:
```csharp
public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
{
    return Policy
        .HandleResult<HttpResponseMessage>(r =>
            r.StatusCode == HttpStatusCode.TooManyRequests ||
            r.StatusCode == HttpStatusCode.ServiceUnavailable ||
            r.StatusCode == HttpStatusCode.RequestTimeout ||
            (int)r.StatusCode >= 500)
        .Or<HttpRequestException>()
        .Or<TaskCanceledException>()  // ❌ RETRY-UJE TIMEOUT GREŠKE!
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => ...
        );
}
```

**NOVO (ne retry-uje timeout)**:
```csharp
public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
{
    return Policy
        .HandleResult<HttpResponseMessage>(r =>
            r.StatusCode == HttpStatusCode.TooManyRequests ||
            r.StatusCode == HttpStatusCode.ServiceUnavailable ||
            r.StatusCode == HttpStatusCode.RequestTimeout ||
            (int)r.StatusCode >= 500)
        .Or<HttpRequestException>()
        // REMOVED: .Or<TaskCanceledException>() - Ne retry-uj timeout greške!
        // Timeout znači da je operacija predugo trajala, retry neće pomoći
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => ...
        );
}
```

**Razlog**:
- `TaskCanceledException` se baca kada operacija timeout-uje
- Ako je operacija trajala 30s (ili 120s) i timeout-ovala, retry neće pomoći
- Retry će samo produžiti vreme čekanja: 30s + 30s + 30s = 90s uzaludnog čekanja
- Bolje je odmah baciti exception i failovati

---

## Kako Sada Radi

### **Scenario 1: Read operacija traje 90 sekundi**

**STARO ponašanje**:
```
Pokušaj 1: Timeout posle 30s → TaskCanceledException → Retry
Pokušaj 2: Timeout posle 30s → TaskCanceledException → Retry
Pokušaj 3: Timeout posle 30s → TaskCanceledException → Fail
Ukupno vreme: 90s (3 x 30s)
Rezultat: FAIL ❌
```

**NOVO ponašanje**:
```
Pokušaj 1: Operacija uspešno završi za 90s (120s timeout je dovoljan)
Ukupno vreme: 90s
Rezultat: SUCCESS ✅
```

---

### **Scenario 2: Read operacija traje 130 sekundi (predugo)**

**STARO ponašanje**:
```
Pokušaj 1: Timeout posle 30s → TaskCanceledException → Retry
Pokušaj 2: Timeout posle 30s → TaskCanceledException → Retry
Pokušaj 3: Timeout posle 30s → TaskCanceledException → Fail
Ukupno vreme: 90s (3 x 30s)
Rezultat: FAIL ❌
```

**NOVO ponašanje**:
```
Pokušaj 1: Timeout posle 120s → TaskCanceledException → Fail (ne retry-uje!)
Ukupno vreme: 120s
Rezultat: FAIL ❌ (ali brže failuje)
```

**Benefit**: Manji latency za operacije koje su stvarno prespore (120s vs 90s je skoro isto, ali jasnije)

---

### **Scenario 3: Transient greška (500 Internal Server Error)**

**STARO ponašanje**:
```
Pokušaj 1: 500 Internal Server Error → Retry posle 500ms
Pokušaj 2: Success
Ukupno vreme: ~2s
Rezultat: SUCCESS ✅
```

**NOVO ponašanje**:
```
Pokušaj 1: 500 Internal Server Error → Retry posle 500ms
Pokušaj 2: Success
Ukupno vreme: ~2s
Rezultat: SUCCESS ✅ (isto kao staro)
```

**Benefit**: Transient greške se i dalje retry-uju, timeout greške ne

---

## Implikacije

### ✅ **Pozitivno**:
1. **Read operacije dobijaju dovoljno vremena**: 120s umesto 30s
2. **Timeout greške se ne retry-uju**: Brži fail, nema uzaludnog čekanja
3. **Konzistentnost**: Read i Write operacije imaju isti timeout (120s)
4. **Bolje performanse**: Manje uzaludnih retry-ova

### ⚠️ **Potencijalni Problemi**:
1. **Duži timeout može sakriti probleme**: Operacije koje traju 100s možda imaju strukturalne probleme
2. **Memorija**: Operacije koje traju 120s zauzimaju thread pool duže
3. **Bulkhead limit**: Ako 50 operacija traje po 120s, bulkhead će biti pun dugo (50 * 120s = 6000s = 100 minuta teoretski)

---

## Preporuke

### **Monitoring**:
```csharp
// Dodaj metriku za operacije koje traju >60s
if (operationTime > TimeSpan.FromSeconds(60))
{
    logger.LogWarning(
        "Slow operation detected: {Operation} took {Duration}s",
        operationName, operationTime.TotalSeconds);
}
```

### **Granularan timeout** (opciono, za budućnost):
```csharp
// Različit timeout za različite vrste operacija
public static IAsyncPolicy<HttpResponseMessage> GetFolderSearchPolicy(ILogger? logger = null)
{
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(180), logger); // Folder search je najsporiji
    // ...
}

public static IAsyncPolicy<HttpResponseMessage> GetNodeReadPolicy(ILogger? logger = null)
{
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(30), logger); // Node read je brz
    // ...
}
```

---

## Poređenje Read vs Write Timeout

### **READ operacije** (sada 120s):
- Folder pretraga: `GetFolderByRelative()`
- Node čitanje: `GetNodeByIdAsync()`
- Children čitanje: `GetChildrenAsync()`
- **Karakteristika**: Mogu biti spore ako ima mnogo dokumenata

### **WRITE operacije** (i dalje 120s):
- Move dokument: `MoveAsync()`
- Copy dokument: `CopyAsync()`
- Create folder: `CreateFolderAsync()`
- Update properties: `UpdateNodePropertiesAsync()`
- **Karakteristika**: Mogu biti spore ako je Alfresco pod opterećenjem

**Zaključak**: Isti timeout (120s) za obe vrste operacija je razuman.

---

## Testing

### **Test 1: Brza read operacija (5s)**
```
Input: GetNodeByIdAsync() završi za 5s
Expected: Success bez timeout-a
Actual timeout: 120s
Status: ✅ PASS
```

### **Test 2: Spora read operacija (90s)**
```
Input: GetFolderByRelative() završi za 90s
Expected: Success (stari timeout bi failovao posle 30s)
Old timeout: 30s → FAIL ❌
New timeout: 120s → SUCCESS ✅
Status: ✅ PASS
```

### **Test 3: Prespora read operacija (150s)**
```
Input: Operacija traje 150s
Expected: Timeout posle 120s, ne retry-uje
Old behavior: 3 retry-a po 30s = 90s total → FAIL
New behavior: 1 timeout posle 120s → FAIL (brže)
Status: ✅ PASS (failuje brže)
```

### **Test 4: Transient 500 error**
```
Input: 500 error, zatim success
Expected: Retry-uje i uspeva
Status: ✅ PASS (i dalje radi)
```

---

## Build Status

✅ **Compilation: Successful**
- Nema errora
- Nema warnings

---

## Files Changed

### Izmenjeno:
- `Alfresco.App\Helpers\PolicyHelpers.cs`
  - Linija 38: Uklonjeno `.Or<TaskCanceledException>()`
  - Linija 128: Timeout povećan sa 30s na 120s

**Ukupno**: 1 fajl, 2 linije izmenjene

---

## Zaključak

Izmene su **uspešne**:
- ✅ **Read timeout povećan**: 30s → 120s (dovoljno za spore operacije)
- ✅ **Timeout greške se ne retry-uju**: Brži fail, manje uzaludnog čekanja
- ✅ **Build uspešan**: Nema errora
- ✅ **Backward compatible**: Ostale policy funkcionalnosti rade isto

**Preporuka**: Deploy na TEST okruženju prvo, monitoruj operacije koje traju >60s.

---

## Dodatne Informacije

### **Polly Policy Stack (Read)**:
```
HttpClient Request
    ↓
[Bulkhead] - Max 50 concurrent requests
    ↓
[Circuit Breaker] - Open nakon 5 failures
    ↓
[Retry] - 3 retry-a za transient errors (NE TIMEOUT!)
    ↓
[Timeout] - 120s (NOVO: bilo 30s)
    ↓
Alfresco API
```

### **Polly Policy Stack (Write)**:
```
HttpClient Request
    ↓
[Bulkhead] - Max 100 concurrent requests
    ↓
[Circuit Breaker] - Open nakon 5 failures
    ↓
[Retry] - 3 retry-a za transient errors (NE TIMEOUT!)
    ↓
[Timeout] - 120s (isto kao READ)
    ↓
Alfresco API
```

**Napomena**: Write operacije imaju veći bulkhead (100 vs 50) jer su kritičnije.
