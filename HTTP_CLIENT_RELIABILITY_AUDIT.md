# HTTP Client & Reliability Audit — Alfresco Migration App

**Datum analize:** 2026-04-08  
**Analizirao:** Claude Sonnet 4.6  
**Scope:** HttpClient lifecycle, Polly policies, paralelizam, Alfresco API patterns, auto-recovery  
**Kontekst:** ~1.5M dokumenata za migraciju, stabilnost > brzina, Alfresco server nepoznate konfiguracije

---

## Sadržaj

1. [Kontekst i ciljevi analize](#1-kontekst-i-ciljevi-analize)
2. [Arhitektura relevantnih komponenti](#2-arhitektura-relevantnih-komponenti)
3. [Executive Summary](#3-executive-summary)
4. [Critical Issues](#4-critical-issues)
5. [High Priority Issues](#5-high-priority-issues)
6. [Medium Priority Issues](#6-medium-priority-issues)
7. [Auto-Recovery za Fazu 1 (PreviewLoadService)](#7-auto-recovery-za-fazu-1-previewloadservice)
8. [Konkretan refaktor — finalna HttpClient registracija](#8-konkretan-refaktor--finalna-httpclient-registracija)
9. [Preporučene konfiguracije za batch scenario](#9-preporuene-konfiguracije-za-batch-scenario)
10. [Observability — metrike i alarmi](#10-observability--metrike-i-alarmi)
11. [Preporučeni redosled implementacije](#11-preporueni-redosled-implementacije)
12. [Kompatibilnost između fiks-ova](#12-kompatibilnost-izmeu-fiks-ova)

---

## 1. Kontekst i ciljevi analize

### Šta se radi
Migracija ~1.5M dokumenata iz legacy sistema u Alfresco DMS. WPF desktop aplikacija komunicira sa Alfresco REST API-jem i ClientAPI-jem. Migracija se odvija kroz faze:

- **PreviewTypeMigration (preview):** Faza 1 (PreviewLoadService) → Faza 2 (FolderPreparation) → Faza 3 (FolderCreation) → Transfer → Move
- **StandardMigration:** FolderDiscovery → DocumentDiscovery → FolderPreparation → Move

### Cilj analize
Pronaći sve konfiguracione i implementacione greške koje mogu dovesti do:
- Preopterećenja Alfresco servera
- Iscrpljivanja resursa na klijentu (socket exhaustion, memory pressure)
- Kaskadnih otkaza pod opterećenjem
- Tihe gubitke podataka (silent failures)

### Odgovori na ključna pitanja okruženja
- **Throughput:** Nema tvrdog limita — bitna je stabilnost, ne brzina
- **Alfresco server:** Nepoznata konfiguracija i mašina
- **`BasicAuthHandler`:** Postavlja `request.Headers.Authorization` per-request — `DefaultRequestHeaders.Authorization` u `ConfigureHttpClient` je mrtav kod koji overwrite-uje handler

---

## 2. Arhitektura relevantnih komponenti

```
App.xaml.cs (DI root)
│
├── HttpClient registracije:
│   ├── AlfrescoReadApi   → typed client, Polly policy (PROBLEM: per-request)
│   ├── AlfrescoWriteApi  → typed client, Polly policy (PROBLEM: per-request)
│   ├── ClientApi         → typed client, Polly policy (PROBLEM: per-request)
│   └── AlfrescoCurrentUserClient → named client, bez Polly, 30s timeout
│
├── Polly policies → PolicyHelpers.cs
│   └── WrapAsync(fallback, retry, timeout, circuitBreaker, bulkhead)
│
├── Singleton servisi koji DRŽE transient HttpClient (captive dependency):
│   ├── PreviewLoadService
│   ├── DocumentSearchService
│   ├── FolderDiscoveryService
│   ├── DocumentDiscoveryService
│   ├── KdpDocumentProcessingService
│   ├── MoveService
│   └── ostali...
│
└── PreviewMigrationUC.xaml.cs → UI trigger za Faze 1-N
```

### Polly policy chain (trenutni redosled)
```
Execution: Fallback → Retry → Timeout → CircuitBreaker → Bulkhead → HttpClient
                                        ↑ SVAKI put nova instanca ← KRITIČAN BUG
```

### Checkpoint sistem (već implementiran, radi ispravno)
- `PreviewLoadService`: `LoadCheckpointAsync` / `SaveCheckpointAsync` / `FlushPendingBatchesAsync`
- Checkpoint se čuva u SQL tabeli `PreviewLoadCheckpoints` po folder tipu (PI/LE)
- `InsertManyMergeAsync` koristi MERGE INSERT — duplikati su bezbedni

---

## 3. Executive Summary

| # | Problem | Fajl:Linija | Sev. |
|---|---------|------------|------|
| C-1 | Circuit Breaker i Bulkhead kreiraju se **per-request** — nikad ne trip-uju | `App.xaml.cs:130,169,245` | CRITICAL |
| C-2 | **Captive Dependency** — Singleton servisi drže transient `HttpClient` zauvek | `App.xaml.cs:287–298` | CRITICAL |
| C-3 | `FolderExistsAsync` / `GetFolderByNameAsync` bez `maxItems` — false negatives | `AlfrescoReadApi.cs:384,429` | CRITICAL |
| H-1 | Pogrešan redosled policy-ja (Bulkhead innermost umesto outermost) | `PolicyHelpers.cs:447` | HIGH |
| H-2 | Retry na `BrokenCircuitException` i `BulkheadRejectedException` | `PolicyHelpers.cs:36–37` | HIGH |
| H-3 | Dupla Basic Auth — `DefaultRequestHeaders` + `BasicAuthHandler` | `App.xaml.cs:118,128` | HIGH |
| H-4 | `ReadAsStringAsync` svuda — ceo response u RAM pre parsiranja | `AlfrescoReadApi.cs` svuda | HIGH |
| H-5 | Full response body loguje se na INFO nivou za svaki HTTP poziv | `AlfrescoReadApi.cs:217,249,...` | HIGH |
| M-1 | `SetHandlerLifetime == PooledConnectionLifetime` (10 min) — connection storm | `App.xaml.cs:123,129` | MEDIUM |
| M-2 | `AlfrescoCurrentUserClient` — nema `MaxConnectionsPerServer` i Polly | `App.xaml.cs:186–197` | MEDIUM |
| M-3 | `TimeoutSeconds = 240` — previsok timeout za 3 retry = 720s najgori slučaj | `appsettings.json` | MEDIUM |
| M-4 | Health Checks zakomentarisani | `App.xaml.cs:343–354` | MEDIUM |

---

## 4. Critical Issues

### C-1. Circuit Breaker i Bulkhead su per-request instance

**Fajl:** `Alfresco.App/App.xaml.cs:130–141` (i identično `:169–180`, `:245–254`)

```csharp
// PROBLEM: lambda se poziva za SVAKI HTTP request
.AddPolicyHandler((sp, req) =>
{
    // GetCombinedReadPolicy() kreira NOVU instancu CircuitBreakerPolicy i BulkheadPolicy
    // Circuit state (broj neuspeha) živi u instanci objekta
    // Nova instanca = nulto stanje = circuit se nikad ne otvori
    return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, ...);
});
```

**Posledice:**
- Circuit Breaker: nikad se ne otvori. Alfresco može da pada satima, aplikacija nastavlja da ga bombarduje
- Bulkhead: svaki request ima sopstveni bulkhead → efektivno nema throttling-a
- Retry na `BulkheadRejectedException` je besmislen jer shared bulkhead ne postoji

**Fix — `PolicyRegistry` singleton:**

```csharp
// App.xaml.cs — dodati pre AddHttpClient blokova
services.AddPolicyRegistry((sp, registry) =>
{
    var pollyOptions  = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var fileLogger    = loggerFactory.CreateLogger("FileLogger");
    var dbLogger      = loggerFactory.CreateLogger("DbLogger");
    var uiLogger      = loggerFactory.CreateLogger("UiLogger");

    registry.Add("AlfrescoRead",
        PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, fileLogger, dbLogger, uiLogger));
    registry.Add("AlfrescoWrite",
        PolicyHelpers.GetCombinedWritePolicy(pollyOptions.WriteOperations, fileLogger, dbLogger, uiLogger));
    registry.Add("ClientApi",
        PolicyHelpers.GetCombinedClientApiPolicy(pollyOptions.ReadOperations, fileLogger, dbLogger, uiLogger));
});

// Zatim u AddHttpClient umesto .AddPolicyHandler((sp, req) => ...):
services.AddHttpClient<IAlfrescoReadApi, AlfrescoReadApi>(...)
    ...
    .AddPolicyHandlerFromRegistry("AlfrescoRead");  // ← ista instanca za sve requestove

services.AddHttpClient<IAlfrescoWriteApi, AlfrescoWriteApi>(...)
    ...
    .AddPolicyHandlerFromRegistry("AlfrescoWrite");

services.AddHttpClient<IClientApi, ClientApi>(...)
    ...
    .AddPolicyHandlerFromRegistry("ClientApi");
```

---

### C-2. Captive Dependency — Singleton servisi drže transient HttpClient zauvek

**Fajl:** `Alfresco.App/App.xaml.cs:287–298`

```csharp
// Typed klijenti su TRANSIENT po defaultu
services.AddHttpClient<IAlfrescoReadApi, AlfrescoReadApi>(...); // → transient
services.AddHttpClient<IAlfrescoWriteApi, AlfrescoWriteApi>(...); // → transient

// Ovi Singleton-i injektuju transient typed klijente u konstruktor → captive
services.AddSingleton<IPreviewLoadService, PreviewLoadService>();       // ← CAPTIVE
services.AddSingleton<IDocumentSearchService, DocumentSearchService>(); // ← CAPTIVE
services.AddSingleton<IFolderDiscoveryService, FolderDiscoveryService>(); // ← CAPTIVE
// ... i ostali
```

**Posledice:**
- `SetHandlerLifetime(10 min)` nikad ne rotira handler u Singleton-ima
- Ako se Alfresco IP promeni (DNS refresh), app nastavlja sa starim IP tokom cele sesije
- Za migraciju koja traje satima — ovo je realan problem

**Fix — servisi koriste `IServiceScopeFactory` za HttpClient:**

```csharp
// Registrovati kao named client umesto typed:
services.AddHttpClient("AlfrescoReadApi", (sp, cli) => { /* konfiguracija */ })
    .ConfigurePrimaryHttpMessageHandler(...)
    .AddPolicyHandlerFromRegistry("AlfrescoRead");

// Transient factory za IAlfrescoReadApi:
services.AddTransient<IAlfrescoReadApi>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var client  = factory.CreateClient("AlfrescoReadApi");
    return new AlfrescoReadApi(client,
        sp.GetRequiredService<IOptions<AlfrescoOptions>>(),
        sp.GetRequiredService<ILoggerFactory>());
});

// Singleton servisi → koriste IServiceScopeFactory da dobiju IAlfrescoReadApi per-operacija:
// (PreviewLoadService već ima _scopeFactory za DB — isti pattern za HTTP)
```

---

### C-3. `FolderExistsAsync` / `GetFolderByNameAsync` — bez `maxItems`

**Fajl:** `Alfresco.Client/Implementation/AlfrescoReadApi.cs:384` i `:429`

```csharp
// PROBLEM: nema &maxItems — Alfresco default = 100
var url = $"...nodes/{parentFolderId}/children?where=(isFolder=true)";
// Folder sa 150+ child-foldera → samo prvih 100 vraća → false negative
```

**Ovo je correctness bug** — tiho vraća pogrešan rezultat.

**Fix — koristiti AFTS search (kao `GetFolderByRelative`):**

```csharp
public async Task<bool> FolderExistsAsync(string parentFolderId, string folderName, CancellationToken ct = default)
{
    var escaped = folderName.Replace("\"", "\\\"");
    var request = new PostSearchRequest
    {
        Query = new QueryRequest
        {
            Language = "afts",
            Query = $"TYPE:\"cm:folder\" AND PARENT:\"{parentFolderId}\" AND =cm:name:\"{escaped}\""
        },
        Paging = new PagingRequest { MaxItems = 1, SkipCount = 0 }
    };
    var result = await SearchAsync(request, ct).ConfigureAwait(false);
    return result?.List?.Entries?.Any() == true;
}

public async Task<NodeResponse?> GetFolderByNameAsync(string parentFolderId, string folderName, CancellationToken ct = default)
{
    var escaped = folderName.Replace("\"", "\\\"");
    var request = new PostSearchRequest
    {
        Query = new QueryRequest
        {
            Language = "afts",
            Query = $"TYPE:\"cm:folder\" AND PARENT:\"{parentFolderId}\" AND =cm:name:\"{escaped}\""
        },
        Paging  = new PagingRequest { MaxItems = 1, SkipCount = 0 },
        Include = new List<string> { "properties" }
    };
    var result = await SearchAsync(request, ct).ConfigureAwait(false);
    var entry = result?.List?.Entries?.FirstOrDefault()?.Entry;
    return entry == null ? null : new NodeResponse { Entry = entry };
}
```

---

## 5. High Priority Issues

### H-1. Pogrešan redosled Polly policy-ja

**Fajl:** `Alfresco.App/Helpers/PolicyHelpers.cs:447`

```csharp
// Trenutno — Bulkhead innermost (closest to HTTP):
return Policy.WrapAsync(fallback, retry, timeout, circuitBreaker, bulkhead);
// Flow: fallback → retry → [svaki pokušaj: timeout → CB → bulkhead → HTTP]

// Ispravan redosled — Bulkhead outermost da ograniči ukupan paralelizam:
return Policy.WrapAsync(fallback, bulkhead, circuitBreaker, retry, timeout);
// Flow: fallback → bulkhead (limit total) → CB (health check) → retry → timeout → HTTP
```

---

### H-2. Retry na `BrokenCircuitException` i `BulkheadRejectedException`

**Fajl:** `Alfresco.App/Helpers/PolicyHelpers.cs:36–37`

```csharp
// UKLONITI ove dve linije iz GetRetryPolicy():
.Or<BrokenCircuitException>()    // CB je otvoren iz razloga — retry ne može da ga zatvori
.Or<BulkheadRejectedException>() // Backpressure signal — ne treba retry, treba čekanje
```

**Zašto `BrokenCircuitException` ne treba retry:**  
CB ulazi u HALF-OPEN posle `durationOfBreak` sekundi — sam se "oporavlja". Retry delays su 2s+4s+8s=14s total. Ako je break duration 30s+, sva 3 retry propadaju bez efekta i troše retry budget.

**Napomena:** `BrokenCircuitException` treba da bude mapiran u Fallback policy-ju na smislenu grešku:
```csharp
if (outcome.Exception is BrokenCircuitException)
    throw new AlfrescoRetryExhaustedException(operation, retryCount: 0, lastException: outcome.Exception, ...);
```

---

### H-3. Dupla Basic Auth — mrtav kod u `ConfigureHttpClient`

**Fajl:** `Alfresco.App/App.xaml.cs:118` + `App.xaml.cs:128`

```csharp
// U ConfigureHttpClient:
cli.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Basic", credentials);  // ← MRTAV KOD

// BasicAuthHandler.SendAsync (linija 38 u BasicAuthHandler.cs):
request.Headers.Authorization = new AuthenticationHeaderValue("Basic", ...);
// ← ovo PREPISUJE DefaultRequestHeaders na svakom requestu
```

`BasicAuthHandler` uvek poslednji setuje header. `DefaultRequestHeaders.Authorization` linija nikad ne stiže do servera.

**Fix:** Ukloniti `DefaultRequestHeaders.Authorization` iz `ConfigureHttpClient` za oba klijenta.

---

### H-4. `ReadAsStringAsync` svuda — double-buffering u RAM-u

**Fajl:** `Alfresco.Client/Implementation/AlfrescoReadApi.cs` (svuda)

```csharp
// PROBLEM: Ceo JSON body u string, pa deserijalizacija = 2x memorija
var body   = await getResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
var result = JsonConvert.DeserializeObject<NodeChildrenResponse>(body);

// FIX: Streaming deserijalizacija
using var stream = await getResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
var result = await JsonSerializer.DeserializeAsync<NodeChildrenResponse>(stream,
    JsonSerializerOptions.Default, ct).ConfigureAwait(false);
```

Za download binarnih sadržaja (fajlovi iz Alfresca):
```csharp
using var response = await _client.GetAsync(url,
    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
// Streamed copy direktno na disk/storage
```

---

### H-5. Full response body na INFO nivou — disk I/O pressure

**Fajl:** `AlfrescoReadApi.cs:217`, `:249`, `:271`, `:312`, `:363`

```csharp
// PROBLEM: Svaki search response (može biti 50–200KB JSON) logovati na INFO
_fileLogger.LogInformation("RESPONSE -> Status: {StatusCode}, Body: {ResponseBody}",
    (int)getResponse.StatusCode, body);

// FIX: Samo status + elapsed na INFO; body samo na ERROR
_fileLogger.LogDebug("SearchAsync: Status: {StatusCode}, ElapsedMs: {Elapsed}",
    (int)postResponse.StatusCode, elapsed.TotalMilliseconds);

if (!postResponse.IsSuccessStatusCode)
{
    var preview = stringResponse.Length > 500
        ? stringResponse[..500] + "..." : stringResponse;
    _fileLogger.LogWarning("SearchAsync FAILED: Status: {StatusCode}, Body: {Preview}",
        (int)postResponse.StatusCode, preview);
}
```

---

## 6. Medium Priority Issues

### M-1. `SetHandlerLifetime == PooledConnectionLifetime` — connection storm

**Fajl:** `App.xaml.cs:123,129`

Kad `SetHandlerLifetime` istekne, `IHttpClientFactory` reciklira handler. Ako je isti kao `PooledConnectionLifetime`, svi pooled connections su zamenjeni u istom trenutku → burst novih TCP konekcija.

```csharp
// FIX: SetHandlerLifetime = 2× PooledConnectionLifetime
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(10),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer     = 20,  // konzervativan za nepoznat Alfresco server
    EnableMultipleHttp2Connections = true
})
.SetHandlerLifetime(TimeSpan.FromMinutes(20)) // ← 2× PooledConnectionLifetime
```

---

### M-2. `AlfrescoCurrentUserClient` — nema `MaxConnectionsPerServer`

**Fajl:** `App.xaml.cs:186–197`

```csharp
// FIX: Dodati SocketsHttpHandler konfiguraciju
services.AddHttpClient("AlfrescoCurrentUserClient", (sp, cli) => { ... })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer  = 2  // singleton service
    })
    .AddHttpMessageHandler<BasicAuthHandler>();
```

---

### M-3. Timeout od 240s sa 3 retry = 720s najgori slučaj

Preporučene vrednosti za batch scenario (~1.5M dok., stabilnost > brzina):

```json
"PollyPolicy": {
  "ReadOperations": {
    "TimeoutSeconds": 60,
    "RetryCount": 3,
    "CircuitBreakerFailuresBeforeBreaking": 5,
    "CircuitBreakerDurationOfBreakSeconds": 60,
    "BulkheadMaxParallelization": 8,
    "BulkheadMaxQueuingActions": 16
  },
  "WriteOperations": {
    "TimeoutSeconds": 120,
    "RetryCount": 2,
    "CircuitBreakerFailuresBeforeBreaking": 5,
    "CircuitBreakerDurationOfBreakSeconds": 60,
    "BulkheadMaxParallelization": 5,
    "BulkheadMaxQueuingActions": 10
  }
}
```

**Obrazloženje:**
- `BulkheadMaxParallelization = 8` za Read: 8 paralelnih Solr query-ja je realno za svaki Alfresco server
- `TimeoutSeconds = 60`: ako Alfresco ne odgovori za 60s, problem je ozbiljan — čekanje 4 min samo blokira resurse
- `RetryCount = 3` + `CircuitBreakerFailuresBeforeBreaking = 5`: posle 5 uzastopnih neuspeha CB otvara i čeka 60s

---

### M-4. Health Checks zakomentarisani

**Fajl:** `App.xaml.cs:343–354`

```csharp
// Odkomentarisati i ažurirati:
services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "SqlServer-db",
        failureStatus: HealthStatus.Unhealthy, tags: new[] { "db" })
    .AddUrlGroup(new Uri(alfrescoBaseUrl + "/alfresco/api/discovery"),
        name: "alfresco-api",
        failureStatus: HealthStatus.Unhealthy, tags: new[] { "api" });
```

---

## 7. Auto-Recovery za Fazu 1 (PreviewLoadService)

### Kontekst

Kad Alfresco padne tokom Faze 1 i Polly iscrpi sve retry pokušaje, baca se `AlfrescoRetryExhaustedException` ili `AlfrescoTimeoutException`. Trenutno se ta greška propagira do `BtnStartFaza1_Click` koji prikazuje MessageBox i resetuje dugmad — **manuelni restart je jedina opcija**.

Cilj: kad veza sa Alfresco-om padne i retry-ji se iscrpe, aplikacija sama:
1. Sačeka dok Alfresco opet ne postane dostupan (polling)
2. Nastavi Fazu 1 tačno od mesta gde je stala (checkpoint)

**Zašto checkpoint osigurava korektnost:** `PreviewLoadService.RunLoopAsync` poziva `LoadCheckpointAsync()` na svakom startu. `FlushPendingBatchesAsync` upisuje checkpoint u DB posle svakog batch-a. `InsertManyMergeAsync` koristi MERGE INSERT (duplikati su bezbedni). Ponovni poziv `RunLoopAsync` automatski nastavlja od zadnje sačuvane pozicije.

---

### Implementacija — novi fajlovi

#### `Alfresco.Abstraction/Interfaces/IAlfrescoHealthChecker.cs` (novi)

```csharp
namespace Alfresco.Abstraction.Interfaces
{
    public interface IAlfrescoHealthChecker
    {
        /// <summary>
        /// Direktan ping Alfresca bez Polly policy-ja.
        /// Mora biti bez CB da bi radio i kad je circuit otvoren.
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken ct = default);

        /// <summary>
        /// Blokira dok Alfresco ne postane dostupan ili dok ct ne bude cancelovan.
        /// Baca InvalidOperationException ako se iscrpe maxAttempts.
        /// onAttempt(pokušaj, maxPokušaja) — callback za UI update.
        /// </summary>
        Task WaitUntilAvailableAsync(
            TimeSpan pollInterval,
            int maxAttempts,
            Action<int, int>? onAttempt,
            CancellationToken ct);
    }
}
```

#### `Alfresco.Client/Implementation/AlfrescoHealthChecker.cs` (novi)

```csharp
using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Alfresco.Client.Implementation
{
    /// <summary>
    /// Direktna provera dostupnosti Alfresco servera.
    /// Koristi "AlfrescoCurrentUserClient" — bez Polly policy-ja.
    /// Ovo je ključno: ne prolazi kroz Circuit Breaker koji može biti otvoren.
    /// </summary>
    public class AlfrescoHealthChecker : IAlfrescoHealthChecker
    {
        private const string HealthClientName = "AlfrescoCurrentUserClient";
        private const string HealthEndpoint   = "/alfresco/api/discovery";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;

        public AlfrescoHealthChecker(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory;
            _logger = loggerFactory.CreateLogger("FileLogger");
        }

        public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            try
            {
                using var client   = _httpClientFactory.CreateClient(HealthClientName);
                using var response = await client.GetAsync(HealthEndpoint, ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug("Alfresco health check neuspešan: {Error}", ex.Message);
                return false;
            }
        }

        public async Task WaitUntilAvailableAsync(
            TimeSpan pollInterval,
            int maxAttempts,
            Action<int, int>? onAttempt,
            CancellationToken ct)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogInformation("Alfresco health check pokušaj {Attempt}/{Max}...", attempt, maxAttempts);

                if (await IsAvailableAsync(ct).ConfigureAwait(false))
                {
                    _logger.LogInformation("Alfresco dostupan — veza obnovljena (pokušaj {Attempt})", attempt);
                    return;
                }

                onAttempt?.Invoke(attempt, maxAttempts);
                _logger.LogWarning("Alfresco nedostupan ({Attempt}/{Max}). Sledeća provera za {Interval}s.",
                    attempt, maxAttempts, pollInterval.TotalSeconds);

                if (attempt < maxAttempts)
                    await Task.Delay(pollInterval, ct).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                $"Alfresco nije dostupan ni posle {maxAttempts} pokušaja. Prekidanje oporavka.");
        }
    }
}
```

---

### Implementacija — DI registracija

**`Alfresco.App/App.xaml.cs`** — dodati posle `AddHttpClient` blokova:

```csharp
services.AddSingleton<IAlfrescoHealthChecker, AlfrescoHealthChecker>();
```

---

### Implementacija — modifikacija UI

**`Alfresco.App/UserControls/PreviewMigrationUC.xaml.cs`**

Novi field-ovi na vrhu klase:
```csharp
private readonly IAlfrescoHealthChecker _healthChecker;
private readonly IOptions<PollyPolicyOptions> _pollyOptions;
```

U konstruktoru (uz ostale `GetRequiredService` pozive):
```csharp
_healthChecker = App.AppHost.Services.GetRequiredService<IAlfrescoHealthChecker>();
_pollyOptions  = App.AppHost.Services.GetRequiredService<IOptions<PollyPolicyOptions>>();
```

Zameniti ceo `BtnStartFaza1_Click`:
```csharp
private async void BtnStartFaza1_Click(object sender, RoutedEventArgs e)
{
    const int maxRecoveryAttempts = 10;
    var pollInterval = TimeSpan.FromSeconds(30);

    try
    {
        SetButtonsRunning(true);
        ProgressBar.Value = 0;
        _cts = new CancellationTokenSource();

        var folderFilter = CmbFaza1FolderFilter.SelectedItem is ComboBoxItem fi &&
                           !string.IsNullOrEmpty(fi.Tag?.ToString())
            ? fi.Tag.ToString() : null;

        void OnProgress(WorkerProgress p)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus(p.Message ?? "U toku...");
                AppendLog($"Ucitano: {p.ProcessedItems}  |  Greske: {p.FailedCount}  |  {p.Message}");
                if (p.TotalItems > 0)
                    ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
            });
        }

        int recoveryAttempt = 0;

        while (true)
        {
            _cts.Token.ThrowIfCancellationRequested();

            AppendLog(recoveryAttempt == 0
                ? $"=== Pokretanje Faze 1 (filter: {folderFilter ?? "Sve"}) ==="
                : $"=== Nastavak Faze 1 od checkpointa (oporavak {recoveryAttempt}/{maxRecoveryAttempts}) ===");

            try
            {
                // RunLoopAsync pri svakom pozivu učitava checkpoint i nastavlja odatle
                var result = await Task.Run(
                    () => _previewLoadService.RunLoopAsync(_cts.Token, OnProgress, folderFilter),
                    _cts.Token);

                ProgressBar.Value = 100;
                var msg = result
                    ? "Faza 1 zavrsena uspesno."
                    : "Faza 1 zavrsena sa upozorenjem (nema konfiguriranih foldera?).";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");
                await RefreshStatisticsAsync();
                await LoadDataAsync();
                break; // jedini normalni izlaz iz petlje
            }
            catch (OperationCanceledException)
            {
                throw; // korisnik je kliknuo Stop — ne raditi recovery
            }
            catch (AlfrescoTimeoutException ex)
            {
                if (++recoveryAttempt > maxRecoveryAttempts)
                {
                    AppendLog($"GRESKA: Dostignut maksimalan broj oporavaka ({maxRecoveryAttempts}).");
                    throw;
                }
                AppendLog($"UPOZORENJE: Alfresco timeout ({ex.TimeoutDuration.TotalSeconds}s). " +
                          $"Čekam obnovu veze... (oporavak {recoveryAttempt}/{maxRecoveryAttempts})");
                UpdateStatus("Veza sa Alfresco-om prekinuta (timeout). Čekam obnovu...");
                await WaitForAlfrescoRecoveryAsync(pollInterval, maxRecoveryAttempts, recoveryAttempt);
            }
            catch (AlfrescoRetryExhaustedException ex)
            {
                if (++recoveryAttempt > maxRecoveryAttempts)
                {
                    AppendLog($"GRESKA: Dostignut maksimalan broj oporavaka ({maxRecoveryAttempts}).");
                    throw;
                }
                AppendLog($"UPOZORENJE: Alfresco retry iscrpljen ({ex.RetryCount} pokušaja). " +
                          $"Čekam obnovu veze... (oporavak {recoveryAttempt}/{maxRecoveryAttempts})");
                UpdateStatus("Veza prekinuta (retry exhausted). Čekam obnovu...");
                await WaitForAlfrescoRecoveryAsync(pollInterval, maxRecoveryAttempts, recoveryAttempt);
            }
            // Sve ostale greške (SQL, null ref, itd.) — NE recovery, propagirati
        }
    }
    catch (OperationCanceledException)
    {
        UpdateStatus("Zaustavljeno.");
        AppendLog("=== Ucitavanje zaustavljeno od strane korisnika. ===");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("nije dostupan ni posle"))
    {
        UpdateStatus($"GRESKA: {ex.Message}");
        AppendLog($"GRESKA: {ex.Message}");
        MessageBox.Show($"Alfresco nije obnovio vezu posle dužeg čekanja.\n{ex.Message}",
            "Greška konekcije", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    catch (Exception ex)
    {
        UpdateStatus($"GRESKA: {ex.Message}");
        AppendLog($"GRESKA: {ex.Message}");
        MessageBox.Show($"Greska pri ucitavanju:\n{ex.Message}", "Greska",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
        SetButtonsRunning(false);
        _cts?.Dispose();
        _cts = null;
    }
}

private async Task WaitForAlfrescoRecoveryAsync(
    TimeSpan pollInterval,
    int maxRecoveryAttempts,
    int currentRecoveryAttempt)
{
    var remainingAttempts = maxRecoveryAttempts - currentRecoveryAttempt + 1;
    var maxPingAttempts   = Math.Max(remainingAttempts * 2, 10);

    await _healthChecker.WaitUntilAvailableAsync(
        pollInterval,
        maxPingAttempts,
        onAttempt: (attempt, max) =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus($"Alfresco nedostupan. Provera {attempt}/{max}. Sledeća za {pollInterval.TotalSeconds}s...");
                AppendLog($"Ping {attempt}/{max}: Alfresco još uvek nedostupan...");
            });
        },
        ct: _cts!.Token);

    Dispatcher.Invoke(() =>
    {
        UpdateStatus("Veza obnovljena — čekam stabilizaciju...");
        AppendLog("=== Veza obnovljena — čekam stabilizaciju CB-a ===");
    });

    // KLJUČNO: Čekati CB break duration + buffer.
    // Pre C-1 fixa (CB ne postoji): delay je "slobodan" — 5s nije problem
    // Posle C-1 fixa (CB singleton): ako CB ima break duration 60s,
    // ovaj delay sprečava lažni restart dok je CB još OPEN.
    // Vrednost se čita iz konfiguracije tako da automatski prati promene.
    var cbBreakSeconds     = _pollyOptions.Value.ReadOperations.CircuitBreakerDurationOfBreakSeconds;
    var stabilizationDelay = TimeSpan.FromSeconds(cbBreakSeconds + 5);

    Dispatcher.Invoke(() =>
        AppendLog($"Čekam {stabilizationDelay.TotalSeconds}s da se circuit breaker stabilizuje..."));

    await Task.Delay(stabilizationDelay, _cts!.Token);
}
```

---

### Dijagram toka auto-recovery

```
BtnStartFaza1_Click
│
├─ recoveryAttempt = 0
└─ while(true)
   │
   ├─ RunLoopAsync(ct, progressCallback, filter)
   │   ├── LoadCheckpointAsync()     ← uvek nastavlja od zadnje pozicije
   │   ├── ParralelProccesDocumentsAsync()
   │   │    └── SearchDocumentsAsync()
   │   │         └── [ALFRESCO PADA] → AlfrescoTimeoutException / AlfrescoRetryExhaustedException
   │   │
   │   └── [SUCCESS] → break
   │
   ├─ catch AlfrescoTimeoutException / AlfrescoRetryExhaustedException
   │    ├── recoveryAttempt > maxAttempts? → throw (daj korisniku MessageBox)
   │    └── WaitForAlfrescoRecoveryAsync()
   │         ├── loop: IAlfrescoHealthChecker.IsAvailableAsync()
   │         │    ├── [false] → log, wait 30s, repeat
   │         │    └── [true]  → potvrđena konekcija
   │         └── delay(CB_break_duration + 5s)   ← sprečava lažni restart
   │
   └─ [next iteration] RunLoopAsync() nastavlja od checkpointa
```

---

## 8. Konkretan refaktor — finalna HttpClient registracija

```csharp
// App.xaml.cs — ConfigureServices

// 1. Policy Registry — singleton instance za sve requestove
services.AddPolicyRegistry((sp, registry) =>
{
    var pollyOptions  = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;
    var lf            = sp.GetRequiredService<ILoggerFactory>();

    registry.Add("AlfrescoRead",
        PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations,
            lf.CreateLogger("FileLogger"), lf.CreateLogger("DbLogger"), lf.CreateLogger("UiLogger")));
    registry.Add("AlfrescoWrite",
        PolicyHelpers.GetCombinedWritePolicy(pollyOptions.WriteOperations,
            lf.CreateLogger("FileLogger"), lf.CreateLogger("DbLogger"), lf.CreateLogger("UiLogger")));
    registry.Add("ClientApi",
        PolicyHelpers.GetCombinedClientApiPolicy(pollyOptions.ReadOperations,
            lf.CreateLogger("FileLogger"), lf.CreateLogger("DbLogger"), lf.CreateLogger("UiLogger")));
});

// 2. Named klijenti (ne typed — da bi Singleton servisi mogli da koriste IHttpClientFactory)
services.AddHttpClient("AlfrescoReadApi", (sp, cli) =>
{
    cli.Timeout = Timeout.InfiniteTimeSpan;
    var opts  = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
    var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.Username}:{opts.Password}"));
    cli.BaseAddress = new Uri(opts.BaseUrl);
    cli.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    cli.DefaultRequestHeaders.ConnectionClose = false;
    // NE dodavati DefaultRequestHeaders.Authorization — BasicAuthHandler to radi
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime    = TimeSpan.FromMinutes(10),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer     = 20,
    EnableMultipleHttp2Connections = true
})
.SetHandlerLifetime(TimeSpan.FromMinutes(20))  // 2× PooledConnectionLifetime
.AddHttpMessageHandler<BasicAuthHandler>()
.AddPolicyHandlerFromRegistry("AlfrescoRead");

// (AlfrescoWriteApi analogno, MaxConnectionsPerServer = 15)

// 3. Transient factory za interface (Singleton servisi koriste IServiceScopeFactory)
services.AddTransient<IAlfrescoReadApi>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new AlfrescoReadApi(
        factory.CreateClient("AlfrescoReadApi"),
        sp.GetRequiredService<IOptions<AlfrescoOptions>>(),
        sp.GetRequiredService<ILoggerFactory>());
});

// 4. Health checker
services.AddSingleton<IAlfrescoHealthChecker, AlfrescoHealthChecker>();
```

---

## 9. Preporučene konfiguracije za batch scenario

### `appsettings.json` — Polly (konzervativan za nepoznat server)

```json
"PollyPolicy": {
  "ReadOperations": {
    "TimeoutSeconds": 60,
    "RetryCount": 3,
    "CircuitBreakerFailuresBeforeBreaking": 5,
    "CircuitBreakerDurationOfBreakSeconds": 60,
    "BulkheadMaxParallelization": 8,
    "BulkheadMaxQueuingActions": 16
  },
  "WriteOperations": {
    "TimeoutSeconds": 120,
    "RetryCount": 2,
    "CircuitBreakerFailuresBeforeBreaking": 5,
    "CircuitBreakerDurationOfBreakSeconds": 60,
    "BulkheadMaxParallelization": 5,
    "BulkheadMaxQueuingActions": 10
  }
},
"Migration": {
  "MaxDegreeOfParallelism": 3,
  "PreviewFolderPreparation": {
    "MaxDegreeOfParallelism": 5,
    "BatchSize": 100
  },
  "PreviewFolderCreation": {
    "MaxDegreeOfParallelism": 3,
    "BatchSize": 25
  },
  "PreviewToStagingTransfer": {
    "MaxDegreeOfParallelism": 4,
    "BatchSize": 200
  }
}
```

### `appsettings.json` — Recovery (opciono, za konfigurabilnost)

```json
"PreviewMigration": {
  "MaxRecoveryAttempts": 10,
  "RecoveryPollIntervalSeconds": 30
}
```

---

## 10. Observability — metrike i alarmi

### Što dodati u `PolicyHelpers.cs`

```csharp
// Circuit Breaker — logovati svaku promenu stanja
onBreak: (outcome, duration) =>
{
    fileLogger?.LogError("CB OPENED for {Duration}s. Failure: {Error}",
        duration.TotalSeconds, outcome?.Exception?.Message);
    // TODO: Increment counter metrike "alfresco.circuit_breaker.open"
},
onReset: () =>
{
    fileLogger?.LogInformation("CB CLOSED — requests resume");
    // TODO: Increment counter metrike "alfresco.circuit_breaker.close"
},

// Bulkhead — logovati rejection rate
onBulkheadRejectedAsync: context =>
{
    fileLogger?.LogWarning("Bulkhead rejected — server overloaded");
    // TODO: Increment counter metrike "alfresco.bulkhead.rejected"
    return Task.CompletedTask;
}
```

### Što dodati u `AlfrescoReadApi.SearchAsync`

```csharp
var sw = Stopwatch.StartNew();
using var postResponse = await _client.PostAsync(url, bodyRequest, ct).ConfigureAwait(false);
sw.Stop();

if (sw.ElapsedMilliseconds > 5000)
    _fileLogger.LogWarning("SLOW SEARCH: {Elapsed}ms, Query: {Query}",
        sw.ElapsedMilliseconds, inRequest?.Query?.Query);
```

### Alarmi koje treba podesiti u produkciji

| Metrika | Alarm |
|--------|-------|
| CB ostane otvoren > 60s | CRITICAL — Alfresco server problem |
| Retry rate > 10% u 5-min prozoru | WARNING — degradirana dostupnost |
| `AlfrescoTimeoutException` rate > 5/min | WARNING — server pod stresom |
| Recovery loop aktiviran > 3× u sat | WARNING — nestabilna veza |
| Process memory > 2GB | WARNING — potencijalni memory leak |

---

## 11. Preporučeni redosled implementacije

```
Korak 1: Auto-Recovery (Faza 1)                          ← ODMAH
   • IAlfrescoHealthChecker + AlfrescoHealthChecker
   • Modifikacija BtnStartFaza1_Click
   • Recovery delay čita CB duration iz configa
   Zašto prvo: Ne menja HttpClient, nema rizika regrešije.
   Samostalna promena koja odmah donosi vrednost.

         ↓

Korak 2: C-3 FolderExistsAsync / GetFolderByNameAsync    ← ODMAH
   • Zamena /children listing sa AFTS search query-jem
   Zašto rano: Correctness bug — tihi false negatives.
   Izmenjen samo AlfrescoReadApi.cs, bez DI promena.

         ↓

Korak 3: H-3 Ukloni DefaultRequestHeaders.Authorization  ← LAKO, BEZ RIZIKA
   • Brisanje 2 linije iz App.xaml.cs
   Zašto: Mrtav kod koji zbunjuje — ne menja ponašanje.

         ↓

Korak 4: H-2 Ukloni retry na BrokenCircuitException      ← PRE C-1
   • Brisanje 2 linije iz PolicyHelpers.cs
   Zašto pre C-1: Priprema teren za kad CB počne da radi.
   Bez efekta dok je C-1 bug aktivan.

         ↓

Korak 5: H-1 Ispravi redosled policy-ja                  ← PRE C-1
   • PolicyHelpers.cs: WrapAsync(fallback, bulkhead, CB, retry, timeout)
   Zašto pre C-1: Korektan redosled za kad CB i Bulkhead budu singletons.

         ↓

Korak 6: C-1 PolicyRegistry singleton                    ← KLJUČNI FIX
   • Dodati services.AddPolicyRegistry(...)
   • Promeniti .AddPolicyHandler(lambda) → .AddPolicyHandlerFromRegistry(name)
   Zašto sada: Koraci 4 i 5 su postavljeni, CB i Bulkhead sada zaista rade.
   CB break duration iz configa sada utiče na recovery delay (Korak 1 to već čita).

         ↓

Korak 7: M-1 SetHandlerLifetime fix                      ← NISKA RIZIČNOST
   • Promeniti SetHandlerLifetime na 2× PooledConnectionLifetime
   Izmenjen samo App.xaml.cs.

         ↓

Korak 8: M-3 Timeout i parallelism konfiguracija         ← TUNING
   • Smanjiti TimeoutSeconds: 240 → 60 (Read), 120 (Write)
   • Smanjiti BulkheadMaxParallelization: 50 → 8 (Read), 5 (Write)
   • Pratiti logove 24h i po potrebi podesiti naviše
   PAŽNJA: Ovo menja ponašanje u produkciji. Pratiti latenciju.

         ↓

Korak 9: C-2 Captive Dependency                          ← ARHITEKTURNA PROMENA
   • Promeniti typed klijente na named klijente
   • Singleton servisi koriste IServiceScopeFactory za HTTP pozive
   Zašto zadnje: Najveća promena, zahteva testiranje svakog servisa.
   Preporučiti deployment u test okruženje prvo.

         ↓

Korak 10: H-4 Streaming + H-5 Log level                 ← PERFORMANCE
   • ReadAsStreamAsync + JsonSerializer.DeserializeAsync
   • Info → Debug za response body logging
   Zašto zadnje: Benefit pri velikom throughput-u, ali zahteva
   promenu serialization biblioteke (Newtonsoft → STJ).
```

---

## 12. Kompatibilnost između fiks-ova

### Auto-Recovery × HttpClient fiks-ovi

| HttpClient Fix | Utiče na auto-recovery? | Potrebna prilagodba |
|----------------|------------------------|---------------------|
| C-1 PolicyRegistry | **Da — indirektno** | Recovery delay čita CB duration iz configa — **već implementirano u Koraku 1** |
| C-2 Captive dependency | Ne — ako `PreviewLoadService` ostaje Singleton | Nema |
| C-3 FolderExistsAsync | Ne | Nema |
| H-1 Policy redosled | Ne | Nema |
| H-2 Ukloni CB retry | Ne — zapravo poboljšava recovery | Nema |
| H-3 Double auth | Ne | Nema |
| H-4 Streaming | Ne | Nema |
| H-5 Log level | Ne | Nema |
| M-1 SetHandlerLifetime | Ne | Nema |
| M-2/M-3 CurrentUserClient | Ne — health checker koristi isti klijent, limit 2 konex. je sasvim dovoljan | Nema |

### Ključna interakcija: C-1 + Recovery delay

- **Pre C-1 fixa:** CB per-request → nikad OPEN → `delay(CB_sec + 5s)` ≈ `delay(65s)` u konfig — prihvatljivo  
- **Posle C-1 fixa:** CB singleton → kad Alfresco padne, CB se otvori → health check potvrdi konekciju → delay 65s → CB sigurno CLOSED (break duration 60s) → restart uspešan  
- **Ako se delay ne desi (5s):** CB još OPEN → prvi request → `BrokenCircuitException` → Fallback → recovery ponovo se aktivira → `recoveryAttempt++` → eventualno odustaje  
- **Zaštita:** Recovery delay je implementiran da čita `CircuitBreakerDurationOfBreakSeconds` iz konfiga od Koraka 1, tako da C-1 fix ne zahteva modifikaciju recovery koda

---

*Kraj dokumenta — HTTP_CLIENT_RELIABILITY_AUDIT.md*
