# Analiza HTTP klijenta - Uzroci Alfresco HTTP 500

**Datum:** 2026-04-20  
**Scope:** Alfresco.Client, Alfresco.App, CA_MockData  
**Fokus:** Obrasci koji prouzrokuju HTTP 500 sa strane Alfresco servera

---

## Pregled pronađenih problema

| # | Ozbiljnost | Oblast | Fajl | Opis |
|---|-----------|--------|------|------|
| 1 | **KRITIČNO** | Retry | `PolicyHelpers.cs` | Polly retry pokreće non-idempotentni POST ponovo nakon timeoutu |
| 2 | **KRITIČNO** | Multipart/Stream | `CA_MockData/Program.cs` | Stream konzumiran pre retry — prazno telo na drugom pokušaju |
| 3 | **VISOKO** | Policy order | `PolicyHelpers.cs` | Pogrešan redosled wrappa: CB obuhvata Retry, ne obrnuto |
| 4 | **VISOKO** | Autentikacija | `BasicAuthHandler.cs` | `Encoding.ASCII` korumpuje non-ASCII kredencijale |
| 5 | **VISOKO** | Konkurentnost | `AlfrescoWriteApi.cs` | 100-pokušajni while loop × Polly retry = 400 HTTP zahteva |
| 6 | **SREDNJE** | Serijalizacija | `AlfrescoWriteApi.cs` | DateTime vrednosti u properties se ne formatiraju po Alfresco standardu |
| 7 | **SREDNJE** | Error handling | `AlfrescoReadApi.cs` | `FolderExistsAsync` guta sve izuzetke tiho → pogrešna logika kreiranja |

---

## Problem 1 — Polly Retry pokreće non-idempotentni POST nakon timeoutu (KRITIČNO)

### Lokacija
- `Alfresco.App/Helpers/PolicyHelpers.cs`, linija 449–480 (`GetCombinedWritePolicy`)
- `Alfresco.Client/Implementation/AlfrescoWriteApi.cs`, linije 59, 223, 438

### Opis problema

`GetCombinedWritePolicy` koristi isti `GetRetryPolicy` kao i read operacije:

```csharp
// PolicyHelpers.cs:479
return Policy.WrapAsync(fallback, bulkhead, circuitBreaker, retry, timeout)
    .WithPolicyKey("AlfrescoWrite");
```

`GetRetryPolicy` (linije 28–34) ponovo šalje zahtev kad Alfresco vrati 5xx **ili** kada Polly pesimistički timeout (`TimeoutStrategy.Pessimistic`, linija 213) okine:

```csharp
Policy
    .HandleResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
    .Or<TimeoutRejectedException>()
    ...
    .WaitAndRetryAsync(retryCount: retryCount, ...)
```

Pesimistički timeout (240s) **prisilno prekida HTTP zahtev na klijentskoj strani**, ali Alfresco server može biti usred transakcije. Kada Polly pošalje isti POST ponovo:

1. `CreateFileAsync` → `POST /nodes/{parentId}/children` sa `name = X`  
2. Alfresco obradi zahtev 1 (kreira čvor) dok Polly čeka  
3. Polly timeout okine → Polly šalje POST ponovo sa istim `name = X`  
4. Alfresco prima novi POST dok je možda još u transakciji za čvor 1  
5. **Rezultat:** DB deadlock ili constraint violation → Alfresco 500

Isto važi za `CreateFolderInternalAsync` (linija 223 i 438) — oba POST poziva idu kroz isti write policy.

### Zašto Alfresco vraća 500 (a ne 409)

Alfresco vraća 409 samo kada je prethodni čvor već u indeksu. Ako se drugi POST pojavi dok je Alfresco u toku prve transakcije (nije commit), Alfresco's NodeService baca `IntegrityException` ili `DuplicateChildNodeNameException` pre nego što stigne do name-check sloja → neobrađeni `RuntimeException` → HTTP 500.

### Minimalni ispravak

Napraviti odvojenu `GetWritePolicy` koja **ne retry-uje** POST create operacije na timeout:

```csharp
// U GetCombinedWritePolicy — ne koristiti retry na TimeoutRejectedException za write
Policy
    .HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.ServiceUnavailable)
    .Or<HttpRequestException>()
    // NE: .Or<TimeoutRejectedException>()  ← ukloniti za non-idempotentne POST
    .WaitAndRetryAsync(retryCount: retryCount, ...);
```

Alternativno, dodati `autoRename: true` u payload CreateFileAsync i prihvatiti da Alfresco sam reši conflict umesto da se server-side transakcija ponavlja.

---

## Problem 2 — CA_MockData: Stream konzumiran pre retry, prazno telo na drugom pokušaju (KRITIČNO)

### Lokacija
- `CA_MockData/Program.cs`, linije 479–537 (`CreateDocumentAsync`)
- `CA_MockData/Program.cs`, linije 649–675 (`Clone`)

### Opis problema

`CreateDocumentAsync` kreira multipart zahtev sa `StreamContent(content)` koji omata ulazni `Stream content`:

```csharp
// Program.cs:495-497
var sc = new StreamContent(content);
sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
form.Add(sc, "filedata", name);

using var createReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
using var createRes = await SendWithRetryAsync(http, cfg, createReq, ct);
```

`SendWithRetryAsync` poziva `Clone(req)` pri svakom pokušaju:

```csharp
// Program.cs:591
res = await http.SendAsync(Clone(req), HttpCompletionOption.ResponseHeadersRead, ct);
```

`Clone` čita `req.Content` (MultipartFormDataContent) u `MemoryStream` **sinhrono**:

```csharp
// Program.cs:659-661
var ms = new MemoryStream();
req.Content.CopyToAsync(ms).GetAwaiter().GetResult();  // čita originalni Stream
ms.Position = 0;
```

**Scenario retry:**

1. Pokušaj 1: `Clone(createReq)` poziva `CopyToAsync` na `form` → čita `content` stream do kraja → šalje Alfrescu
2. Alfresco vrati 5xx → retry
3. Pokušaj 2: `Clone(createReq)` opet poziva `CopyToAsync` na **isti** `form` → underlying `content` je na poziciji end → 0 bajtova se kopira
4. Alfresco prima multipart zahtev sa `filedata` od 0 bajtova → parsira multipart → baca NullPointerException ili ArrayIndexOutOfBoundsException interno → **HTTP 500**

Dodatni problem: `.GetAwaiter().GetResult()` u `Clone` je blokirajući poziv unutar async koda — može prouzrokovati deadlock u aplikacijama sa `SynchronizationContext`.

### Minimalni ispravak

Preočitati `content` Stream u `MemoryStream` PRE kreiranja forme, pa resetovati poziciju pri svakom pokušaju:

```csharp
// Pre petlje retry
var ms = new MemoryStream();
await content.CopyToAsync(ms, ct);

// U svakom pokušaju
ms.Position = 0;
using var form = new MultipartFormDataContent();
form.Add(new StringContent(name), "name");
form.Add(new StringContent("cm:content", Encoding.UTF8), "nodeType");
form.Add(new StringContent("false"), "autoRename");
var sc = new StreamContent(ms);
sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
form.Add(sc, "filedata", name);
```

---

## Problem 3 — Pogrešan redosled Polly policy wrappa: Circuit Breaker obuhvata Retry (VISOKO)

### Lokacija
- `Alfresco.App/Helpers/PolicyHelpers.cs`, linije 441–446 i 475–480

### Opis problema

Komentar u kodu dokumentuje **nameravani** redosled:

```
// Execution flow: Fallback → Retry → Timeout → CircuitBreaker → Bulkhead → HttpClient
```

Ali **stvarni** kod je:

```csharp
// PolicyHelpers.cs:445
return Policy.WrapAsync(fallback, bulkhead, circuitBreaker, retry, timeout)
    .WithPolicyKey("AlfrescoRead");
```

U Polly `Policy.WrapAsync(p1, p2, p3, p4, p5)`, p1 je **spoljašnji** (outermost), p5 je **unutrašnji** (innermost). Dakle stvarni redosled je:

```
fallback → bulkhead → circuitBreaker → retry → timeout → HTTP
```

Ovo znači da **Circuit Breaker obuhvata Retry** (Retry je unutar CB). Posledice:

**A) CB se otvara mnogo sporije od nameravnog:**
- CB treba 5 grešaka da se otvori
- Ali CB vidi sve 3 retry pokušaje kao JEDAN događaj (jer su unutar njega)
- Potrebno je 5 × (1 + 3 retry) = **do 20 HTTP grešaka** pre nego što CB otvori — umesto nameravanih 5

**B) Bulkhead je izvan CB:**
- Kada CB otvori, bulkhead i dalje rezerviše slotove za zahteve koji odmah budu odbijeni od CB
- Bulkhead se puni, novi zahtevi dobijaju `BulkheadRejectedException` — lažni error koji se prosledi ka Alfrescu jer fallback baca `AlfrescoRetryExhaustedException`

**Veza sa Alfresco 500:**  
Pošto CB otvara 4× sporije, u periodu kada Alfresco servira 500 odgovore, klijent nastavlja da šalje zahteve mnogo duže nego što treba → bombardovanje Alfresco servera → overload → više 500 odgovora.

### Minimalni ispravak

Promeniti redosled da odgovara komentaru i nameravnom dizajnu:

```csharp
// Nameravani redosled: Fallback → Bulkhead → Retry → Timeout → CircuitBreaker
return Policy.WrapAsync(fallback, bulkhead, retry, timeout, circuitBreaker)
    .WithPolicyKey("AlfrescoRead");
```

Ovim CB vidi svaki retry pokušaj kao poseban event i otvara se na prvih 5 HTTP grešaka.

---

## Problem 4 — `Encoding.ASCII` u BasicAuthHandler korumpuje non-ASCII kredencijale (VISOKO)

### Lokacija
- `Alfresco.Client/Handlers/BasicAuthHandler.cs`, linija 37

### Opis problema

```csharp
// BasicAuthHandler.cs:37
var byteArray = Encoding.ASCII.GetBytes($"{_username}:{_password}");
request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
```

`Encoding.ASCII` tiho zamenjuje svaki non-ASCII karakter (npr. dijakritičke znakove u lozinci) sa `0x3F` (`?`). Lozinka `"P@ss#lörd"` postaje `"P@ss#l?rd"` u base64 headeru.

**Kontrast sa CA_MockData** koji radi ispravno:

```csharp
// CA_MockData/Program.cs:406
var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.Password}"));
```

**Mehanizam za Alfresco 500:**  
Alfresco prima korumpovane kredencijale → AuthenticationFilter dekoduje base64 → dobija neispravno korisničko ime/lozinku → poziva Alfresco AuthenticationService → u nekim konfigurisanjima (LDAP, SSO, custom auth subsystem) ovo ne vraća standardni 401 već izaziva `RuntimeException` u auth processing sloju → **HTTP 500**.

Alfresco-ovi Spring Security filtri su dizajnirani da vraćaju 401, ali custom `AuthenticationComponent` implementacije (posebno LDAP/Kerberos integracije) ne garantuju 401 za sve auth failure scenarije.

### Minimalni ispravak

```csharp
// BasicAuthHandler.cs:37 - promena
var byteArray = Encoding.UTF8.GetBytes($"{_username}:{_password}");
```

---

## Problem 5 — `MoveDocumentAsync` 100-pokušajni while loop × Polly retry = do 400 HTTP zahteva (VISOKO)

### Lokacija
- `Alfresco.Client/Implementation/AlfrescoWriteApi.cs`, linije 531–695

### Opis problema

```csharp
// AlfrescoWriteApi.cs:531
const int MAX_RETRY_ATTEMPTS = 100;
int attemptNumber = 0;

while (attemptNumber < MAX_RETRY_ATTEMPTS)
{
    // ...
    using var res = await _client.PostAsync(url, content, ct);  // Ovo IDE KROZ POLLY (3 retry)
    
    if (res.StatusCode == Conflict && nameConflict)
    {
        attemptNumber++;
        currentName = await GenerateNewNameWithSuffixAsync(nodeId, currentName, attemptNumber, ct); // + GET kroz Polly
        continue;
    }
}
```

Svaki `_client.PostAsync` poziv **prolazi kroz Polly WritePolicy** (3 retry po pokušaju).

**Worst-case scenario:**
- 100 while iteracija × (1 POST + 3 Polly retry) = **400 POST zahteva** na `/nodes/{nodeId}/move`
- Plus: `GenerateNewNameWithSuffixAsync` (linija 939) poziva `_client.GetAsync` (još 100 × 4 = 400 GET zahteva)
- Ukupno: **do 800 HTTP zahteva** za jednu `MoveDocumentAsync` operaciju

**Veza sa Alfresco 500:**  
800 zahteva za jednu move operaciju, sa eksponencijalnim backoffom (2s, 4s, 8s + jitter), može trajati:
- 100 × (2+4+8 + max jitter) ≈ **2300+ sekundi** za jednu migraciju operaciju
- U konkurentnom batch kontekstu (Bulkhead: 100 paralelnih write poziva), ovo generiše hiljade konkurentnih HTTP zahteva
- Alfresco's Tomcat thread pool se iscrpljuje → `503` ili interno `500` zbog nedostatka worker threadova

Dodatno: `GenerateNewNameWithSuffixAsync` koji poziva `_client.GetAsync` unutar while loopa koristi isti `_client` koji ide kroz WritePolicy (ne ReadPolicy) — nepotrebno agresivno.

### Minimalni ispravak

Smanjiti `MAX_RETRY_ATTEMPTS` na razumnih 10, i koristiti `_readClient` (ako je dostupan) ili dodati direktni `GetAsync` bez write Polly wrappa:

```csharp
const int MAX_RETRY_ATTEMPTS = 10;  // umesto 100
```

Za `GenerateNewNameWithSuffixAsync`, koristiti zasebni `_client` vezan za read policy.

---

## Problem 6 — DateTime vrednosti u properties se serijalizuju bez Alfresco-kompatibilnog formata (SREDNJE)

### Lokacija
- `Alfresco.Client/Implementation/AlfrescoWriteApi.cs`, linije 200–299 i 410–514 (`CreateFolderInternalAsync`)
- `Alfresco.Client/Implementation/AlfrescoWriteApi.cs`, linije 809–912 (`UpdateNodePropertiesAsync`)

### Opis problema

Properties Dictionary koji se šalje Alfrescu:

```csharp
// AlfrescoWriteApi.cs:217-219
if (properties != null && properties.Count > 0)
{
    body.properties = properties;
}
var json = JsonConvert.SerializeObject(body, jsonSerializerSettings);
```

`jsonSerializerSettings` koristi samo `NullValueHandling.Ignore` i `CamelCasePropertyNamesContractResolver`. Nema `DateFormatString` konfiguracije.

Alfresco REST API očekuje date vrednosti u **ISO 8601 UTC format**: `2024-01-15T12:00:00.000Z`

Newtonsoft.Json bez eksplicitne konfiguracije serijalizuje `DateTime` kao `"2024-01-15T12:00:00"` ili `"2024/01/15 12:00:00"` u zavisnosti od lokalne kulture i `DateTimeKind`.

**Mehanizam za Alfresco 500:**  
Alfresco REST API prosleđuje vrednost `xsd:dateTime` DataTyp-u. Nevalidan format (npr. `"2024-01-15T12:00:00"` bez `Z` ili sa drugačijim formatom) baca `IllegalArgumentException` u Alfresco's `TypeConverter` → neobrađeno → HTTP 500 (ne 400, jer se baca pre HTTP exception mappera).

### Minimalni ispravak

Dodati `DateFormatString` u `JsonSerializerSettings` u svim metodama koje šalju properties:

```csharp
var jsonSerializerSettings = new JsonSerializerSettings
{
    NullValueHandling = NullValueHandling.Ignore,
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ",
    DateTimeZoneHandling = DateTimeZoneHandling.Utc
};
```

---

## Problem 7 — `FolderExistsAsync` guta sve izuzetke tiho (SREDNJE)

### Lokacija
- `Alfresco.Client/Implementation/AlfrescoReadApi.cs`, linije 380–423

### Opis problema

```csharp
// AlfrescoReadApi.cs:417-422
catch (Exception ex)
{
    _fileLogger.LogError(ex, "FolderExistsAsync: Error checking folder existence...");
    return false;  // ← guta 500, timeout, auth grešku — sve vraća false
}
```

Kada Alfresco vrati 500 na `GET /nodes/{parentFolderId}/children`, `FolderExistsAsync` loguje grešku i vraća `false`.

**Mehanizam za kaskadni 500:**
1. Alfresco ima prolazni problem, vraća 500 na GET children zahtev
2. `FolderExistsAsync` vraća `false` (folder "ne postoji")
3. Caller (service layer) odlučuje da kreira folder
4. `CreateFolderAsync` šalje POST za kreiranje foldera koji **već postoji**
5. Alfresco prima POST za dupli folder u sredini potencijalno još uvek nestabilnog stanja → baca `DuplicateChildNodeNameException` → HTTP 409 ili **500** u zavisnosti od stanja transakcije

Pored toga, `AlfrescoTimeoutException` i `AlfrescoRetryExhaustedException` su **i dalje ulovljeni** u opštem `catch (Exception)` — Polly custom izuzeci su dizajnirani da se propagiraju, ali ih `FolderExistsAsync` guta.

### Minimalni ispravak

```csharp
catch (AlfrescoTimeoutException)
{
    throw;  // propagiraj — ne tretirati timeout kao "folder ne postoji"
}
catch (AlfrescoRetryExhaustedException)
{
    throw;  // propagiraj
}
catch (Exception ex)
{
    _fileLogger.LogError(ex, "FolderExistsAsync: Error...");
    return false;  // samo za genuinely recoverable greške
}
```

---

## Sumarni prioriteti

### Hitno (može da prouzrokuje 500 u produkciji odmah)

1. **Problem 2** — CA_MockData retry šalje prazno multipart telo → Alfresco NPE
2. **Problem 1** — Polly retry ponavlja POST create na timeout → Alfresco DB conflict/deadlock

### Visoko (sistemski problem, worsens 500 frequencyy)

3. **Problem 3** — Pogrešan CB/Retry wrap order → CB ne štiti Alfresco na vreme
4. **Problem 4** — ASCII encoding lozinke → garbled auth → Alfresco auth exception
5. **Problem 5** — 100 × 4 HTTP zahteva u MoveDocumentAsync → Alfresco thread pool exhaust

### Srednje (intermitentni, zavisno od podataka)

6. **Problem 6** — DateTime format bez zone → Alfresco TypeConverter exception
7. **Problem 7** — FolderExistsAsync guta timeout → logički cascade ka 500

---

## Šablon ispravke za Problem 1 i Problem 3 (kombinovano)

```csharp
// PolicyHelpers.cs — novi metod za non-idempotentne write operacije
public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
    PolicyOperationOptions? options = null, ...)
{
    // 1. FIX: Timeout ne uzrokuje retry za write operacije
    var retryForWrite = Policy
        .HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.ServiceUnavailable)
        .Or<HttpRequestException>()
        // NE retry na TimeoutRejectedException za POST/PUT/DELETE
        .WaitAndRetryAsync(options.RetryCount, ...);

    // 2. FIX: Ispravan redosled — CB je UNUTAR Retry
    // fallback → bulkhead → retry → timeout → circuitBreaker → HTTP
    return Policy.WrapAsync(fallback, bulkhead, retryForWrite, timeout, circuitBreaker)
        .WithPolicyKey("AlfrescoWrite");
}
```

---

*Izveštaj generisao: Claude Sonnet 4.6, 2026-04-20*
