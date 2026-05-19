# Phase 3: Optimization & Refactoring

Code that works but could work better — faster, cleaner, more maintainable. These are not urgent
fixes but improvements that reduce technical debt and prevent future bugs.

## Table of Contents
1. Performance Optimization
2. Code Quality & DRY
3. SOLID Principles
4. Refactoring Patterns
5. Modernization Opportunities

---

## 1. Performance Optimization

### String Operations in Loops
```csharp
// 🟡 SLOW — O(n²) allocations
var result = "";
foreach (var item in items) {
    result += item.Name + ", ";
}
```

**Suggested approach:** `StringBuilder` for loops, `string.Join` for simple cases.

### LINQ Misuse
```csharp
// 🟡 — multiple enumerations
var filtered = items.Where(x => x.IsActive);
var count = filtered.Count();   // enumerates
var first = filtered.First();   // enumerates again
var list = filtered.ToList();   // third time

// 🟡 — unnecessary materialization
var data = context.Users.Where(u => u.IsActive).ToList();
foreach (var user in data) { ... }  // ToList unnecessary if iterating once
```

**Suggested approach:** Materialize once when you need Count/index access. Skip
materialization when iterating once.

### Unnecessary Boxing
```csharp
// 🟡 — value type boxed to object
string text = string.Format("Count: {0}", count);  // boxes int
```

**Suggested approach:** String interpolation in modern .NET avoids boxing.

### Collection Pre-allocation
```csharp
// 🟡 — List resizes repeatedly
var list = new List<string>();
for (int i = 0; i < 10000; i++) list.Add(items[i]);
```

**Suggested approach:** `new List<string>(items.Length)` when size is known.

### Missing Caching
```csharp
// 🟡 — recomputing expensive result every call
public decimal CalculateDiscount(Order order) {
    var rules = _ruleEngine.LoadAllRules();  // DB call every time
    return rules.Apply(order);
}
```

**Suggested approach:** `IMemoryCache` with appropriate TTL, or `IDistributedCache`
for multi-instance services.

### Synchronous I/O on Hot Paths
```csharp
// 🟡 — blocks thread
var content = File.ReadAllText(path);
var response = httpClient.Send(request);
var data = dbCommand.ExecuteReader();
```

**Suggested approach:** Async equivalents (`ReadAllTextAsync`, `SendAsync`,
`ExecuteReaderAsync`) to free threads.

---

## 2. Code Quality & DRY

### Code Duplication
Look for repeated patterns:
- Same validation logic across multiple services/controllers
- Copy-pasted try/catch/log blocks
- Repeated object mapping code
- Same LINQ query with minor variations

**Suggested approach:** Depends on the duplication — could be an extension method,
base class, middleware, shared validator, or AutoMapper profile. Recommend the
abstraction that fits the specific pattern.

### Cyclomatic Complexity
Methods with deep nesting or many branches are hard to test and maintain:
```csharp
// 🟡 — deeply nested conditions
public decimal CalculatePrice(Order order) {
    if (order.Customer.IsPremium) {
        if (order.Total > 1000) {
            if (order.Items.Count > 5) {
                // buried logic
            }
        }
    }
}
```

**Suggested approach:** Guard clauses (return early), Strategy pattern, lookup
tables (`Dictionary<TKey, Func<>>`), or extract method.

### Magic Numbers and Strings
```csharp
// 🟡 — unclear intent
if (order.Status == 3) { ... }
if (retryCount > 5) { ... }
Thread.Sleep(30000);
```

**Suggested approach:** Named constants, enums, `TimeSpan.FromSeconds(30)`.

### God Classes / God Methods
Warning signs:
- Class > 300 lines with mixed responsibilities
- Constructor with 7+ dependencies
- Method > 50 lines doing multiple unrelated things
- Method name contains "And" (doing two things)
- Many parameters (> 4-5)

**Suggested approach:** Identify responsibilities and suggest how to split them.
Name the resulting classes/services.

---

## 3. SOLID Principles

### Single Responsibility Violations
```csharp
// 🟡 — service does business logic, data access, and notifications
public class OrderService {
    public void PlaceOrder(Order order) {
        ValidateOrder(order);
        _dbContext.Orders.Add(order);
        _dbContext.SaveChanges();
        SendConfirmationEmail(order);
        LogOrderPlaced(order);
    }
}
```

**Suggested approach:** Separate concerns into focused services. Use MediatR
notifications or domain events for cross-cutting reactions.

### Open/Closed Violations
```csharp
// 🟡 — adding new type requires modifying existing code
public decimal CalculateShipping(string type) {
    switch (type) {
        case "standard": return 5.99m;
        case "express": return 15.99m;
        // new type → modify this method
    }
}
```

**Suggested approach:** Interface + implementations, registered via DI.

### Dependency Inversion Violations
```csharp
// 🟡 — depends on concrete implementation
public class ReportGenerator {
    private readonly SqlReportRepository _repo = new SqlReportRepository();
}
```

**Suggested approach:** Constructor injection of `IReportRepository`.

### Interface Segregation Violations
```csharp
// 🟡 — fat interface
public interface IUserService {
    User GetUser(int id);
    void CreateUser(User user);
    void SendEmail(User user, string message);
    byte[] ExportToPdf(User user);
}
```

**Suggested approach:** Split into focused interfaces aligned with responsibilities.

---

## 4. Refactoring Patterns

### Replace Nested Conditionals with Guard Clauses
```csharp
// Before — buried logic
if (payment != null) {
    if (payment.Amount > 0) {
        if (payment.IsValid) {
            // actual logic
        }
    }
}

// Suggested approach: Guard clauses
if (payment is null) throw new ArgumentNullException(nameof(payment));
if (payment.Amount <= 0) throw new ArgumentException("...");
if (!payment.IsValid) return;
// actual logic at top level
```

### Replace Boolean Parameters
```csharp
// Before — unclear at call site
userService.GetUsers(true, false, true);

// Suggested approach: named params, enum, or separate methods
userService.GetActiveUsers(includeDeleted: false);
```

### Extract Complex Conditions
```csharp
// Before — long boolean chain
if (user.Age >= 18 && user.HasVerifiedEmail && !user.IsBanned && user.SubscriptionEndDate > DateTime.UtcNow)

// Suggested approach: encapsulate in a method
if (user.IsEligibleForPurchase())
```

---

## 5. Modernization Opportunities

Only suggest when the target framework supports them and the value is clear.

### Pattern Matching (C# 7+)
```csharp
// Before
if (obj is MyType) { var typed = (MyType)obj; typed.DoSomething(); }
// After
if (obj is MyType typed) { typed.DoSomething(); }
```

### Null-Conditional and Coalescing (C# 6+)
```csharp
// Before
var name = user != null ? user.Name : "Unknown";
// After
var name = user?.Name ?? "Unknown";
```

### Using Declarations (C# 8+)
```csharp
// Before — nested using blocks
using (var stream = File.OpenRead(path)) {
    using (var reader = new StreamReader(stream)) {
        return reader.ReadToEnd();
    }
}
// After — flat
using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream);
return reader.ReadToEnd();
```

### Records for DTOs (C# 9+ / .NET 5+)
```csharp
// Before — boilerplate class
public class UserDto { public string Name { get; set; } public string Email { get; set; } }
// After
public record UserDto(string Name, string Email);
```

### Switch Expressions (C# 8+)
```csharp
// Before
switch (status) { case OrderStatus.Pending: return "Pending"; ... }
// After
var text = status switch { OrderStatus.Pending => "Pending", _ => throw new ArgumentException() };
```

### Target-Typed New (C# 9+)
```csharp
// Before
Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();
// After
Dictionary<string, List<int>> map = new();
```
