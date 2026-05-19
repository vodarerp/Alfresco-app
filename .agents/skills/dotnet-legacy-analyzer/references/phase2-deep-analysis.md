# Phase 2: Deep Analysis

Issues that may not cause immediate production failures but represent bugs waiting to surface —
usually under load, edge cases, or after code changes.

## Table of Contents
1. Bug Patterns
2. Async/Await Issues
3. Thread Safety
4. Exception Handling
5. Database Access Patterns

---

## 1. Bug Patterns

### Null Reference Risks
```csharp
// 🟠 RISK — no null check before access
var user = _repository.GetUser(id);
var email = user.Email;  // NullReferenceException if not found

// 🟠 RISK — FirstOrDefault returns null
var item = list.FirstOrDefault(x => x.Id == targetId);
item.Process();  // boom if not found

// 🟠 RISK — dictionary access without TryGetValue
var value = _dict[key];  // KeyNotFoundException
```

**Suggested approach:** Null checks, `TryGetValue`, null-conditional operators, or
nullable reference types if project supports them.

### String Comparison Issues
```csharp
// 🟠 BUG — culture-sensitive comparison (Turkish 'i' problem)
if (status == "Active")

// 🟠 BUG — case mismatch
if (userInput == "yes")  // "Yes", "YES" won't match
```

**Suggested approach:** `string.Equals` with `StringComparison.OrdinalIgnoreCase`.

### Off-by-One and Boundary Errors
```csharp
// 🟠 — common off-by-one
for (int i = 0; i <= array.Length; i++)  // should be <

// 🟠 — empty collection not handled
var first = collection.First();  // throws if empty
var max = values.Max();           // throws on empty sequence
```

**Suggested approach:** Use `FirstOrDefault`, validate collection before operations.

### Incorrect Equality
```csharp
// 🟠 — reference equality instead of value equality for reference types
if (obj1 == obj2)

// 🟠 — floating point comparison
if (amount == 0.1 + 0.2)  // false due to precision
```

**Suggested approach:** `decimal` for financial values, epsilon comparison for floats,
override `Equals` or use `IEquatable<T>`.

### DateTime Pitfalls
```csharp
// 🟠 — DateTime.Now in server code
var created = DateTime.Now;  // server's local time, inconsistent across instances

// 🟠 — comparing DateTimes with different Kinds
if (utcDate == localDate)  // meaningless comparison
```

**Suggested approach:** `DateTime.UtcNow` consistently across all services.

### Integer Overflow
```csharp
// 🟡 — unchecked arithmetic on user-controlled values
int total = quantity * price;  // silent overflow possible
long fileSize = width * height * bytesPerPixel;  // int multiplication before long assignment
```

**Suggested approach:** Cast to `long` before multiplication, or use `checked` context.

---

## 2. Async/Await Issues

### Sync-over-Async (Deadlock Risk)
```csharp
// 🟠 DEADLOCK RISK — blocking on async code
var result = GetDataAsync().Result;
var result = GetDataAsync().GetAwaiter().GetResult();
GetDataAsync().Wait();
```

These capture the SynchronizationContext and block it. While ASP.NET Core doesn't have a
SyncContext, this is still a thread-pool starvation risk and a bug if the code ever runs
in a context that does have one.

**Suggested approach:** Async all the way up. If sync caller truly cannot be changed,
explain the `Task.Run` wrapper as last resort.

### Missing ConfigureAwait in Library/Shared Code
```csharp
// 🟡 — in shared libraries or NuGet packages
public async Task<Data> GetDataAsync() {
    var raw = await _httpClient.GetAsync(url);  // captures context
    return Parse(raw);
}
```

In ASP.NET Core services this is harmless, but if the code is in a shared library that could
be consumed by WPF/WinForms, it matters.

**Suggested approach:** `ConfigureAwait(false)` in library code. Note when it's not needed.

### Async Lambda Pitfalls
```csharp
// 🟠 BUG — async void lambda
list.ForEach(async item => {
    await ProcessAsync(item);  // this is async void!
});

// 🟠 BUG — not awaited
var tasks = items.Select(async item => await ProcessAsync(item));
// IEnumerable<Task> — nothing awaited yet
```

**Suggested approach:** `Select` + `Task.WhenAll` for parallel, or sequential `foreach`
with `await`.

### Unnecessary Async State Machine
```csharp
// 🟡 OVERHEAD — async keyword unnecessary
public async Task<int> GetCountAsync() {
    return await _repository.CountAsync();  // just pass Task through
}
```

**Suggested approach:** Elide async/await when the only await is the return. Note the
exception-behavior caveat.

---

## 3. Thread Safety

### Improper Locking
```csharp
// 🟠 — locking on public/shared objects
lock (this) { ... }              // anyone with a reference can deadlock
lock (typeof(MyClass)) { ... }   // global lock
lock ("myLock") { ... }          // string interning makes this global
```

**Suggested approach:** Private `readonly object _lock = new()`.

### Non-Thread-Safe Collections
```csharp
// 🟠 — Dictionary is not thread-safe
private Dictionary<string, int> _cache = new();
// Multiple threads reading and writing → corrupted state, infinite loops possible
```

**Suggested approach:** `ConcurrentDictionary`, or `lock` around all access.

### Double-Check Locking Done Wrong
```csharp
// 🟠 — field not volatile, partial initialization visible
if (_instance == null) {
    lock (_lock) {
        if (_instance == null) {
            _instance = new Singleton();
        }
    }
}
```

**Suggested approach:** `Lazy<T>` — simpler and correct by construction.

### Timer Callback Reentrancy
```csharp
// 🟠 — System.Timers.Timer callbacks can overlap
var timer = new System.Timers.Timer(1000);
timer.Elapsed += (s, e) => {
    ProcessBatch();  // if takes >1s, callbacks overlap
};
```

**Suggested approach:** Stop timer during processing, or use `lock` / `SemaphoreSlim`.

---

## 4. Exception Handling

### Swallowed Exceptions
```csharp
// 🟠 BAD — exception completely hidden
try { riskyOperation(); }
catch (Exception) { }

try { riskyOperation(); }
catch (Exception ex) { /* TODO */ }
```

**Suggested approach:** At minimum log the exception. If intentionally ignoring, add a
comment explaining why.

### Catching Too Broadly
```csharp
// 🟠 — catches everything including OOM, StackOverflow
try { ... }
catch (Exception ex) {
    return defaultValue;
}
```

**Suggested approach:** Catch specific exceptions. Use `when` filter for conditional handling.

### Stack Trace Destruction
```csharp
// 🟠 BUG — stack trace lost
catch (Exception ex) {
    _logger.LogError(ex.Message);
    throw ex;  // WRONG — resets stack trace
}
```

**Suggested approach:** `throw;` (bare) preserves the original stack trace.

### Throwing Base Exception
```csharp
// 🟡 — non-specific exception type
throw new Exception("Something went wrong");
```

**Suggested approach:** Use or create specific exception types that carry context.

### Exception in Finally/Dispose
```csharp
// 🟠 — exception in finally hides original
try { riskyOperation(); }
finally {
    anotherRiskyOperation();  // if both throw, original lost
}
```

**Suggested approach:** Wrap finally body in try/catch, log secondary exception.

---

## 5. Database Access Patterns

### N+1 Query Problem
```csharp
// 🟠 PERFORMANCE BUG — lazy loading triggers N+1
var orders = context.Orders.ToList();
foreach (var order in orders) {
    var items = order.OrderItems;  // 1 query per order
}
```

**Suggested approach:** `.Include()` for eager loading. Mention projection as alternative.

### Connection Management
```csharp
// 🟠 LEAK — connection not returned to pool
var conn = new SqlConnection(connStr);
conn.Open();
var dt = ExecuteQuery(conn);  // exception → leak
conn.Close();
```

**Suggested approach:** `using` ensures return to pool even on exception.

### Unbounded Queries
```csharp
// 🟠 RISK — no limit, loads entire table
var allUsers = context.Users.ToList();
var results = context.Orders.Where(o => o.Status == "Active").ToList();
```

**Suggested approach:** Always paginate (`Skip/Take`) or apply `TOP`/`LIMIT`.

### Dynamic SQL Without Parameters
```csharp
// 🟠 — building WHERE clauses with concatenation
var where = "WHERE 1=1";
if (hasFilter) where += $" AND Name LIKE '%{name}%'";
cmd.CommandText = "SELECT * FROM Products " + where;
```

**Suggested approach:** Build parameter list alongside the WHERE clause.

### DbContext Lifetime Issues
```csharp
// 🟠 — DbContext as singleton (EF is not thread-safe)
services.AddSingleton<MyDbContext>();

// 🟠 — DbContext kept alive too long → stale data, tracking bloat
```

**Suggested approach:** Scoped lifetime (default for `AddDbContext`), explain why.
