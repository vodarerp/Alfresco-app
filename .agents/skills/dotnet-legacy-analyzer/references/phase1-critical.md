# Phase 1: Critical Issues

Highest-priority items. Any finding here likely represents a production risk — a security breach,
data loss, or service outage waiting to happen. Present these as "stop and fix now" issues.

## Table of Contents
1. Security Vulnerabilities
2. Memory Leaks
3. Resource Leaks
4. Data Corruption Risks
5. Critical Async Anti-Patterns

---

## 1. Security Vulnerabilities

### SQL Injection
String concatenation or interpolation in SQL queries — the classic and still the most common:

```csharp
// 🔴 VULNERABLE — concatenation
var query = "SELECT * FROM Users WHERE Name = '" + userName + "'";
cmd.CommandText = "DELETE FROM Orders WHERE Id = " + orderId;

// 🔴 VULNERABLE — interpolation
var query = $"SELECT * FROM Users WHERE Name = '{userName}'";

// 🔴 VULNERABLE — EF raw SQL with interpolation
context.Database.SqlQuery<User>($"SELECT * FROM Users WHERE Name = '{name}'");
```

Also check stored procedure calls — if parameters are built via string concatenation before
being passed to `sp_executesql` or `EXEC`, injection still exists on the SQL side.

Watch for dynamic WHERE clause construction:
```csharp
// 🔴 VULNERABLE — dynamic filter building
var where = "WHERE 1=1";
if (hasName) where += $" AND Name LIKE '%{name}%'";
cmd.CommandText = "SELECT * FROM Products " + where;
```

**Suggested approach:** Parameterized queries, always. Show `@param` + `AddWithValue` or
EF parameterized overload.

### Insecure Deserialization
Especially dangerous in legacy .NET Framework code and in services that accept serialized
payloads via Kafka or API endpoints:

```csharp
// 🔴 CRITICAL — BinaryFormatter is inherently unsafe
BinaryFormatter bf = new BinaryFormatter();
var obj = bf.Deserialize(stream);

// 🔴 DANGEROUS — TypeNameHandling enables type injection
JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings {
    TypeNameHandling = TypeNameHandling.All  // or Auto, Objects
});

// Also flag: NetDataContractSerializer, SoapFormatter, LosFormatter, ObjectStateFormatter
```

**Suggested approach:** Remove BinaryFormatter entirely, switch TypeNameHandling to None
(the default), use System.Text.Json or typed deserialization.

### Hardcoded Secrets
Search for credentials, keys, and connection strings embedded in source code:

```csharp
// 🔴 Hardcoded connection strings
var connStr = "Server=prod-db;User Id=sa;Password=P@ssw0rd123;Database=Orders";

// 🔴 API keys and tokens
var apiKey = "sk-abc123def456";
const string JwtSecret = "MySecretKey12345";
private readonly string _token = "Bearer eyJhbG...";
```

Also check:
- `web.config` / `app.config` with plain-text connection strings
- `appsettings.json` committed with real credentials
- Constants classes or static readonly fields with keys/tokens
- Kafka producer/consumer config with plain-text SASL credentials

**Suggested approach:** Environment variables, User Secrets (dev), Azure Key Vault or
similar secrets manager (prod). Point to `IConfiguration` binding pattern.

### Insecure Cryptography
```csharp
// 🔴 WEAK — MD5/SHA1 for password hashing
var hash = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(password));
var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(password));

// 🔴 WEAK — obsolete algorithms
var des = DES.Create();
var tripleDes = TripleDES.Create();
var rc2 = RC2.Create();
```

**Suggested approach:** BCrypt/Argon2/PBKDF2 for passwords, AES-256-GCM for encryption.
Note the specific .NET classes to use.

### Path Traversal
Even in microservice APIs, file-serving endpoints can be vulnerable:

```csharp
// 🔴 VULNERABLE — user controls file path
var filePath = Path.Combine(uploadDir, userFileName);
return File.ReadAllBytes(filePath);  // "../../../etc/passwd" possible

// 🔴 VULNERABLE — partial sanitization
var path = uploadDir + "/" + fileName.Replace("..", "");  // bypass possible
```

**Suggested approach:** `Path.GetFileName()` to strip directory components, then verify
the resolved full path starts with the expected base directory.

---

## 2. Memory Leaks

### Event Handler Leaks
When an object subscribes to an event of a longer-lived object, it cannot be GC'd:

```csharp
// 🔴 LEAK — publisher outlives subscriber
publisher.SomeEvent += subscriber.HandleEvent;
// subscriber never unsubscribes → pinned in memory forever
```

Watch especially for:
- Static events (publisher lives forever)
- `Timer.Elapsed` without unsubscription
- Kafka consumer event callbacks that capture `this`

**Suggested approach:** Unsubscribe in `Dispose()`, or use weak event pattern.

### Static Collections Growing Unbounded
```csharp
// 🔴 LEAK — items added, never removed
private static List<LogEntry> _auditLog = new();
private static Dictionary<string, CachedResult> _cache = new();
private static ConcurrentDictionary<Guid, Session> _sessions = new();
```

In a long-running microservice, these grow until OOM or restart.

**Suggested approach:** Bounded cache with eviction (IMemoryCache with expiration),
or identify if this should be in Redis/external store.

### Captured Variables in Closures
```csharp
// 🔴 POTENTIAL LEAK — lambda captures 'this', keeping entire object alive
someService.OnComplete += () => this.ProcessResult();
Task.Run(() => this.HeavyOperation());
```

Problematic when the lambda is stored in a long-lived delegate, event, or collection.

**Suggested approach:** Capture only needed values, not `this`. Or unsubscribe.

### Large Object Heap (LOH) Fragmentation
```csharp
// 🟠 RISK — repeated allocation of large arrays (>85KB) in loops
while (processing) {
    var buffer = new byte[1024 * 1024];  // 1MB → LOH, not compacted by default
}
```

**Suggested approach:** `ArrayPool<T>.Shared.Rent/Return` for reusable buffers.

---

## 3. Resource Leaks

### IDisposable Not Disposed
The most common resource leak. Any IDisposable MUST be in a `using` block or disposed in finally:

```csharp
// 🔴 LEAK — connection not closed on exception
var conn = new SqlConnection(connStr);
conn.Open();
var cmd = new SqlCommand(query, conn);
var reader = cmd.ExecuteReader();
// exception here → connection stays open, not returned to pool
```

Key IDisposable types to flag:
- `SqlConnection`, `SqlCommand`, `SqlDataReader`, `SqlDataAdapter`
- `HttpClient` (special case — see Phase 4)
- `FileStream`, `StreamReader`, `StreamWriter`
- `MemoryStream` (when wrapping unmanaged resources)
- `SmtpClient`, `TcpClient`, `NetworkStream`
- Entity Framework `DbContext`
- Kafka `IProducer<>`, `IConsumer<>`

**Suggested approach:** Wrap in `using` statement/declaration. Show both classic and
C# 8+ `using var` syntax.

### File Handle Leaks
```csharp
// 🔴 LEAK — handle not released on exception
var stream = File.OpenRead(path);
var content = ReadContent(stream);  // if throws → stream stays open
stream.Close();
```

**Suggested approach:** Always `using`, never manual Close.

---

## 4. Data Corruption Risks

### Missing Transaction Scopes
Multiple related writes without a transaction — partial failure leaves inconsistent state:

```csharp
// 🔴 RISK — if second operation fails, first is already committed
repo.UpdateAccount(fromAccount);
repo.UpdateAccount(toAccount);
repo.SaveAuditLog(transfer);
```

**Suggested approach:** `TransactionScope` or EF `SaveChanges` wrapping all related changes.
In distributed (cross-service) scenarios via Kafka, mention the Outbox pattern.

### Race Conditions on Shared State
```csharp
// 🔴 RACE CONDITION — check-then-act not atomic
if (!_cache.ContainsKey(key)) {
    _cache[key] = ComputeValue(key);  // two threads can both enter
}

// 🔴 Non-atomic operations
_requestCount++;  // not thread-safe without Interlocked or lock
```

**Suggested approach:** `ConcurrentDictionary.GetOrAdd`, `Interlocked.Increment`,
or proper locking. Briefly explain why check-then-act is dangerous.

---

## 5. Critical Async Anti-Patterns

### async void
```csharp
// 🔴 CRITICAL — exceptions in async void crash the process
public async void ProcessOrder(Order order) {
    await _repository.SaveAsync(order);
}
// Exception → unobserved → UnhandledException → process terminates
```

The only acceptable use of `async void` is UI event handlers, which don't exist in
microservices. Every `async void` in a microservice is a critical finding.

**Suggested approach:** Change return type to `Task`, ensure caller awaits it.

### Fire-and-Forget Without Error Handling
```csharp
// 🔴 DANGEROUS — exception silently lost
_ = SendNotificationAsync(userId);
Task.Run(() => CleanupTempFiles());
```

**Suggested approach:** If truly fire-and-forget, wrap in try/catch with logging.
Better yet, use a background job system (Hangfire, IHostedService).
