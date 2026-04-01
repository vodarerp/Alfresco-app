# .NET Legacy Code Analysis Report
**Project:** Alfresco.DocStatusUpdater (WPF Desktop Application)
**Framework:** .NET 8.0 (WPF)
**Date:** 2026-03-30
**Scope:** Kompletna analiza WPF aplikacije — `MainWindow.xaml.cs`, `App.xaml.cs`, `MainWindow.xaml`, `appsettings.json`

## Executive Summary
- **Overall Health Score:** 5/10
- **Findings:** 2 critical, 4 high, 7 medium, 3 low
- **Top 3 Priorities:**
  1. Ukloniti hardkodovane kredencijale iz `appsettings.json` koji se čuvaju u source control-u
  2. Ispraviti deljeni `CancellationTokenSource` — jedna operacija može otkazati ili oštetiti drugu
  3. Thread-safety problem: čitanje `_allItems`/`_dossierItems` iz UI thread-a dok se menjaju iz paralelnih task-ova
- **Quick Wins:** Razdvojiti `_cts` po tab-u, dodati `ConfigureAwait(false)` na `GetIdExpozituraAsync`, izvući magic string-ove u konstante
- **Technical Debt Level:** Significant — god class (847 linija), masivna duplikacija koda, nepostojanje logovanja van TextBox-a

---

## Table of Contents
- [Phase 1: Critical Issues](#phase-1-critical-issues)
- [Phase 2: Deep Analysis](#phase-2-deep-analysis)
- [Phase 3: Optimization & Refactoring](#phase-3-optimization--refactoring)
- [Phase 4: Infrastructure (delimično)](#phase-4-infrastructure-delimično)

---

## Phase 1: Critical Issues

### 🔴 [CRITICAL] Hardkodovani kredencijali u source control-u
**Location:** appsettings.json → Alfresco sekcija
**Category:** Security
**Risk:** Alfresco admin kredencijali (`admin/admin`) su commitovani u git repozitorijum. Svako sa pristupom repo-u ima admin pristup Alfresco instanci.
**Problem:**
```json
"Alfresco": {
    "BaseUrl": "http://localhost:8080",
    "Username": "admin",
    "Password": "admin"
}
```
Čak i ako su ovo dev kredencijali, pattern je opasan — lako se desi da neko stavi produkcijske.
**Suggested Approach:** Koristiti .NET User Secrets za development, environment varijable ili secrets manager za produkciju:
```csharp
// U appsettings.json ostaviti samo strukturu bez vrednosti
// Kredencijale čitati iz env varijabli:
// ALFRESCO__Username, ALFRESCO__Password
config.AddEnvironmentVariables();
```
Dodati `appsettings.json` u `.gitignore` i commitovati samo `appsettings.Example.json` sa placeholder-ima.
**Effort:** Low

---

### 🔴 [CRITICAL] Deljeni CancellationTokenSource između nezavisnih operacija
**Location:** MainWindow.xaml.cs → polje `_cts` (linija 24)
**Category:** Bug / Data Corruption Risk
**Risk:** Korisnik može pokrenuti operaciju na jednom tabu dok je druga na drugom tabu u toku. Novo dodeljivanje `_cts` preskače `Dispose()` na starom, a `Cancel` na jednom tabu otkazuje operaciju na drugom.
**Problem:**
```csharp
private CancellationTokenSource? _cts;  // deljeno između sva 4 handler-a

// BtnSearch_Click:
_cts = new CancellationTokenSource();  // stari CTS je "lost" ako prethodni nije završen

// BtnDossierCancel_Click:
_cts?.Cancel();  // otkazuje BILO KOJU aktivnu operaciju, ne samo dossier
```
Scenario: korisnik klikne "Ucitaj dosieje", pa dok traje učitavanje klikne "Pretrazi dokumente" na drugom tabu → stari CTS nikad nije disposed, a Cancel na prvom tabu otkazuje pretragu.
**Suggested Approach:** Koristiti odvojene CTS-ove za svaki tab:
```csharp
private CancellationTokenSource? _searchCts;
private CancellationTokenSource? _dossierCts;

// U svakom handler-u:
_searchCts?.Cancel();
_searchCts?.Dispose();
_searchCts = new CancellationTokenSource();
```
**Effort:** Low

---

### 🟠 [HIGH] Thread-safety: čitanje kolekcije tokom paralelne mutacije
**Location:** MainWindow.xaml.cs → BtnUpdateAll_Click (linija 283), BtnDossierUpdate_Click (linija 589)
**Category:** Thread Safety
**Risk:** `_allItems.Count(x => !x.IsUpdated)` se poziva iz `Dispatcher.Invoke` (UI thread) dok `Parallel.ForEachAsync` istovremeno menja `IsUpdated` na objektima u toj kolekciji. `List<T>` nije thread-safe za čitanje dok se menjaju elementi (iako se ne menja sam List).
**Problem:**
```csharp
// Iz paralelnog task-a:
item.IsUpdated = true;  // linija 255

// Iz Dispatcher.Invoke (UI thread), u istom trenutku:
TxtNotUpdatedCount.Text = _allItems.Count(x => !x.IsUpdated).ToString(); // linija 283
```
`IsUpdated` je `bool` property bez `volatile` ili barijere — čitanje iz drugog thread-a može videti staru vrednost (stale read). Iako u praksi na x86 ovo retko puca, formalno je data race.
**Suggested Approach:** Osloniti se samo na `Interlocked` brojače za UI prikaz umesto LINQ upita nad deljenom kolekcijom:
```csharp
// Umesto _allItems.Count(x => !x.IsUpdated):
TxtNotUpdatedCount.Text = (total - success - failed).ToString();
```
**Effort:** Low

---

### 🟠 [HIGH] Nema retry/resilience politike za Alfresco API pozive
**Location:** MainWindow.xaml.cs → BtnUpdateAll_Click (linija 251), BtnDossierUpdate_Click (linija 559)
**Category:** Resiliency
**Risk:** Svaki API poziv (`UpdateNodePropertiesAsync`) ima samo jedan pokušaj. Alfresco API može privremeno biti nedostupan (timeout, 503) — u tom slučaju dokument se označava kao "neuspešan" bez ponovnog pokušaja.
**Problem:**
```csharp
var updated = await _writeApi.UpdateNodePropertiesAsync(item.NodeId, properties, ct);
// Transient failure → item.UpdateMessage = "Greska: ..." → prebačen u "neuspešno"
```
Za batch operaciju koja obrađuje stotine/hiljade stavki, jedan kratak network blip može uzrokovati veliki broj neuspelih.
**Suggested Approach:** Dodati Polly retry politiku na HttpClient registraciju u `App.xaml.cs`:
```csharp
.AddTransientHttpErrorPolicy(p =>
    p.WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))))
```
Ili na nivou aplikacione logike, implementirati retry samo za transient greške (HttpRequestException, TaskCanceledException sa timeout-om).
**Effort:** Medium

---

### 🟠 [HIGH] Nedostaje ConfigureAwait(false) u GetIdExpozituraAsync
**Location:** MainWindow.xaml.cs → GetIdExpozituraAsync (linija 733-761)
**Category:** Async
**Risk:** Metoda se poziva iz `Parallel.ForEachAsync` koji koristi thread pool. `SemaphoreSlim.WaitAsync` i Dapper `QueryFirstOrDefaultAsync` hvataju SynchronizationContext. U WPF aplikaciji, ako se ovi await-ovi vrate na dispatcher thread (maloverovatno iz thread pool-a ali moguće u edge case-ovima), može doći do deadlock-a sa `Dispatcher.Invoke` pozivima.
**Problem:**
```csharp
await _cacheLock.WaitAsync(ct);  // nema ConfigureAwait(false)
// ...
var result = await conn.QueryFirstOrDefaultAsync<string?>(...);  // nema ConfigureAwait(false)
```
**Suggested Approach:**
```csharp
await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
await conn.OpenAsync(ct).ConfigureAwait(false);
var result = await conn.QueryFirstOrDefaultAsync<string?>(...)
    .ConfigureAwait(false);
```
**Effort:** Low

---

### 🟠 [HIGH] SemaphoreSlim serijalizuje sve DB upite za keš
**Location:** MainWindow.xaml.cs → GetIdExpozituraAsync (linija 740)
**Category:** Performance / Thread Safety
**Risk:** `_cacheLock` je `SemaphoreSlim(1,1)` — svi paralelni radnici čekaju na jedan lock za SVAKI keš-promašaj. Ako ima 100 različitih `idZaposleni` vrednosti, svi DB upiti se izvršavaju sekvencijalno, čime se poništava efekat paralelizacije.
**Problem:**
```csharp
await _cacheLock.WaitAsync(ct);  // SVIH MaxDOP radnika čeka na ovo
try {
    // DB upit traje ~10-50ms, ali samo 1 radnik može da ga radi
}
```
**Suggested Approach:** Koristiti `ConcurrentDictionary` sa `Lazy<Task<T>>` umesto ručnog lockinga:
```csharp
private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _expozituraCache = new();

private Task<string?> GetIdExpozituraAsync(string idZaposleni, CancellationToken ct)
{
    return _expozituraCache.GetOrAdd(idZaposleni,
        key => new Lazy<Task<string?>>(() => LoadExpozituraFromDbAsync(key, ct))).Value;
}
```
Alternativno, smanjiti granularnost lock-a — lock po `idZaposleni` ključu.
**Effort:** Medium

---

## Phase 2: Deep Analysis

### 🟡 [MEDIUM] God Class — MainWindow.xaml.cs (847 linija)
**Location:** MainWindow.xaml.cs → MainWindow klasa
**Category:** Code Quality / Maintainability
**Risk:** Sve je u jednoj klasi: pretraga, ažuriranje, dossier učitavanje, dossier ažuriranje, export, keš, DB pristup. Svaka promena nosi rizik od nenamerne regresije.
**Problem:** Klasa ima 6+ odgovornosti i 15+ metoda. Konstruktor ručno resolve-uje servise iz DI kontejnera umesto constructor injection-a.
**Suggested Approach:** Izdvojiti logiku u servise:
- `DocStatusSearchService` — pretraga i filtriranje
- `DocStatusUpdateService` — batch ažuriranje
- `DossierService` — učitavanje i ažuriranje dosieja
- `ExportService` — CSV/log export

MainWindow postaje tanak — samo deli UI evente na servisne pozive.
**Effort:** High

---

### 🟡 [MEDIUM] Masivna duplikacija koda između tab-ova
**Location:** MainWindow.xaml.cs → BtnUpdateAll_Click (linija 195-334) vs BtnDossierUpdate_Click (linija 495-639)
**Category:** Code Quality / DRY
**Risk:** Iste greške se moraju ispravljati na dva mesta. Isti pattern (Parallel.ForEachAsync + Interlocked + Dispatcher.Invoke + try/catch/finally) je copy-paste sa minimalnim razlikama.
**Problem:** Oba handler-a imaju identičan skeleton:
1. Filter items → confirm → SetBusy → init counters
2. Parallel.ForEachAsync sa Interlocked + Dispatcher.Invoke
3. Refresh DataGrid → MessageBox → finally SetBusy(false)

Razlikuju se samo u: properties dictionary, API poziv, UI elementi.
**Suggested Approach:** Izvući generički `RunParallelUpdateAsync<T>` metod:
```csharp
private async Task RunParallelUpdateAsync<T>(
    List<T> items,
    Func<T, CancellationToken, Task<bool>> updateFunc,
    Action<int, int, int> onProgress,
    ProgressBar progressBar) { ... }
```
**Effort:** Medium

---

### 🟡 [MEDIUM] Duplikacija HttpClient konfiguracije u App.xaml.cs
**Location:** App.xaml.cs → ConfigureServices (linije 50-97)
**Category:** Code Quality / DRY
**Risk:** Read API i Write API HttpClient konfiguracije su identične (iste auth, isti SocketsHttpHandler). Promena parametra (npr. timeout) se mora menjati na oba mesta.
**Problem:** ~45 linija copy-paste koda za konfiguraciju dva HttpClient-a.
**Suggested Approach:** Izdvojiti konfiguraciju u extension metod ili koristiti `ConfigureAll`:
```csharp
void ConfigureAlfrescoClient(IServiceProvider sp, HttpClient cli) { ... }

services.AddHttpClient<IAlfrescoReadApi, AlfrescoReadApi>()
    .ConfigureHttpClient(ConfigureAlfrescoClient)
    .ConfigurePrimaryHttpMessageHandler(CreateHandler);
```
**Effort:** Low

---

### 🟡 [MEDIUM] Magic string-ovi rasuti kroz kod
**Location:** MainWindow.xaml.cs → više lokacija
**Category:** Code Quality
**Risk:** Typo u property imenu (npr. `"ecm:docStatu"` umesto `"ecm:docStatus"`) teško se otkriva.
**Problem:**
```csharp
{ "ecm:docStatus", "2" }
{ "ecm:bnkSource", "DUT22" }
{ "ecm:bnkCreator", item.DocCreator }
{ "ecm:bnkStatus", "AKTIVAN" }
GetProperty(node.Properties, "ecm:docStatus");
```
**Suggested Approach:** Definisati konstante:
```csharp
private static class AlfrescoProps
{
    public const string DocStatus = "ecm:docStatus";
    public const string BnkSource = "ecm:bnkSource";
    // ...
}
```
**Effort:** Low

---

### 🟡 [MEDIUM] Fragilan detection puta do appsettings.json
**Location:** App.xaml.cs → ConfigureAppConfiguration (linije 30-38)
**Category:** Bug
**Risk:** Provera `basePath.Contains(@"\bin\Debug\")` je fragilan i platform-specifičan. Ne radi za custom build konfiguracije, i `Parent!.Parent!.Parent!.Parent!` može baciti `NullReferenceException` u neočekivanim putanjama.
**Problem:**
```csharp
if (basePath.Contains(@"\bin\Debug\") || basePath.Contains(@"\bin\Release\"))
{
    var projectRoot = Directory.GetParent(basePath)!.Parent!.Parent!.Parent!.FullName;
    // NullReferenceException ako struktura foldera nije očekivana
}
```
**Suggested Approach:** Koristiti standardno ponašanje `Host.CreateDefaultBuilder` koji već traži `appsettings.json` u `ContentRootPath`. Alternativno, koristiti `Directory.GetCurrentDirectory()` ili `Assembly.GetExecutingAssembly().Location`.
```csharp
.ConfigureAppConfiguration((context, config) =>
{
    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
})
```
Pošto je `appsettings.json` već konfigurisan sa `<CopyToOutputDirectory>Always</CopyToOutputDirectory>` u `.csproj`, biće u output direktorijumu.
**Effort:** Low

---

### 🟡 [MEDIUM] `DateTime.Now` umesto `DateTime.UtcNow`
**Location:** MainWindow.xaml.cs → AppendLog (linija 775), file naming (linija 160, 667)
**Category:** Bug Pattern
**Risk:** Za desktop aplikaciju ovo je manjeg značaja nego za server, ali ako se logovi porede sa serverskim logovima (Alfresco), razlika u timezone-u otežava korelaciju.
**Problem:**
```csharp
var fileName = $"DocStatusSearch_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
```
**Suggested Approach:** Koristiti `DateTimeOffset.Now` ili dodati timezone info u log format (`HH:mm:ss zzz`).
**Effort:** Low

---

### 🟡 [MEDIUM] Resolver servisa iz DI kontejnera u konstruktoru
**Location:** MainWindow.xaml.cs → konstruktor (linije 29-36)
**Category:** Code Quality / DI Anti-Pattern
**Risk:** Service Locator anti-pattern — zavisnosti nisu eksplicitne, teže za testiranje.
**Problem:**
```csharp
public MainWindow()
{
    _readApi = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();
    // ... 5 drugih resolve-ova
}
```
A u `App.xaml.cs` MainWindow je registrovan kao `Transient`:
```csharp
services.AddTransient<MainWindow>();
```
Ali se nigde ne koristi taj DI — `StartupUri="MainWindow.xaml"` kreira instancu mimo DI kontejnera.
**Suggested Approach:** Koristiti DI pravilno — kreirati MainWindow kroz kontejner:
```csharp
// App.xaml: ukloniti StartupUri
// App.xaml.cs:
protected override void OnStartup(StartupEventArgs e)
{
    AppHost.Start();
    var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
    mainWindow.Show();
    base.OnStartup(e);
}

// MainWindow konstruktor sa DI:
public MainWindow(IAlfrescoReadApi readApi, IAlfrescoWriteApi writeApi, ...) { ... }
```
**Effort:** Low

---

## Phase 3: Optimization & Refactoring

### 🔵 [LOW] Model klase u istom fajlu kao MainWindow
**Location:** MainWindow.xaml.cs → DocStatusItem (linija 823), DossierUpdateItem (linija 836)
**Category:** Code Organization
**Risk:** Otežava pronalaženje i deljenje modela između klasa.
**Suggested Approach:** Prebaciti u `Models/DocStatusItem.cs` i `Models/DossierUpdateItem.cs`.
**Effort:** Low

---

### 🔵 [LOW] Nepostojanje INotifyPropertyChanged na model klasama
**Location:** MainWindow.xaml.cs → DocStatusItem, DossierUpdateItem
**Category:** WPF Best Practice
**Risk:** DataGrid ne ažurira pojedinačne redove kada se property promeni — zato se ceo `ItemsSource` zamenjuje novom kolekcijom nakon operacije. Ovo je funkcionalno ali nepotrebno rekreira UI elemente.
**Suggested Approach:** Implementirati `INotifyPropertyChanged` ili koristiti community toolkit `[ObservableProperty]`:
```csharp
public class DocStatusItem : INotifyPropertyChanged
{
    private bool _isUpdated;
    public bool IsUpdated
    {
        get => _isUpdated;
        set { _isUpdated = value; OnPropertyChanged(); }
    }
    // ...
}
```
**Effort:** Medium

---

### 🔵 [LOW] Nedostaje strukturirano logovanje
**Location:** MainWindow.xaml.cs → AppendLog, AppendDossierLog
**Category:** Observability
**Risk:** Logovi postoje samo u TextBox-u — kad se aplikacija zatvori, sve je izgubljeno (osim ručnog exporta). Nema mogućnosti za naknadnu analizu grešaka.
**Suggested Approach:** Dodati `ILogger<MainWindow>` i Serilog sa file sink-om pored TextBox-a:
```csharp
private void AppendLog(string message)
{
    _logger.LogInformation(message);  // ide u fajl
    Dispatcher.Invoke(() => {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        TxtLog.ScrollToEnd();
    });
}
```
**Effort:** Medium

---

## Phase 4: Infrastructure (delimično)

> **Napomena:** Phase 4 je dizajnirana za mikroservise. Za ovu WPF aplikaciju, primenjivi su samo delovi o HTTP klijentu i konfiguraciji.

### Pozitivno: HttpClient konfiguracija
`IHttpClientFactory` pattern je **pravilno korišćen** u `App.xaml.cs`:
- `SocketsHttpHandler` sa pool-ovanim konekcijama ✅
- `PooledConnectionLifetime` i `IdleTimeout` postavljeni ✅
- `MaxConnectionsPerServer = 20` ✅
- Basic auth header postavljen ✅
- `ConnectionClose = false` (keep-alive) ✅

### Konfiguracija — nedostaje validacija na startu
**Location:** App.xaml.cs → ConfigureServices
**Category:** Configuration
Nema `ValidateOnStart()` — ako `appsettings.json` nedostaje ili ima pogrešan format, aplikacija pukne tek pri prvoj operaciji umesto na pokretanju.

---

## Summary Statistics

| Category | 🔴 Critical | 🟠 High | 🟡 Medium | 🔵 Low | Total |
|----------|-------------|---------|-----------|--------|-------|
| Security | 1 | - | - | - | 1 |
| Bug / Data Corruption | 1 | 1 | 1 | - | 3 |
| Thread Safety | - | 1 | - | - | 1 |
| Async | - | 1 | - | - | 1 |
| Performance | - | 1 | - | - | 1 |
| Resiliency | - | - | - | - | 0 |
| Code Quality / DRY | - | - | 4 | 1 | 5 |
| WPF Best Practice | - | - | 1 | 1 | 2 |
| Observability | - | - | - | 1 | 1 |
| Configuration | - | - | 1 | - | 1 |
| **Total** | **2** | **4** | **7** | **3** | **16** |

### Prioritizovani action plan:
1. **Odmah:** Ukloniti kredencijale iz `appsettings.json`, razdvojiti `_cts` po tabu
2. **Uskoro:** Dodati `ConfigureAwait(false)`, ispraviti LINQ thread-safety, dodati retry politiku
3. **Planirano:** Refaktorisati god class, eliminisati duplikaciju, pravilno koristiti DI, dodati logovanje u fajl
