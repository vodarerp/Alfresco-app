# Phase 4: Microservice & Infrastructure

Assessment of microservice-specific concerns — inter-service communication, operational readiness,
and production hygiene. This phase is conditional: some sections only apply if certain patterns
are detected in the codebase.

## Table of Contents
1. Kafka Communication Patterns
2. External HTTP Client Usage (conditional)
3. API Contract Quality
4. Resiliency Patterns
5. Logging & Observability
6. Configuration Management

---

## 1. Kafka Communication Patterns

Only apply this section if Kafka/Confluent libraries are detected in the project
(`Confluent.Kafka`, `KafkaNet`, or MassTransit with Kafka transport).

### Producer Error Handling
```csharp
// 🟠 — fire-and-forget produce, no delivery confirmation
producer.Produce(topic, message);
// If broker is down or topic doesn't exist → message silently lost

// 🟠 — async produce without error callback
await producer.ProduceAsync(topic, message);
// Exception not caught → unobserved task exception
```

**Suggested approach:** Use the `DeliveryReport` callback or await `ProduceAsync`
with proper try/catch. Log failed deliveries with enough context to retry or
investigate.

### Consumer Idempotency
If the same message is delivered twice (at-least-once semantics), will the consumer
produce incorrect results?

```csharp
// 🟠 RISK — non-idempotent consumer
public void HandleOrderCreated(OrderCreatedEvent evt) {
    _repo.Insert(new Order { Id = Guid.NewGuid(), ... });
    // Duplicate message → duplicate order
}
```

**Suggested approach:** Use the event's natural key (not a new GUID) for deduplication.
Check-before-insert, or use upsert/`INSERT ... ON CONFLICT DO NOTHING`.

### Dead Letter Handling
What happens when a message can't be processed after retries?

```csharp
// 🟠 RISK — poison message blocks consumer forever
while (true) {
    var msg = consumer.Consume();
    ProcessMessage(msg);  // throws every time → infinite retry loop
}
```

**Suggested approach:** Retry with backoff (2-3 attempts), then publish to a Dead
Letter Topic. Log the failure with the original message payload for investigation.

### Offset Commit Strategy
```csharp
// 🟠 RISK — auto-commit enabled (default), message processed after commit
// If processing fails after auto-commit → message lost

// 🟠 RISK — manual commit before processing complete
consumer.Commit();
ProcessMessage(msg);  // if this fails → message already committed, lost
```

**Suggested approach:** Manual commit AFTER successful processing. Set
`EnableAutoCommit = false` in consumer config.

### Consumer Dispose / Graceful Shutdown
```csharp
// 🟠 — consumer not properly closed on shutdown
// Causes rebalance delays for the consumer group
```

**Suggested approach:** `IHostApplicationLifetime` cancellation token,
`consumer.Close()` in shutdown handler.

---

## 2. External HTTP Client Usage

**Only apply this section if `HttpClient`, `IHttpClientFactory`, or `RestSharp` usage is
detected.** If all inter-service communication goes through Kafka, skip this section entirely.

### Socket Exhaustion
```csharp
// 🔴 — new HttpClient per request
using (var client = new HttpClient()) {
    var result = await client.GetAsync(url);
}
// Socket not released immediately → port exhaustion under load
```

**Suggested approach:** `IHttpClientFactory` via DI, or static/singleton `HttpClient`.

### Missing Timeouts
```csharp
// 🟠 — default timeout is 100 seconds
var client = new HttpClient();
var result = await client.GetAsync(url);
// If external service hangs → thread blocked for 100s
```

**Suggested approach:** Set explicit `Timeout` on HttpClient. Consider per-request
`CancellationToken` with timeout.

### No Retry / No Circuit Breaker
```csharp
// 🟠 — single attempt, no resilience
var response = await _httpClient.GetAsync(externalApiUrl);
if (!response.IsSuccessStatusCode)
    throw new Exception("External API failed");
// Transient failure → immediate error to caller
```

**Suggested approach:** Recommend Polly for retry with exponential backoff and
circuit breaker. Show the `AddHttpClient().AddPolicyHandler()` DI pattern.

### Response Not Validated
```csharp
// 🟡 — trusting external response blindly
var data = await response.Content.ReadAsStringAsync();
var result = JsonConvert.DeserializeObject<ExternalDto>(data);
result.Process();  // no null check, no schema validation
```

**Suggested approach:** Validate response status, null-check deserialized result,
handle unexpected schemas gracefully.

---

## 3. API Contract Quality

### Missing Request Validation
```csharp
// 🟡 — no validation on incoming DTOs
[HttpPost]
public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto) {
    _service.Create(dto);  // dto.Amount could be -1, dto.Name could be null
    return Ok();
}
```

**Suggested approach:** FluentValidation or DataAnnotations. Validate at the API
boundary, not deep in business logic.

### Inconsistent Status Codes
Watch for:
- POST returning 200 instead of 201
- Not found returning 200 with null body instead of 404
- Validation errors returning 500 instead of 400/422
- Catch-all returning 200 with error message in body

**Suggested approach:** Consistent use of `Ok()`, `Created()`, `NotFound()`,
`BadRequest()`. Standardized error response model.

### Internal Models Exposed via API
```csharp
// 🟡 — leaking EF entity through API response
[HttpGet("{id}")]
public async Task<IActionResult> GetOrder(int id) {
    var order = await _context.Orders.FindAsync(id);
    return Ok(order);  // exposes DB schema, navigation properties, etc.
}
```

**Suggested approach:** Separate API DTOs from domain/EF models. Map explicitly.

### Missing Error Response Standardization
Different endpoints return errors in different shapes — some as string, some as
`{ error: "..." }`, some as `ProblemDetails`.

**Suggested approach:** Use `ProblemDetails` (RFC 7807) consistently. Global
exception middleware that maps exceptions to standard error responses.

---

## 4. Resiliency Patterns

### Missing Health Checks
If the service has no health check endpoint, orchestrators (K8s, Docker) can't
determine if it's healthy and ready to receive traffic.

```csharp
// 🟡 — no health checks registered
// Kubernetes readiness/liveness probes have nothing to call
```

**Suggested approach:** `AddHealthChecks()` with checks for DB connectivity,
Kafka connectivity, and downstream dependencies.

### No Graceful Degradation
When a dependency (DB, Kafka, external API) is down, does the service crash
or return useful fallback responses?

```csharp
// 🟡 — hard dependency, no fallback
public async Task<OrderStatus> GetStatus(int orderId) {
    var order = await _dbContext.Orders.FindAsync(orderId);
    return order.Status;  // DB down → 500 to caller
}
```

**Suggested approach:** Try/catch with fallback behavior, cached last-known-good
values, or degraded response that communicates partial availability.

---

## 5. Logging & Observability

### Inconsistent Logging
Watch for mixed approaches within the same project:
```csharp
Console.WriteLine("Error: " + ex.Message);            // console
Debug.WriteLine("Processing " + id);                   // debug only
Trace.TraceError("Failed");                            // trace
_logger.LogError(ex, "Payment failed");                // proper
EventLog.WriteEntry("MyApp", "Started");               // Windows Event Log
```

**Suggested approach:** Unified `ILogger<T>` usage throughout. Structured logging
with named parameters: `_logger.LogError(ex, "Failed for {OrderId}", orderId)`.

### Sensitive Data in Logs
Flag logging of:
- Passwords, tokens, API keys
- Credit card numbers, CVVs
- Personal data (JMBG, email without masking)
- Connection strings with credentials
- Full request/response bodies containing PII
- Kafka message payloads with sensitive data

```csharp
// 🔴 — sensitive data logged
_logger.LogInformation("Login: {User} {Password}", username, password);
_logger.LogDebug("Kafka message: {Body}", JsonConvert.SerializeObject(message));
```

**Suggested approach:** Never log credentials. Mask PII. Log only metadata (endpoint,
content length, correlation ID) not full payloads.

### Missing Logging on Critical Paths
Verify these are logged:
- Application startup/shutdown
- Authentication/authorization failures
- Database connection failures
- Kafka producer/consumer failures
- External API call failures with response status
- Business-critical state transitions
- Unhandled exceptions (global middleware)

### Missing Correlation / Request Tracing
- Is there a correlation ID propagated across service boundaries?
- Is the correlation ID included in Kafka message headers?
- Can you trace a request from API entry → Kafka → consumer → DB?

**Suggested approach:** Middleware that generates/reads `X-Correlation-ID`, propagates
it through `ILogger` scopes and Kafka headers.

---

## 6. Configuration Management

### Hardcoded Values
```csharp
// 🟡 — should be configurable
var timeout = 30;
var maxRetries = 3;
var apiUrl = "https://api.example.com/v1";
var kafkaBroker = "kafka-prod:9092";
var batchSize = 100;
```

**Suggested approach:** `IOptions<T>` / `IConfiguration` binding. Show the options
pattern briefly.

### Missing Configuration Validation
```csharp
// 🟡 — app starts with invalid config, fails at runtime
var connStr = _configuration.GetConnectionString("Default");
// Could be null, empty, or malformed — no startup validation
```

**Suggested approach:** `ValidateDataAnnotations()` + `ValidateOnStart()` on
options registration. Fail fast at startup, not at first request.

### Secrets in Source Control
Check for:
- `appsettings.json` / `appsettings.Production.json` with real credentials
- Kafka SASL passwords in config files
- API keys in constants classes
- `.env` files committed to repository

**Suggested approach:** Environment variables, User Secrets (development),
Azure Key Vault / HashiCorp Vault (production). Never commit secrets.

### Environment Parity
- Are there config values that only work in development?
- Is the same Docker image deployed everywhere (with config differences only)?
- Are Kafka topics, consumer groups, and broker addresses configurable per environment?

**Suggested approach:** Single artifact, environment-specific config via env vars
or mounted config files.
