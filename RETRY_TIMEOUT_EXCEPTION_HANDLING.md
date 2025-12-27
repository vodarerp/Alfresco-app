# Retry i Timeout Exception Handling - Implementacija

## Pregled

Implementiran je kompletan sistem za hendlovanje timeout-a i retry exceptiona u Polly policy-ima sa custom exceptionima i detaljnim loggovanjem.

---

## Implementirane komponente

### 1. Custom Exception klase

#### `AlfrescoTimeoutException`
```csharp
// Lokacija: Alfresco.Abstraction\Models\AlfrescoTimeoutException.cs
public class AlfrescoTimeoutException : AlfrescoException
{
    public string Operation { get; }           // Ime operacije (npr. "AlfrescoRead", "AlfrescoWrite")
    public TimeSpan TimeoutDuration { get; }   // Timeout limit koji je prekoračen
    public TimeSpan ElapsedTime { get; }       // Vreme koje je prošlo
}
```

**Kada se baca:**
- Kada operacija timeout-uje nakon iscrpljivanja svih retry pokušaja
- Npr: Read operacija timeout-uje 3 puta (svaki put po 120s)

#### `AlfrescoRetryExhaustedException`
```csharp
// Lokacija: Alfresco.Abstraction\Models\AlfrescoRetryExhaustedException.cs
public class AlfrescoRetryExhaustedException : AlfrescoException
{
    public string Operation { get; }           // Ime operacije
    public int RetryCount { get; }             // Broj retry pokušaja
    public Exception? LastException { get; }   // Poslednji exception
    public int? LastStatusCode { get; }        // Poslednji HTTP status kod
}
```

**Kada se baca:**
- Kada svi retry pokušaji propadnu zbog HTTP grešaka (429, 503, 500-599)
- Kada `HttpRequestException` prođe kroz sve retry-e

---

### 2. PolicyHelpers modifikacije

#### Retry Policy - sada retry-uje timeout exceptione
```csharp
// PolicyHelpers.cs:30
.Or<TimeoutRejectedException>() // NOVO: Retry-uje timeout exceptione
```

**Exponential backoff:**
- Pokušaj 1: 2s delay + random jitter (0-500ms)
- Pokušaj 2: 4s delay + random jitter
- Pokušaj 3: 8s delay + random jitter

**Logging:**
- Loguje svaki retry pokušaj sa informacijama o tipu greške
- Razlikuje timeout od drugih exceptiona
- Loguje u FileLogger i UiLogger

#### Fallback Policy - baca custom exceptione
```csharp
// PolicyHelpers.cs:150
public static IAsyncPolicy<HttpResponseMessage> GetFallbackPolicy(...)
```

**Logika:**
1. Hvata sve exceptione koji prođu kroz retry policy
2. Proverava tip exception-a:
   - Ako je `TimeoutRejectedException` → baca `AlfrescoTimeoutException`
   - Sve ostalo → baca `AlfrescoRetryExhaustedException`
3. Loguje final failure u FileLogger i UiLogger

#### Combined Policies - redosled izvršavanja
```csharp
// Execution flow: Fallback → Timeout → Retry → CircuitBreaker → Bulkhead → HttpClient
Policy.WrapAsync(fallback, timeout, retry, circuitBreaker, bulkhead)
```

**Šta se dešava:**
1. **Bulkhead** - kontroliše broj paralelnih zahteva
2. **CircuitBreaker** - prekida ako je previše grešaka (5 uzastopno)
3. **Retry** - pokušava 3 puta sa exponential backoff-om
4. **Timeout** - svaki pokušaj ima timeout limit
5. **Fallback** - hvata finalne exceptione i baca custom exceptione

---

### 3. Timeout konfiguracija u appsettings.json

```json
"PollyPolicy": {
  "ReadOperations": {
    "TimeoutSeconds": 120,      // 2 minuta - za kompleksne query-je
    "RetryCount": 3
  },
  "WriteOperations": {
    "TimeoutSeconds": 60,       // 1 minut - za create/move/update
    "RetryCount": 3
  }
}
```

**Maksimalno vreme čekanja po operaciji:**
- **Read:** 3 × 120s + delays = ~6-7 minuta
- **Write:** 3 × 60s + delays = ~3-4 minuta

---

## Kako se koriste custom exceptioni

### U MigrationWorker-u ili drugim servisima:

```csharp
try
{
    // Poziv Alfresco API-ja koji koristi Policy
    var result = await _alfrescoWriteApi.MoveNodeAsync(...);
}
catch (AlfrescoTimeoutException timeoutEx)
{
    // Timeout nakon svih retry pokušaja
    _logger.LogError(
        "Operacija '{Operation}' je timeout-ovala nakon {RetryCount} pokušaja. " +
        "Timeout limit: {TimeoutDuration}s",
        timeoutEx.Operation,
        // RetryCount nije direktno u timeoutEx, ali može se ekstrahovati iz poruke
        timeoutEx.TimeoutDuration.TotalSeconds);

    // OVDE: Implementiraj logiku za tracking timeout grešaka
    // npr: IncrementTimeoutCounter(), CheckIfShouldStopMigration()

    // OVDE: Javi UI-u da se desio timeout
    // npr: _uiLogger.LogError("KRITIČNA GREŠKA: Timeout na migraciji dokumenta {DocId}", docId);
}
catch (AlfrescoRetryExhaustedException retryEx)
{
    // Svi retry pokušaji iscrpljeni
    _logger.LogError(
        "Operacija '{Operation}' je propala nakon {RetryCount} pokušaja. " +
        "Poslednja greška: {LastError}",
        retryEx.Operation,
        retryEx.RetryCount,
        retryEx.LastException?.Message);

    // OVDE: Implementiraj logiku za tracking retry grešaka
}
catch (AlfrescoException alfrescoEx)
{
    // Generalni Alfresco exception (parent class)
    _logger.LogError("Alfresco greška: {Message}", alfrescoEx.Message);
}
```

---

## Preporuke za implementaciju tracking-a i prekida migracije

### 1. **Kreiraj GlobalErrorTracker service**

```csharp
public class GlobalErrorTracker
{
    private int _timeoutCount = 0;
    private int _retryExhaustedCount = 0;
    private readonly int _maxTimeoutsBeforeStop;
    private readonly int _maxRetriesBeforeStop;
    private readonly ILogger<GlobalErrorTracker> _logger;

    public bool ShouldStopMigration =>
        _timeoutCount >= _maxTimeoutsBeforeStop ||
        _retryExhaustedCount >= _maxRetriesBeforeStop;

    public void RecordTimeout(AlfrescoTimeoutException ex)
    {
        Interlocked.Increment(ref _timeoutCount);
        _logger.LogWarning("Timeout #{Count}: {Operation}", _timeoutCount, ex.Operation);

        if (ShouldStopMigration)
        {
            _logger.LogCritical("MIGRACIJA ZAUSTAVLJENA: Previše timeout grešaka ({Count})", _timeoutCount);
            // Trigger UI notification
        }
    }
}
```

### 2. **Injektuj GlobalErrorTracker u MigrationWorker**

```csharp
public class MigrationWorker
{
    private readonly GlobalErrorTracker _errorTracker;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_errorTracker.ShouldStopMigration)
        {
            try
            {
                // Migration logic
            }
            catch (AlfrescoTimeoutException timeoutEx)
            {
                _errorTracker.RecordTimeout(timeoutEx);

                if (_errorTracker.ShouldStopMigration)
                {
                    _uiLogger.LogCritical("MIGRACIJA PREKINUTA: Previše timeout grešaka!");
                    break; // Prekini migraciju
                }
            }
        }
    }
}
```

### 3. **Dodaj konfiguraciju u appsettings.json**

```json
"Migration": {
  "ErrorThresholds": {
    "MaxTimeoutsBeforeStop": 10,        // Zaustavi nakon 10 timeout-a
    "MaxRetryFailuresBeforeStop": 50,   // Zaustavi nakon 50 retry failures
    "TimeWindowMinutes": 5              // U poslednjih 5 minuta
  }
}
```

### 4. **UI Feedback implementacija**

```csharp
// U catch bloku MigrationWorker-a
catch (AlfrescoTimeoutException timeoutEx)
{
    // Log to UI
    _uiLogger.LogError(
        "⏱️ TIMEOUT: Operacija '{Operation}' istekla nakon {Timeout}s. " +
        "Ukupno timeout-ova: {Count}",
        timeoutEx.Operation,
        timeoutEx.TimeoutDuration.TotalSeconds,
        _errorTracker.TimeoutCount);

    // Trigger UI notification (npr: StatusBar update)
    await _statusUpdater.UpdateAsync(new StatusUpdate
    {
        Level = StatusLevel.Critical,
        Message = $"Timeout na operaciji {timeoutEx.Operation}",
        Action = "Proveri Alfresco server performance"
    });
}
```

### 5. **Circuit Breaker notification**

Circuit breaker se već automatski otvara nakon 5 uzastopnih failova. Možeš da dodaš dodatnu logiku:

```csharp
// U PolicyHelpers.cs -> GetCircuitBreakerPolicy -> onBreak
onBreak: (outcome, duration) =>
{
    fileLogger?.LogError("Circuit breaker OPENED for {Duration}s", duration.TotalSeconds);

    // OVDE: Notifikuj UI
    uiLogger?.LogCritical(
        "🔴 KRITIČNO: Circuit breaker otvoren na {Duration}s zbog previše grešaka!",
        duration.TotalSeconds);
}
```

### 6. **Real-time monitoring dashboard**

Kreiraš UI komponentu koja prikazuje:
- Broj timeout-ova u real-time
- Broj retry failures
- Status Circuit Breaker-a (Open/Closed/Half-Open)
- Preostali timeout-ovi do zaustavljanja migracije

```csharp
public class ErrorMetrics
{
    public int TotalTimeouts { get; set; }
    public int TotalRetryFailures { get; set; }
    public int RemainingTimeoutsBeforeStop => MaxTimeouts - TotalTimeouts;
    public CircuitBreakerState CircuitBreakerState { get; set; }
    public DateTime LastError { get; set; }
}
```

---

## Testiranje implementacije

### Scenario 1: Test timeout-a
1. Smanji timeout na 5 sekundi u appsettings.json
2. Pokreni migraciju
3. Simuliraj spor server (dodaj Task.Delay u Alfresco API)
4. Verifikuj da se baca `AlfrescoTimeoutException` nakon 3 pokušaja

### Scenario 2: Test retry exhausted
1. Pokreni migraciju sa ugašenim Alfresco serverom
2. Verifikuj da se baca `AlfrescoRetryExhaustedException`
3. Proveri da logging prikazuje sve retry pokušaje

### Scenario 3: Test circuit breaker
1. Simuliraj 5 uzastopnih grešaka
2. Verifikuj da circuit breaker otvori
3. Proveri da UI prikazuje kritičnu grešku

---

## Logovanje - Primer output-a

### Timeout scenario:
```
⚠️ Retry 1/3 for operation 'AlfrescoWrite' - TIMEOUT. Waiting 2.5s before next attempt.
⚠️ Retry 2/3 for operation 'AlfrescoWrite' - TIMEOUT. Waiting 4.3s before next attempt.
⚠️ Retry 3/3 for operation 'AlfrescoWrite' - TIMEOUT. Waiting 8.1s before next attempt.
⚠️ Fallback triggered for operation 'AlfrescoWrite'
❌ FINAL FAILURE - Operation 'AlfrescoWrite' timed out after 3 attempts. Throwing AlfrescoTimeoutException.
```

### Retry exhausted scenario:
```
⚠️ Retry 1/3 for operation 'AlfrescoRead' - HttpRequestException: Connection refused. Waiting 2.2s before next attempt.
⚠️ Retry 2/3 for operation 'AlfrescoRead' - HttpRequestException: Connection refused. Waiting 4.6s before next attempt.
⚠️ Retry 3/3 for operation 'AlfrescoRead' - HttpRequestException: Connection refused. Waiting 8.4s before next attempt.
⚠️ Fallback triggered for operation 'AlfrescoRead'
❌ FINAL FAILURE - Operation 'AlfrescoRead' failed after 3 attempts. Last error: Connection refused. Throwing AlfrescoRetryExhaustedException.
```

---

## Zaključak

Implementiran je kompletan sistem za hendlovanje timeout-a i retry exceptiona:

✅ Custom exception klase (`AlfrescoTimeoutException`, `AlfrescoRetryExhaustedException`)
✅ Retry policy retry-uje timeout exceptione
✅ Fallback policy baca custom exceptione
✅ Različiti timeout-ovi za Read (120s) i Write (60s)
✅ Detaljan logging na svaki retry pokušaj
✅ UI logger integracija

**Sledeći korak:** Implementirati GlobalErrorTracker i UI feedback prema preporukama gore.
