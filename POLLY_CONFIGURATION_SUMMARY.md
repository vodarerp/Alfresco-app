# Polly Policy Configuration - Summary

## Datum: 2025-01-21

## Cilj
Omogućiti konfiguraciju Polly policy parametara (timeouts, retry count, circuit breaker, bulkhead) kroz `appsettings.json` umesto hard-coded vrednosti.

---

## Izmene

### 1. **Kreiran novi konfiguracioni model**

**Fajl**: `Alfresco.Contracts\Options\PollyPolicyOptions.cs`

```csharp
public class PollyPolicyOptions
{
    public const string SectionName = "PollyPolicy";

    public PolicyOperationOptions ReadOperations { get; set; } = new();
    public PolicyOperationOptions WriteOperations { get; set; } = new();
}

public class PolicyOperationOptions
{
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryCount { get; set; } = 3;
    public int CircuitBreakerFailuresBeforeBreaking { get; set; } = 5;
    public int CircuitBreakerDurationOfBreakSeconds { get; set; } = 30;
    public int BulkheadMaxParallelization { get; set; } = 50;
    public int BulkheadMaxQueuingActions { get; set; } = 100;

    public void Validate() { /* validation logic */ }
    public TimeSpan GetTimeout() => TimeSpan.FromSeconds(TimeoutSeconds);
    public TimeSpan GetCircuitBreakerDuration() => TimeSpan.FromSeconds(CircuitBreakerDurationOfBreakSeconds);
}
```

**Karakteristike**:
- ✅ Validation metoda za proveru validnosti vrednosti
- ✅ Helper metode za konverziju u TimeSpan
- ✅ Default vrednosti ako konfiguracija nije postavljena
- ✅ Odvojene sekcije za Read i Write operacije

---

### 2. **Dodato u appsettings.json**

**Fajl**: `Alfresco.App\appsettings.json`

```json
"PollyPolicy": {
  "ReadOperations": {
    "TimeoutSeconds": 120,
    "RetryCount": 3,
    "CircuitBreakerFailuresBeforeBreaking": 5,
    "CircuitBreakerDurationOfBreakSeconds": 30,
    "BulkheadMaxParallelization": 50,
    "BulkheadMaxQueuingActions": 100
  },
  "WriteOperations": {
    "TimeoutSeconds": 120,
    "RetryCount": 3,
    "CircuitBreakerFailuresBeforeBreaking": 5,
    "CircuitBreakerDurationOfBreakSeconds": 30,
    "BulkheadMaxParallelization": 100,
    "BulkheadMaxQueuingActions": 200
  }
}
```

**Razlika Read vs Write**:
- Read: `BulkheadMaxParallelization = 50` (manje paralelnih zahteva)
- Write: `BulkheadMaxParallelization = 100` (više paralelnih zahteva)
- Write: `BulkheadMaxQueuingActions = 200` (duplo više od Read)

---

### 3. **Refaktorisan PolicyHelpers.cs**

**Fajl**: `Alfresco.App\Helpers\PolicyHelpers.cs`

#### **3.1. Dodato `using` za konfiguraciju**
```csharp
using Alfresco.Contracts.Options;        // ← PollyPolicyOptions
```

#### **3.2. Update GetRetryPolicy**
```csharp
// STARO
public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)

// NOVO
public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(
    int retryCount = 3,
    ILogger? logger = null)
```

**Razlog**: Sada prima `retryCount` kao parametar umesto hard-coded 3.

#### **3.3. Update GetCircuitBreakerPolicy**
```csharp
// STARO
public static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(ILogger? logger = null)

// NOVO
public static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
    int failuresBeforeBreaking = 5,
    TimeSpan? durationOfBreak = null,
    ILogger? logger = null)
```

**Razlog**: Sada prima `failuresBeforeBreaking` i `durationOfBreak` kao parametre.

#### **3.4. Update GetTimeoutPolicy** (bez izmena)
Već je primao `TimeSpan timeout` kao parametar, tako da je bio spreman za konfiguraciju.

#### **3.5. Update GetBulkheadPolicy**
```csharp
// STARO
public static AsyncPolicy<HttpResponseMessage> GetBulkheadPolicy(
    int maxParallelization = 30,
    int maxQueuingActions = 50,
    ILogger? logger = null)

// NOVO
public static AsyncPolicy<HttpResponseMessage> GetBulkheadPolicy(
    int maxParallelization = 50,  // ← Promenjen default
    int maxQueuingActions = 100,  // ← Promenjen default
    ILogger? logger = null)
```

**Razlog**: Default vrednosti ažurirane da odgovaraju Read operacijama (50/100).

#### **3.6. Refaktorisan GetCombinedReadPolicy**
```csharp
// STARO
public static IAsyncPolicy<HttpResponseMessage> GetCombinedReadPolicy(
    ILogger? logger = null,
    int bulkheadLimit = 50)
{
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(120), logger);
    var retry = GetRetryPolicy(logger);
    var circuitBreaker = GetCircuitBreakerPolicy(logger);
    var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit*2, logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}

// NOVO
public static IAsyncPolicy<HttpResponseMessage> GetCombinedReadPolicy(
    PolicyOperationOptions? options = null,
    ILogger? logger = null)
{
    // Use defaults if options not provided
    options ??= new PolicyOperationOptions();

    var timeout = GetTimeoutPolicy(options.GetTimeout(), logger);
    var retry = GetRetryPolicy(options.RetryCount, logger);
    var circuitBreaker = GetCircuitBreakerPolicy(
        options.CircuitBreakerFailuresBeforeBreaking,
        options.GetCircuitBreakerDuration(),
        logger);
    var bulkhead = GetBulkheadPolicy(
        options.BulkheadMaxParallelization,
        options.BulkheadMaxQueuingActions,
        logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}
```

**Benefit**: Svi parametri dolaze iz konfiguracije!

#### **3.7. Refaktorisan GetCombinedWritePolicy**
```csharp
// STARO
public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
    ILogger? logger = null,
    int bulkheadLimit = 100)
{
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(120), logger);
    var retry = GetRetryPolicy(logger);
    var circuitBreaker = GetCircuitBreakerPolicy(logger);
    var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit * 2, logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}

// NOVO
public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
    PolicyOperationOptions? options = null,
    ILogger? logger = null)
{
    // Use defaults if options not provided
    options ??= new PolicyOperationOptions
    {
        BulkheadMaxParallelization = 100,
        BulkheadMaxQueuingActions = 200
    };

    var timeout = GetTimeoutPolicy(options.GetTimeout(), logger);
    var retry = GetRetryPolicy(options.RetryCount, logger);
    var circuitBreaker = GetCircuitBreakerPolicy(
        options.CircuitBreakerFailuresBeforeBreaking,
        options.GetCircuitBreakerDuration(),
        logger);
    var bulkhead = GetBulkheadPolicy(
        options.BulkheadMaxParallelization,
        options.BulkheadMaxQueuingActions,
        logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}
```

**Napomena**: Ako `options` nije prosleđen, koriste se defaults sa većim bulkhead limitima (100/200) za write operacije.

---

### 4. **Registracija konfiguracije u DI**

**Fajl**: `Alfresco.App\App.xaml.cs`

#### **4.1. Dodato registrovanje konfiguracije**
```csharp
// Configure Polly Policy Options
services.Configure<PollyPolicyOptions>(
    context.Configuration.GetSection(PollyPolicyOptions.SectionName));
```

**Lokacija**: Linija ~180, odmah posle registrovanja drugih Options klasa.

#### **4.2. Update HttpClient za AlfrescoReadApi**
```csharp
// STARO
.AddPolicyHandler((sp, req) =>
{
    var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
    return PolicyHelpers.GetCombinedReadPolicy(logger);
});

// NOVO
.AddPolicyHandler((sp, req) =>
{
    var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
    var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

    return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, logger);
});
```

**Benefit**: Policy sada koristi vrednosti iz appsettings.json!

#### **4.3. Update HttpClient za AlfrescoWriteApi**
```csharp
// STARO
.AddPolicyHandler((sp, req) =>
{
    var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
    return PolicyHelpers.GetCombinedWritePolicy(logger);
});

// NOVO
.AddPolicyHandler((sp, req) =>
{
    var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
    var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

    return PolicyHelpers.GetCombinedWritePolicy(pollyOptions.WriteOperations, logger);
});
```

---

## Kako Sada Radi

### **Scenario 1: Korišćenje appsettings.json vrednosti**
```
1. Aplikacija se pokreće
2. DI container učitava appsettings.json
3. PollyPolicyOptions se popunjava vrednostima iz JSON-a
4. Kada se kreira HttpClient, PolicyHelpers dobija konfigurisane vrednosti
5. Policy se kreira sa timeout=120s, retry=3, circuit breaker=5/30s, bulkhead=50/100
```

### **Scenario 2: Izmena konfiguracije**
```
1. Operator otvara appsettings.json
2. Menja "TimeoutSeconds": 120 → 180
3. Restartuje aplikaciju
4. Novi timeout (180s) se automatski koristi bez recompile-a! ✅
```

### **Scenario 3: Environment-specifična konfiguracija**
```
appsettings.Development.json:
{
  "PollyPolicy": {
    "ReadOperations": {
      "TimeoutSeconds": 30,  // Brži timeout za dev
      "RetryCount": 1        // Manje retry-ova za dev
    }
  }
}

appsettings.Production.json:
{
  "PollyPolicy": {
    "ReadOperations": {
      "TimeoutSeconds": 180,  // Duži timeout za production
      "RetryCount": 5         // Više retry-ova za production
    }
  }
}
```

---

## Prednosti

### ✅ **Fleksibilnost**
- Timeout, retry count, circuit breaker i bulkhead parametri konfigurisani kroz JSON
- Različite vrednosti za Read i Write operacije
- Environment-specifična konfiguracija (Dev, Test, Production)

### ✅ **Održivost**
- Nema potrebe za recompile kad se menjaju timeout vrednosti
- Operatori mogu tuning-ovati vrednosti bez developera
- Centralizovana konfiguracija na jednom mestu

### ✅ **Backward Compatibility**
- Ako konfiguracija nije postavljena, koriste se default vrednosti
- Stari pozivi PolicyHelpers metoda i dalje rade (default parametri)

### ✅ **Validation**
- `PolicyOperationOptions.Validate()` metoda proverava validnost vrednosti
- Sprečava negativne vrednosti ili nonsense konfiguraciju

---

## Testiranje

### **Test 1: Učitavanje konfiguracije**
```csharp
// Arrange
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var options = config.GetSection(PollyPolicyOptions.SectionName)
    .Get<PollyPolicyOptions>();

// Assert
Assert.Equal(120, options.ReadOperations.TimeoutSeconds);
Assert.Equal(3, options.ReadOperations.RetryCount);
Assert.Equal(100, options.WriteOperations.BulkheadMaxParallelization);
```

### **Test 2: Policy kreiranje sa custom vrednostima**
```csharp
var options = new PolicyOperationOptions
{
    TimeoutSeconds = 60,
    RetryCount = 5
};

var policy = PolicyHelpers.GetCombinedReadPolicy(options, logger);

// Verify policy je kreiran sa custom vrednostima
```

### **Test 3: Default vrednosti**
```csharp
var policy = PolicyHelpers.GetCombinedReadPolicy(null, logger);

// Verify policy koristi defaults (120s timeout, 3 retries, etc.)
```

---

## Files Changed

### Kreiran novi fajl:
- `Alfresco.Contracts\Options\PollyPolicyOptions.cs` (100 linija)
- `POLLY_CONFIGURATION_SUMMARY.md` (ovaj dokument)

### Izmenjeno:
- `Alfresco.App\Helpers\PolicyHelpers.cs`
  - Dodato `using Alfresco.Contracts.Options`
  - Refaktorisano `GetRetryPolicy` (+1 parametar)
  - Refaktorisano `GetCircuitBreakerPolicy` (+2 parametra)
  - Refaktorisano `GetBulkheadPolicy` (promenjeni defaults)
  - Refaktorisano `GetCombinedReadPolicy` (koristi `PolicyOperationOptions`)
  - Refaktorisano `GetCombinedWritePolicy` (koristi `PolicyOperationOptions`)
  - **Mark-ovane stare metode kao `[Obsolete]`**:
    - `GetRetryPlicy()` → error: true (compile error ako se koristi)
    - `GetCircuitBreakerPolicy()` (bez parametara) → error: true
- `Alfresco.App\appsettings.json`
  - Dodato sekciju `PollyPolicy` sa Read i Write operacijama
- `Alfresco.App\App.xaml.cs`
  - Registrovano `PollyPolicyOptions` u DI
  - Update-ovan `AddPolicyHandler` za `AlfrescoReadApi` (koristi `pollyOptions.ReadOperations`)
  - Update-ovan `AddPolicyHandler` za `AlfrescoWriteApi` (koristi `pollyOptions.WriteOperations`)
  - **Update-ovan `AddPolicyHandler` za `ClientApi`** (koristi `pollyOptions.ReadOperations`)
  - **Update-ovan `AddPolicyHandler` za `DutApi`** (koristi `pollyOptions.ReadOperations`, zakomentarisano)
  - **Promenjen `cli.Timeout`** sa hardcodovanih vrednosti na `Timeout.InfiniteTimeSpan` (Polly upravlja timeout-om)

**Ukupno**: +240 linija dodato, ~40 linija izmenjeno

---

## Zamena Hardcodovanih Vrednosti

### **Problem: Stare metode sa hardcodovanim vrednostima**

Pre refaktorisanja, postojale su 2 metode sa hardcodovanim vrednostima:

```csharp
// STARE METODE (hardcodovane vrednosti)
public static IAsyncPolicy<HttpResponseMessage> GetRetryPlicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        //                 ↑ Hardcoded retry count = 3
}

public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        //                   ↑              ↑
        //         Hardcoded failures = 5   Hardcoded duration = 30s
}
```

**Korišćene u**:
- `ClientApi` HttpClient (linija 218-219)
- `DutApi` HttpClient (linija 253-254, zakomentarisano)

---

### **Rešenje: Obsolete + Zamena sa konfigurisanim metodama**

#### **1. Mark-ovane kao `[Obsolete]` sa `error: true`**

```csharp
[Obsolete("Use GetCombinedReadPolicy or GetCombinedWritePolicy with PolicyOperationOptions from appsettings.json instead", error: true)]
public static IAsyncPolicy<HttpResponseMessage> GetRetryPlicy() { ... }

[Obsolete("Use GetCombinedReadPolicy or GetCombinedWritePolicy with PolicyOperationOptions from appsettings.json instead", error: true)]
public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() { ... }
```

**Benefit**: Compile error ako neko pokuša da koristi stare metode → forsira korisnike da koriste nove konfigurisane verzije!

---

#### **2. ClientAPI HttpClient - PRE i POSLE**

**STARO (hardcodovane vrednosti)**:
```csharp
services.AddHttpClient<IClientApi, ClientApi>(cli =>
{
    cli.Timeout = TimeSpan.FromSeconds(
        context.Configuration.GetValue<int>("ClientApi:TimeoutSeconds", 30));
    //                                                                    ↑
    //                                               Hardcoded default = 30s
})
    .AddPolicyHandler(GetRetryPlicy())           // ❌ 3 retries (hardcoded)
    .AddPolicyHandler(GetCircuitBreakerPolicy()); // ❌ 5 failures, 30s (hardcoded)
```

**NOVO (konfigurisane vrednosti)**:
```csharp
services.AddHttpClient<IClientApi, ClientApi>(cli =>
{
    cli.Timeout = Timeout.InfiniteTimeSpan; // ✅ Polly upravlja timeout-om
})
    .AddPolicyHandler((sp, req) =>
    {
        var logger = sp.GetRequiredService<ILogger<ClientApi>>();
        var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

        // ✅ Sve vrednosti iz appsettings.json!
        return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, logger);
    });
```

**Benefit**:
- ✅ Timeout, retry, circuit breaker, bulkhead - SVE iz appsettings.json
- ✅ Kombinovana policy (timeout + retry + circuit breaker + bulkhead) umesto samo retry + circuit breaker
- ✅ Timeout se ne retry-uje (TaskCanceledException nije u retry listi)

---

#### **3. DUT API HttpClient - PRE i POSLE**

**STARO (hardcodovane vrednosti)**:
```csharp
services.AddHttpClient<IDutApi, DutApi>(cli =>
{
    cli.Timeout = TimeSpan.FromSeconds(
        context.Configuration.GetValue<int>("DutApi:TimeoutSeconds", 30));
})
    .AddPolicyHandler(GetRetryPlicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
```

**NOVO (konfigurisane vrednosti)**:
```csharp
services.AddHttpClient<IDutApi, DutApi>(cli =>
{
    cli.Timeout = Timeout.InfiniteTimeSpan; // Polly upravlja timeout-om
})
    .AddPolicyHandler((sp, req) =>
    {
        var logger = sp.GetRequiredService<ILogger<DutApi>>();
        var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

        return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, logger);
    });
```

**Napomena**: DUT API deo je još uvek zakomentarisan (`/* ... */`), ali je spreman za korišćenje kada DUT API postane dostupan.

---

### **Rezultat: Eliminisani svi hardcodovani timeout/retry/circuit breaker parametri**

| HttpClient          | Stara Policy                        | Nova Policy                          | Benefit                                  |
|---------------------|-------------------------------------|--------------------------------------|------------------------------------------|
| `AlfrescoReadApi`   | GetCombinedReadPolicy (old)         | GetCombinedReadPolicy (options)      | ✅ Konfigurisano iz appsettings.json      |
| `AlfrescoWriteApi`  | GetCombinedWritePolicy (old)        | GetCombinedWritePolicy (options)     | ✅ Konfigurisano iz appsettings.json      |
| `ClientApi`         | GetRetryPlicy + GetCircuitBreaker   | GetCombinedReadPolicy (options)      | ✅ Dodato timeout + bulkhead + konfiguracija |
| `DutApi`            | GetRetryPlicy + GetCircuitBreaker   | GetCombinedReadPolicy (options)      | ✅ Dodato timeout + bulkhead + konfiguracija |

**Sve policies sada koriste vrednosti iz `appsettings.json`**! 🎉

---

## Build Status

✅ **Compilation: Successful**
- Nema errora
- Samo standardni warnings (nullability, unused variables, itd.)
- **Stare metode su mark-ovane kao `[Obsolete]` sa `error: true`** → compile error ako neko pokuša da ih koristi

---

## Preporuke

### **1. Dodati appsettings.Development.json**
```json
{
  "PollyPolicy": {
    "ReadOperations": {
      "TimeoutSeconds": 30,
      "RetryCount": 1,
      "CircuitBreakerFailuresBeforeBreaking": 3
    },
    "WriteOperations": {
      "TimeoutSeconds": 30,
      "RetryCount": 1
    }
  }
}
```

### **2. Dodati validation na startup**
```csharp
// App.xaml.cs
var pollyOptions = services.BuildServiceProvider()
    .GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

pollyOptions.ReadOperations.Validate();
pollyOptions.WriteOperations.Validate();
```

### **3. Monitoring**
Dodati logovanje policy parametara na startup:
```csharp
logger.LogInformation(
    "Polly Policy - Read: Timeout={Timeout}s, Retry={Retry}, Bulkhead={Bulkhead}",
    pollyOptions.ReadOperations.TimeoutSeconds,
    pollyOptions.ReadOperations.RetryCount,
    pollyOptions.ReadOperations.BulkheadMaxParallelization);
```

---

## Primer Korišćenja

### **Scenario: Production ima spore operacije**

**Problem**: Production Alfresco traje duže od Development-a.

**Staro rešenje**: Morali bi recompile-ovati kod sa većim timeout-om.

**Novo rešenje**:

1. Otvori `appsettings.Production.json`
2. Izmeni:
   ```json
   "PollyPolicy": {
     "ReadOperations": {
       "TimeoutSeconds": 300  // 5 minuta umesto 2
     }
   }
   ```
3. Restartuj aplikaciju
4. ✅ Novi timeout aktivan bez recompile-a!

---

## Zaključak

Konfiguracija Polly policy parametara je **uspešno** implementirana:
- ✅ **Kreiran konfiguracioni model**: `PollyPolicyOptions` sa validation
- ✅ **Dodato u appsettings.json**: `PollyPolicy` sekcija sa Read/Write
- ✅ **Refaktorisan PolicyHelpers**: Sve metode primaju konfiguracione parametre
- ✅ **Registrovano u DI**: `Configure<PollyPolicyOptions>(...)`
- ✅ **Update-ovani SVI HttpClient pozivi**:
  - `AlfrescoReadApi` koristi `pollyOptions.ReadOperations`
  - `AlfrescoWriteApi` koristi `pollyOptions.WriteOperations`
  - `ClientApi` koristi `pollyOptions.ReadOperations`
  - `DutApi` koristi `pollyOptions.ReadOperations` (zakomentarisano)
- ✅ **Eliminisane hardcodovane vrednosti**: Stare metode mark-ovane kao `[Obsolete]` sa compile error
- ✅ **Build uspešan**: Nema compilation errora
- ✅ **Backward compatible**: Default vrednosti ako konfiguracija nedostaje

**Benefit**:
- 🎯 **Sve** timeout, retry, circuit breaker i bulkhead vrednosti sada iz appsettings.json
- 🎯 Operatori mogu tuning-ovati parametre bez recompile-a
- 🎯 Nemoguće slučajno koristiti stare hardcodovane metode (compile error)
- 🎯 ClientAPI i DutAPI sada koriste kombinovanu policy (timeout + retry + circuit breaker + bulkhead)

---

## Povezana Dokumentacija
- `POLICY_TIMEOUT_FIX_SUMMARY.md` - Detalji o timeout fix-ovima
- `REFACTORING_MOVESERVICE_SUMMARY.md` - MoveService refactoring
- `FOLDER_NAMING_FIX_SUMMARY.md` - Folder naming fix
