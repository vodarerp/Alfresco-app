# CLAUDE.md - AI Assistant Guide for Alfresco Migration Application

**Last Updated:** 2025-11-14
**Project:** Alfresco Document Migration System
**Technology:** .NET 8.0 / WPF Desktop Application

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Architecture & Structure](#architecture--structure)
3. [Technology Stack](#technology-stack)
4. [Project Dependencies](#project-dependencies)
5. [Development Workflow](#development-workflow)
6. [Coding Conventions](#coding-conventions)
7. [Configuration Management](#configuration-management)
8. [Migration Business Rules](#migration-business-rules)
9. [External API Integration](#external-api-integration)
10. [Database Schema](#database-schema)
11. [Testing Strategy](#testing-strategy)
12. [Common Tasks](#common-tasks)
13. [Documentation Reference](#documentation-reference)
14. [Troubleshooting](#troubleshooting)

---

## Project Overview

### Purpose
This is an enterprise-grade document migration system designed to migrate documents and folders from an old Alfresco Document Management System to a new Alfresco instance. The application handles complex business rules, data enrichment from external APIs, and maintains data integrity throughout the migration process.

### Key Features
- **Automated Migration**: Batch processing of folders and documents
- **Data Enrichment**: Integration with ClientAPI and DUT API for metadata enrichment
- **Complex Business Rules**: Support for various document types, versioning, and status management
- **Staging Tables**: Oracle database staging for tracking migration progress
- **Activity Status Determination**: Intelligent document lifecycle management
- **WPF Desktop Interface**: User-friendly GUI for migration management
- **Mock Services**: Built-in mock APIs for development and testing

### Domain Context
- **FL**: Fizička Lica (Physical/Individual Persons)
- **PL**: Pravna Lica (Legal Entities/Business Clients)
- **KDP**: Kartica Deponovanog Potpisa (Deposited Signature Card)
- **DUT**: Deposit Understanding/Processing API
- **Dosije**: Client dossier/folder containing documents

---

## Architecture & Structure

### Solution Structure

```
Alfresco.sln
├── Alfresco.App/                    # WPF Desktop Application (Main Entry Point)
├── Alfresco.Client/                 # Alfresco REST API Client
├── Alfresco.Abstraction/            # Core Abstractions/Interfaces
├── Alfresco.Contracts/              # Shared DTOs, Models, Enums
│   ├── Models/
│   ├── Request/
│   ├── Response/
│   ├── Enums/
│   ├── Options/
│   ├── Oracle/Models/
│   ├── SqlServer/
│   ├── Mapper/
│   └── Extensions/
├── Migration.Infrastructure/        # Core Migration Logic
│   ├── Implementation/
│   │   ├── Services/               # Migration services
│   │   ├── Document/               # Document migration handlers
│   │   ├── Folder/                 # Folder migration handlers
│   │   ├── Alfresco/               # Alfresco-specific operations
│   │   └── Move/                   # Document moving logic
│   ├── Extensions/
│   └── PostMigration/              # Post-migration transformations
├── Migration.Workers/               # Background Workers
│   ├── FolderDiscoveryWorker.cs
│   ├── DocumentDiscoveryWorker.cs
│   └── MoveWorker.cs
├── Migration.Abstraction/           # Migration Interfaces
├── Migration.Extensions/            # Helper Extensions
├── Oracle.Infrastructure/           # Oracle Database Access Layer
├── Oracle.Abstraction/              # Oracle Abstractions
├── SqlServer.Infrastructure/        # SQL Server Access Layer
├── SqlServer.Abstraction/           # SQL Server Abstractions
├── Mapper/                          # Data Mapping Logic
├── MockClientAPI/                   # Mock Client API (Testing)
├── CA_MockData/                     # Mock Data Generator
├── SQL/                             # SQL Scripts
└── SQL_Scripts/                     # Additional SQL Resources
```

### Architectural Patterns

**Clean Architecture**
- **Abstraction Layer**: Interfaces and contracts isolated from implementation
- **Infrastructure Layer**: Database access, external API clients, concrete implementations
- **Application Layer**: Business logic, migration services, workers
- **Presentation Layer**: WPF desktop application

**Repository Pattern**
- Database operations abstracted through repository interfaces
- Used for Oracle and SQL Server data access

**Worker Pattern**
- Background workers for long-running migration tasks
- `FolderDiscoveryWorker`, `DocumentDiscoveryWorker`, `MoveWorker`

**Dependency Injection**
- Microsoft.Extensions.DependencyInjection used throughout
- Services registered in `App.xaml.cs` or startup configuration

---

## Technology Stack

### Core Technologies
- **.NET 8.0**: Target framework
- **WPF (Windows Presentation Foundation)**: Desktop UI
- **C# 12**: Primary language with nullable reference types enabled
- **ModernWpfUI**: Modern UI components library

### Data Access
- **Oracle.ManagedDataAccess.Core**: Oracle database connectivity
- **Dapper**: Lightweight ORM for database operations
- **Microsoft.Data.SqlClient**: SQL Server connectivity (alternative/future support)

### External APIs
- **Alfresco REST API**: Document management operations
- **ClientAPI**: Client metadata enrichment
- **DUT API**: Deposit/offer validation and enrichment

### Libraries & Frameworks
- **Polly**: Resilience and transient fault handling (retries, circuit breakers)
- **Newtonsoft.Json**: JSON serialization/deserialization
- **Microsoft.Extensions.Hosting**: Generic host for background services
- **Microsoft.Extensions.Logging**: Logging abstraction
- **log4net**: Logging implementation
- **Serilog**: Alternative structured logging (configured in appsettings)

### Health Checks
- **AspNetCore.HealthChecks.Oracle**: Oracle health monitoring
- **AspNetCore.HealthChecks.SqlServer**: SQL Server health monitoring
- **AspNetCore.HealthChecks.Uris**: External API health checks

---

## Project Dependencies

### Project Reference Graph

```
Alfresco.App
    ├── Alfresco.Client
    ├── Alfresco.Contracts
    ├── Mapper
    ├── Migration.Extensions
    ├── Migration.Infrastructure
    ├── Migration.Workers
    ├── Oracle.Infrastructure
    ├── SqlServer.Abstraction
    └── SqlServer.Infrastructure

Migration.Infrastructure
    ├── Migration.Abstraction
    ├── Alfresco.Abstraction
    └── Alfresco.Contracts

Alfresco.Client
    ├── Alfresco.Abstraction
    └── Alfresco.Contracts

Oracle.Infrastructure
    ├── Oracle.Abstraction
    └── Alfresco.Contracts

SqlServer.Infrastructure
    ├── SqlServer.Abstraction
    └── Alfresco.Contracts
```

### Key NuGet Packages
- `Oracle.ManagedDataAccess.Core` (23.9.1)
- `Dapper` (2.1.66)
- `Polly` (8.6.3) + `Polly.Extensions.Http` (3.0.0)
- `ModernWpfUI` (0.9.6)
- `Newtonsoft.Json` (13.0.3)
- `Microsoft.Extensions.Hosting` (9.0.8)
- `log4net` (3.2.0)

---

## Development Workflow

### Getting Started

1. **Prerequisites**
   - Visual Studio 2022 (17.14+) or JetBrains Rider
   - .NET 8.0 SDK
   - Oracle Database access (for staging tables)
   - Alfresco instance (or mock services)

2. **Configuration Setup**
   ```bash
   # Copy example configuration
   cp appsettings.Example.json Alfresco.App/appsettings.Development.json

   # Update with actual values
   # - Connection strings
   # - Alfresco API URLs
   # - ClientAPI configuration
   # - DUT API configuration
   ```

3. **Building the Solution**
   ```bash
   dotnet restore
   dotnet build Alfresco.sln
   ```

4. **Running the Application**
   ```bash
   cd Alfresco.App
   dotnet run
   ```

### Git Workflow

- **Main Branch**: `main` (or master) - production-ready code
- **Feature Branches**: `claude/feature-name` or standard naming
- **Commit Convention**: Descriptive messages, Serbian language acceptable
- **Pull Requests**: Required for merging to main branch

### Branch Naming for Claude
When working with Claude Code, branches should:
- Start with `claude/`
- End with session ID (automatically managed)
- Example: `claude/claude-md-mhz0avqvk920aafr-012pQtWKXmX4pyUfmt4KKkrp`

---

## Coding Conventions

### General Guidelines

1. **Nullable Reference Types**: Enabled project-wide
   ```csharp
   #nullable enable
   ```
   - Always handle potential null values
   - Use `?` for nullable types explicitly
   - Use null-forgiving operator `!` sparingly and only when certain

2. **Async/Await Pattern**
   - All I/O operations should be async
   - Use `async Task` or `async Task<T>` return types
   - Suffix async methods with `Async`
   ```csharp
   public async Task<Document> GetDocumentAsync(string id, CancellationToken ct)
   {
       return await _repository.GetByIdAsync(id, ct);
   }
   ```

3. **Dependency Injection**
   - Constructor injection preferred
   - Use interfaces for dependencies
   - Register services in DI container
   ```csharp
   public class MigrationService : IMigrationService
   {
       private readonly IDocumentRepository _docRepository;
       private readonly ILogger<MigrationService> _logger;

       public MigrationService(
           IDocumentRepository docRepository,
           ILogger<MigrationService> logger)
       {
           _docRepository = docRepository;
           _logger = logger;
       }
   }
   ```

4. **Error Handling**
   - Use try-catch for expected exceptions
   - Log exceptions with context
   - Use Polly for transient fault handling
   - Don't swallow exceptions silently
   ```csharp
   try
   {
       await ProcessDocumentAsync(doc, ct);
   }
   catch (AlfrescoApiException ex)
   {
       _logger.LogError(ex, "Failed to process document {DocumentId}", doc.Id);
       throw;
   }
   ```

5. **Logging**
   - Use structured logging
   - Include relevant context (IDs, counts, etc.)
   - Use appropriate log levels
   ```csharp
   _logger.LogInformation("Processing batch of {Count} documents", documents.Count);
   _logger.LogWarning("Document {DocId} missing metadata: {Property}", id, prop);
   _logger.LogError(ex, "Migration failed for folder {FolderId}", folderId);
   ```

### Naming Conventions

- **Classes/Interfaces**: PascalCase
  - Interfaces prefixed with `I`: `IDocumentService`
  - Abstract classes can use `Base` suffix: `MigrationWorkerBase`

- **Methods**: PascalCase
  - Async methods end with `Async`: `MigrateDocumentAsync`

- **Properties**: PascalCase
  - Public: `public string DocumentId { get; set; }`
  - Private fields: `_camelCase` with underscore prefix

- **Local Variables/Parameters**: camelCase
  - Descriptive names preferred: `documentId`, not `docId` (unless widely used)

- **Constants**: UPPER_SNAKE_CASE or PascalCase
  ```csharp
  private const int MAX_RETRY_COUNT = 3;
  private const string DEFAULT_FOLDER_TYPE = "cm:folder";
  ```

### File Organization

- One class per file (generally)
- File name matches class name
- Related classes can be in same file if small (e.g., DTOs)
- Group using statements, remove unused ones

---

## Configuration Management

### Configuration Files

**appsettings.json** (Main Application)
```
Alfresco.App/appsettings.json
```

**appsettings.Example.json** (Template)
```
/appsettings.Example.json (root level)
```

**log4net.config** (Logging)
```
Alfresco.App/log4net.config
```

### Configuration Structure

Key configuration sections:
- `ConnectionStrings`: Database connections (Oracle, SQL Server)
- `AlfrescoRead`: Old Alfresco instance settings
- `AlfrescoWrite`: New Alfresco instance settings
- `ClientApi`: ClientAPI configuration
- `DutApi`: DUT API configuration
- `MigrationOptions`: Migration-specific settings
- `Logging`: Log levels and output configuration
- `Serilog`: Structured logging configuration

### Sensitive Data Management

**DO NOT commit secrets to git!**

Use one of:
1. **User Secrets** (Development)
   ```bash
   dotnet user-secrets set "ConnectionStrings:OracleConnection" "your-connection-string"
   ```

2. **Environment Variables** (Production)
   ```bash
   export ConnectionStrings__OracleConnection="your-connection-string"
   ```

3. **Azure Key Vault** (Enterprise)
   - Configure Key Vault in production environment
   - Reference secrets via configuration

4. **Local appsettings.Development.json** (Git-ignored)
   - Copy from `appsettings.Example.json`
   - Update with local values
   - Never commit to git

### Configuration Access Pattern

```csharp
// Register options
services.Configure<MigrationOptions>(configuration.GetSection("MigrationOptions"));

// Inject and use
public class MigrationService
{
    private readonly MigrationOptions _options;

    public MigrationService(IOptions<MigrationOptions> options)
    {
        _options = options.Value;
    }
}
```

---

## Migration Business Rules

### Document Status Rules

**TC1: Documents with "-migracija" suffix → INACTIVE**
- Documents mapped with "-migracija" suffix in document name
- Status in Alfresco: **"poništen"** (cancelled/inactive)
- Examples: "Specimen card-migracija", "Account Package-migracija"

**TC2: Documents WITHOUT "-migracija" suffix → ACTIVE**
- Documents without migration suffix
- Status in Alfresco: **"validiran"** (validated/active)
- Examples: "Current Accounts Contract", "Pre-Contract Info"

### Folder Type Rules (Dosije Types)

**TC3: Dosije paket računa (Account Package Dossier) → 300**
- Folder type: "Dosije paket računa"
- Target folder ID: 300
- Examples: Account-related documents

**TC4: Dosije klijenta FL/PL → 500 or 400**
- "Dosije klijenta FL/PL" maps to:
  - **500** for FL (Fizička Lica - Individual)
  - **400** for PL (Pravna Lica - Legal Entity)
- Determined by client type from ClientAPI

**TC5: Dosije klijenta PL → 400**
- "Dosije klijenta PL" (without FL) always maps to **400**

### Source System Rules

**TC6: Source = "Heimdall"**
- Folder types: "Dosije paket računa", "Dosije fizičkog lica", "Dosije pravnog lica", "Dosije ostalo"
- Alfresco field `izvor` = "Heimdall"

**TC7: Source = "DUT"**
- Folder type: "Dosije depozita" (Deposit dossier)
- Alfresco field `izvor` = "DUT"

### Folder Creation Rule

**TC8: Create folder if not exists**
- Check if target folder exists in new Alfresco
- If not exists:
  1. Create appropriate client folder
  2. Enrich with ClientAPI data
  3. Migrate documents to created folder
- Always migrate document attributes from old Alfresco

### KDP Document Rules (Special Handling)

**Kartica Deponovanog Potpisa (Deposited Signature Card)**

Document Types:
- `00099`: KDP FL (final)
- `00824`: KDP FL (migration)
- `00101`: KDP authorized FL (final)
- `00825`: KDP authorized FL (migration)
- `00100`: KDP PL (final)
- `00827`: KDP PL (migration)

Special Rules:
- **Activity Status**: Only the latest KDP document per account should be active
- **Account Validation**: Requires validation via ClientAPI
- **Comparison**: By original creation date
- **Account Numbers**: Must be enriched from ClientAPI

### Document Versioning

Per documentation (line 168-170 of ANALIZA_MIGRACIJE.md):
- **Unsigned documents**: Version `1.1`
- **Signed documents**: Version `1.2`

### Document Type Transformations

Post-migration transformations (from `MigrationOptions.DocumentTypeMappings`):
```
00824 → 00099  (KDP FL: migration → final)
00825 → 00101  (KDP authorized FL: migration → final)
00827 → 00100  (KDP PL: migration → final)
00841 → 00130  (KYC questionnaire)
```

### Deposit Folder Rules

**Unique Identifiers** (line 156-163):
- Format: `DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}`
- Reference: `DE-{CoreId}{ProductType}-{ContractNumber}`

**Product Types**:
- `00008`: FL deposits
- `00010`: PL deposits

**DUT Validation**:
- Only "Booked" status deposits are valid
- Must validate via DUT API before migration

---

## External API Integration

### Alfresco REST API

**Old Alfresco (Read)**
- Purpose: Source system for migration
- Configuration: `AlfrescoRead` section
- Authentication: Basic Auth (Username/Password)

**New Alfresco (Write)**
- Purpose: Target system for migration
- Configuration: `AlfrescoWrite` section
- Authentication: Basic Auth (Username/Password)

**Common Operations**:
```csharp
// Get node
await alfrescoClient.GetNodeAsync(nodeId, cancellationToken);

// Create folder
await alfrescoClient.CreateFolderAsync(parentId, folderRequest, cancellationToken);

// Upload document
await alfrescoClient.UploadDocumentAsync(folderId, documentRequest, cancellationToken);

// Update metadata
await alfrescoClient.UpdateNodeAsync(nodeId, metadata, cancellationToken);
```

### ClientAPI

**Purpose**: Client metadata enrichment

**Configuration**: `ClientApi` section in appsettings

**Endpoints**:
1. `GetClientDetail/{coreId}` - Get client information
2. `GetActiveAccounts/{coreId}` - Get active accounts for KDP validation
3. `ValidateClient/{coreId}` - Check if client exists

**Usage Pattern**:
```csharp
// Enrich folder with client data
var clientData = await clientApi.GetClientDetailAsync(coreId, cancellationToken);
folder.FirstName = clientData.FirstName;
folder.LastName = clientData.LastName;
folder.Email = clientData.Email;

// Validate account for KDP document
var accounts = await clientApi.GetActiveAccountsAsync(coreId, cancellationToken);
var isValid = accounts.Any(a => a.AccountNumber == document.AccountNumber);
```

**Retry Configuration**:
- Polly retry policy configured
- Retry count: 3 (default)
- Exponential backoff

**Caching**:
- Optional client data caching
- Cache duration: 60 minutes (default)
- Reduces API load during bulk migration

### DUT API

**Purpose**: Deposit/offer validation and enrichment

**Configuration**: `DutApi` section in appsettings

**Endpoints**:
1. `GetOffers` - Get offers by criteria
2. `GetOfferDetails/{offerId}` - Get specific offer
3. `GetOfferDocuments/{offerId}` - Get offer documents

**Usage Pattern**:
```csharp
// Get booked deposits
var offers = await dutApi.GetOffersAsync(new OfferRequest
{
    Status = "Booked",
    CoreId = coreId
}, cancellationToken);

// Validate deposit folder
var depositFolder = CreateDepositFolder(offer);
depositFolder.UniqueIdentifier = GenerateDepositIdentifier(offer);
```

**Status Filter**: Only "Booked" offers should be migrated

### Resilience & Fault Handling

**Polly Policies** (configured per API):
```csharp
// Retry policy
services.AddHttpClient<IClientApiClient, ClientApiClient>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}
```

**Timeout Configuration**:
- Default: 30 seconds per API call
- Configurable in `appsettings.json`

---

## Database Schema

### Oracle Staging Tables

**DOC_STAGING** - Document staging table
- Tracks document migration progress
- Stores document metadata before/after migration
- Key columns:
  - `DOC_ID`: Unique document identifier
  - `OLD_ALFRESCO_ID`: ID in old system
  - `NEW_ALFRESCO_ID`: ID in new system (after migration)
  - `CORE_ID`: Client identifier
  - `DOCUMENT_TYPE`: Document type code
  - `STATUS`: Migration status
  - `SOURCE`: Source system (Heimdall, DUT, etc.)
  - `IS_ACTIVE`: Activity status flag
  - `MIGRATION_DATE`: When migrated
  - `ERROR_MESSAGE`: Error details if failed

**FOLDER_STAGING** - Folder staging table
- Tracks folder/dosije migration
- Stores folder structure and metadata
- Key columns:
  - `FOLDER_ID`: Unique folder identifier
  - `OLD_ALFRESCO_ID`: ID in old system
  - `NEW_ALFRESCO_ID`: ID in new system (after migration)
  - `CORE_ID`: Client identifier
  - `FOLDER_TYPE`: Dosije type (300, 400, 500, etc.)
  - `PARENT_FOLDER_ID`: Parent folder reference
  - `STATUS`: Migration status
  - `CREATED_DATE`: Creation timestamp
  - `ENRICHMENT_STATUS`: ClientAPI enrichment status

**MIGRATION_LOGS** - Migration activity log (if Serilog Oracle sink enabled)
- Structured logging to database
- Tracks all migration operations
- Query for troubleshooting

### Connection String Format

**Oracle**:
```
Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=hostname)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=SERVICENAME)));User Id=USERNAME;Password=PASSWORD;
```

**SQL Server** (if used):
```
Server=hostname;Database=dbname;User Id=username;Password=password;
```

### Database Access Pattern

**Dapper Usage**:
```csharp
using (var connection = new OracleConnection(_connectionString))
{
    await connection.OpenAsync(cancellationToken);

    var documents = await connection.QueryAsync<DocumentStaging>(
        "SELECT * FROM DOC_STAGING WHERE STATUS = :Status",
        new { Status = "Pending" });
}
```

**Bulk Operations**:
- Use batch inserts for performance
- Batch size: 1000 (configurable)
- Oracle array binding supported

---

## Testing Strategy

### Mock Services

**MockClientAPI**
- Location: `/MockClientAPI/`
- Purpose: Simulate ClientAPI responses
- Port: 5000 (HTTP) / 5001 (HTTPS)
- Endpoints mirror real ClientAPI
- Contains mock data for common test scenarios

**CA_MockData**
- Location: `/CA_MockData/`
- Purpose: Generate mock client data
- Used by MockClientAPI

**Running Mock Services**:
```bash
cd MockClientAPI
dotnet run
```

### Test Data

**Test CoreIDs** (from documentation):
- `13001926` - FL client with account package
- `13000667` - FL client with specimen card
- `50034220` - PL client (Legal Entity)
- `50034141` - PL client (Legal Entity)
- `102206` - FL client with KYC questionnaire

**Test Scenarios** (see `TestCase-migracija.txt` and `Test Case - novi Alfresco migracija.docx`):
- Refer to `ANALIZA_MIGRACIJE.md` for detailed test cases
- Migration status determination
- Folder type mapping
- Source system assignment
- Document versioning

### Integration Testing

1. **API Health Checks**:
   - Oracle database connectivity
   - Alfresco API availability
   - ClientAPI availability
   - DUT API availability

2. **Migration Validation**:
   - Compare staging table counts
   - Verify metadata mapping
   - Check document content integrity
   - Validate folder structure

---

## Common Tasks

### Task 1: Add New Document Type Mapping

1. Update `appsettings.json`:
```json
"MigrationOptions": {
  "DocumentTypeMappings": {
    "00824": "00099",
    "NEW_MIGRATION_TYPE": "NEW_FINAL_TYPE"
  }
}
```

2. Update mapper logic if needed:
```csharp
// In Mapper project or Migration.Infrastructure
public string GetFinalDocumentType(string migrationType)
{
    return _options.DocumentTypeMappings.TryGetValue(migrationType, out var finalType)
        ? finalType
        : migrationType;
}
```

### Task 2: Add New Client Enrichment Field

1. Update DTO in `Alfresco.Contracts/Models/`:
```csharp
public class ClientData
{
    public string CoreId { get; set; }
    public string FirstName { get; set; }
    public string NewField { get; set; } // Add new field
}
```

2. Update enrichment service in `Migration.Infrastructure/Implementation/Services/`:
```csharp
public async Task EnrichFolderAsync(Folder folder, CancellationToken ct)
{
    var clientData = await _clientApi.GetClientDetailAsync(folder.CoreId, ct);
    folder.FirstName = clientData.FirstName;
    folder.NewField = clientData.NewField; // Map new field
}
```

3. Update Alfresco metadata mapping

### Task 3: Modify Migration Business Rule

1. Locate the relevant service:
   - Document rules: `Migration.Infrastructure/Implementation/Document/`
   - Folder rules: `Migration.Infrastructure/Implementation/Folder/`
   - Activity status: `Migration.Infrastructure/Implementation/Services/`

2. Update the business logic:
```csharp
public bool DetermineActivityStatus(Document document)
{
    // Existing logic
    if (document.Name.Contains("-migracija"))
        return false; // Inactive

    // New rule
    if (document.Type == "NEW_TYPE" && document.Date < cutoffDate)
        return false;

    return true; // Active
}
```

3. Update tests and documentation

### Task 4: Add New Worker

1. Create worker class in `Migration.Workers/`:
```csharp
public class NewMigrationWorker : BackgroundService
{
    private readonly ILogger<NewMigrationWorker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public NewMigrationWorker(
        ILogger<NewMigrationWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IMigrationService>();

            await service.ProcessBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

2. Register in DI container (in `App.xaml.cs` or startup):
```csharp
services.AddHostedService<NewMigrationWorker>();
```

### Task 5: Update Alfresco API Client

1. Add new endpoint to `Alfresco.Abstraction/IAlfrescoClient.cs`:
```csharp
Task<Response> NewOperationAsync(Request request, CancellationToken ct);
```

2. Implement in `Alfresco.Client/AlfrescoClient.cs`:
```csharp
public async Task<Response> NewOperationAsync(Request request, CancellationToken ct)
{
    var url = $"{_baseUrl}/api/-default-/public/alfresco/versions/1/new-operation";
    var response = await _httpClient.PostAsJsonAsync(url, request, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<Response>(ct);
}
```

---

## Documentation Reference

### Key Documentation Files

**Architecture & Design**:
- `ANALIZA_MIGRACIJE.md` - Migration analysis and test cases
- `ARCHITECTURE_FIX_SUMMARY.md` - Architecture fixes summary
- `Analiza_migracije_v2.md` - Migration analysis v2

**Implementation Guides**:
- `IMPLEMENTATION_SUMMARY.md` - Implementation overview
- `MIGRATION_IMPLEMENTATION_CHANGELOG.md` - Implementation changelog
- `README_IMPLEMENTATION.md` - Implementation README
- `IMPLEMENTACIJA_PARENT_FOLDERA.md` - Parent folder implementation

**Integration Guides**:
- `ClientAPI_Integration_Guide.md` - ClientAPI integration
- `ClientAPI_Database_Mapping.md` - Database mapping
- `ClientAPI_FolderEnrichment_Guide.md` - Folder enrichment
- `ClientAPI_Analiza_Za_Migraciju.md` - ClientAPI analysis
- `INTEGRATION_INSTRUCTIONS.md` - General integration instructions

**Migration Rules**:
- `MIGRATION_RULES_V2.md` - Migration rules v2
- `MIGRATION_SERVICES_DOCUMENTATION.md` - Services documentation
- `MIGRATION_FROM_HEIMDALL_MAPPER.md` - Heimdall mapper migration

**Folder Structure**:
- `FOLDER_STRUCTURE_GUIDE.md` - Folder structure guide
- `FOLDER_STRUCTURE_CHANGES.md` - Structure changes
- `FOLDER_COUNT_SETUP.md` - Folder count setup
- `ACC_FOLDER_NAMING_FIX.md` - Account folder naming fixes

**Performance & Optimization**:
- `PERFORMANCE_OPTIMIZATION.md` - Performance optimization
- `PERFORMANCE_TUNING.md` - Performance tuning

**API Documentation**:
- `Mock_ClientAPI_DOKUMENTACIJA.md` - Mock ClientAPI docs
- `ClientAPI - swagger.json` - Swagger/OpenAPI spec

**Post-Migration**:
- `POSTMIGRATION_CLI_EXAMPLE.md` - Post-migration CLI examples
- `SERVICE_MAPPING_UPDATES.md` - Service mapping updates

**Testing**:
- `TestCase-migracija.txt` - Test cases (text)
- `Test Case - novi Alfresco migracija.docx` - Test cases (Word doc)
- `MISSING_DOCUMENTS_ANALYSIS.md` - Missing documents analysis
- `DEBUG_REPORT.md` - Debug report

**Refactoring**:
- `REFACTORING_PLAN_ClientAPI_Integration.md` - Refactoring plan
- `CHANGES_SUMMARY_V2.md` - Changes summary v2

**PDF Documentation**:
- `Migracija_Dokumentacija.pdf` - Migration documentation (PDF)
- `Migracija_dokumentacija.txt` - Migration documentation (text)

### Documentation Reading Priority

For new AI assistants or developers, read in this order:
1. **This file (CLAUDE.md)** - Overview and guidelines
2. **ANALIZA_MIGRACIJE.md** - Business rules and test cases
3. **appsettings.Example.json** - Configuration reference
4. **IMPLEMENTATION_SUMMARY.md** - Implementation overview
5. **MIGRATION_SERVICES_DOCUMENTATION.md** - Service details
6. **ClientAPI_Integration_Guide.md** - External API integration
7. **Specific guides** as needed for tasks

---

## Troubleshooting

### Common Issues

**Issue 1: Oracle Connection Failed**
```
Error: ORA-12154: TNS:could not resolve the connect identifier specified
```
Solution:
- Check connection string format
- Verify Oracle client installation
- Test with Oracle SQL Developer or sqlplus
- Check network connectivity to Oracle server

**Issue 2: Alfresco API Authentication Failed**
```
Error: 401 Unauthorized
```
Solution:
- Verify username/password in `AlfrescoRead`/`AlfrescoWrite` config
- Check if credentials are base64 encoded correctly
- Test with curl or Postman:
  ```bash
  curl -u username:password https://alfresco-url/api/-default-/public/alfresco/versions/1/nodes/-root-
  ```

**Issue 3: ClientAPI Timeout**
```
Error: HttpClient request timeout after 30 seconds
```
Solution:
- Increase timeout in configuration:
  ```json
  "ClientApi": {
    "TimeoutSeconds": 60
  }
  ```
- Check ClientAPI service availability
- Review Polly retry configuration

**Issue 4: Document Migration Stuck**
```
Documents remain in "Pending" status in DOC_STAGING
```
Solution:
- Check worker logs for errors
- Verify Alfresco API connectivity
- Check staging table for error messages:
  ```sql
  SELECT * FROM DOC_STAGING WHERE STATUS = 'Error' ORDER BY MIGRATION_DATE DESC;
  ```
- Review document validation rules

**Issue 5: Memory Issues with Large Batches**
```
OutOfMemoryException during batch processing
```
Solution:
- Reduce batch size in configuration:
  ```json
  "MigrationOptions": {
    "DocumentBatchSize": 25,
    "FolderBatchSize": 50
  }
  ```
- Enable memory throttling
- Process in smaller chunks

**Issue 6: WPF Application Won't Start**
```
Error: "Cannot find appsettings.json"
```
Solution:
- Ensure `appsettings.json` has "Copy to Output Directory: PreserveNewest"
- Check `.csproj` file for ItemGroup configuration
- Copy file manually to bin/Debug or bin/Release

### Debugging Tips

1. **Enable Detailed Logging**:
```json
"Logging": {
  "LogLevel": {
    "Default": "Debug",
    "Migration": "Trace",
    "DbLogger": "Debug"
  }
}
```

2. **Check Logs**:
- Console output (if running from terminal)
- `Logs/migration-YYYYMMDD.log`
- `Logs/errors-YYYYMMDD.log`
- Oracle `MIGRATION_LOGS` table

3. **Use Breakpoints**:
- Set breakpoints in worker `ExecuteAsync` methods
- Break on exceptions in Visual Studio
- Watch variables for staging table data

4. **SQL Debugging**:
```sql
-- Check migration progress
SELECT STATUS, COUNT(*) FROM DOC_STAGING GROUP BY STATUS;

-- Find recent errors
SELECT * FROM DOC_STAGING WHERE STATUS = 'Error' AND MIGRATION_DATE > SYSDATE - 1;

-- Check folder creation
SELECT * FROM FOLDER_STAGING WHERE NEW_ALFRESCO_ID IS NULL;
```

5. **API Testing**:
```bash
# Test Alfresco
curl -u user:pass https://alfresco/api/-default-/public/alfresco/versions/1/nodes/-root-

# Test ClientAPI (via MockClientAPI)
curl http://localhost:5000/api/Client/GetClientDetail/13001926

# Test DUT API
curl http://localhost:5000/api/Dut/GetOffers?status=Booked
```

### Performance Troubleshooting

**Slow Migration**:
1. Check database indexes on staging tables
2. Optimize batch sizes
3. Increase parallelism (carefully):
   ```json
   "MigrationOptions": {
     "MaxParallelism": 4
   }
   ```
4. Review Alfresco API response times
5. Check network latency to external APIs

**High Memory Usage**:
1. Reduce batch sizes
2. Enable memory throttling
3. Profile with dotMemory or ANTS Memory Profiler
4. Check for memory leaks in services (proper disposal)

### Getting Help

1. **Check Documentation**: Review relevant .md files in repository
2. **Check Logs**: Always review logs for error details
3. **Check Staging Tables**: SQL queries can reveal migration state
4. **Test Isolation**: Use MockClientAPI for isolated testing
5. **Ask Questions**: Provide error logs, configuration (sanitized), and steps to reproduce

---

## Working with AI Assistants (Claude)

### Best Practices for AI-Assisted Development

1. **Provide Context**:
   - Reference this CLAUDE.md file
   - Mention specific business rules from ANALIZA_MIGRACIJE.md
   - Include relevant error logs or stack traces

2. **Be Specific**:
   - "Add field X to ClientData DTO and map it in enrichment service"
   - Not: "Update client stuff"

3. **Reference Documentation**:
   - "Per TC1 in ANALIZA_MIGRACIJE.md, documents with -migracija suffix should be inactive"
   - "Following the pattern in ClientAPI_Integration_Guide.md"

4. **Test Incrementally**:
   - Request small changes
   - Test after each change
   - Commit working code before next change

5. **Configuration Safety**:
   - Never commit real credentials
   - Use appsettings.Example.json as template
   - Sanitize connection strings before sharing

### Code Review Checklist for AI

When reviewing AI-generated code:
- [ ] Follows async/await pattern correctly
- [ ] Uses dependency injection
- [ ] Includes proper error handling and logging
- [ ] Handles nullable reference types
- [ ] Uses CancellationToken for async operations
- [ ] Follows naming conventions (PascalCase, camelCase, etc.)
- [ ] Includes XML documentation comments for public APIs
- [ ] No hardcoded values (use configuration)
- [ ] Proper disposal of resources (using statements, IDisposable)
- [ ] Security: No SQL injection, XSS, or other vulnerabilities

### Common AI Assistant Tasks

**Safe tasks** (low risk):
- Reading and analyzing code
- Explaining business logic
- Writing documentation
- Suggesting optimizations
- Creating test data
- Generating SQL queries for analysis

**Moderate tasks** (review carefully):
- Adding new fields/properties
- Creating new DTOs/models
- Adding configuration options
- Writing new service methods
- Updating mappers

**High-risk tasks** (extra caution):
- Modifying database schemas
- Changing authentication/authorization
- Updating critical business rules
- Modifying transaction handling
- Changing migration workers

---

## Version History

### Document Changelog

- **2025-11-14**: Initial CLAUDE.md creation
  - Comprehensive codebase documentation
  - Architecture overview
  - Development workflows
  - Business rules reference
  - Configuration management
  - Troubleshooting guide

---

## Quick Reference

### Project Commands

```bash
# Build solution
dotnet build Alfresco.sln

# Run main application
cd Alfresco.App && dotnet run

# Run mock ClientAPI
cd MockClientAPI && dotnet run

# Run tests (if test project exists)
dotnet test

# Restore dependencies
dotnet restore

# Clean build artifacts
dotnet clean
```

### Important Paths

- Main App: `Alfresco.App/`
- Configuration: `Alfresco.App/appsettings.json`
- Logs: `Alfresco.App/bin/Debug/net8.0-windows/Logs/`
- Documentation: `*.md` files in root
- SQL Scripts: `SQL/` and `SQL_Scripts/`

### Key Files

- `Alfresco.App/App.xaml.cs` - Application startup & DI setup
- `Migration.Workers/FolderDiscoveryWorker.cs` - Folder migration worker
- `Migration.Workers/DocumentDiscoveryWorker.cs` - Document migration worker
- `Migration.Infrastructure/Implementation/Services/` - Core services
- `appsettings.Example.json` - Configuration template

### External Resources

- **Alfresco REST API Docs**: https://api-explorer.alfresco.com/api-explorer/
- **.NET 8 Docs**: https://learn.microsoft.com/en-us/dotnet/
- **Polly Docs**: https://www.pollydocs.org/
- **Dapper Docs**: https://github.com/DapperLib/Dapper

---

**End of CLAUDE.md**

*For updates to this document, please commit changes with a clear message and update the Version History section.*
