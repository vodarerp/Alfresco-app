
using Alfresco.Contracts.Mapper;
using CA_MockData;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Migration.Infrastructure.Implementation;
using SqlServer.Infrastructure.Implementation;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;

public static class Program
{
    private static async Task Main(string[] args)
    {
        //var cfg = new ConfigureAwaitOptions {}
        Console.WriteLine("Cao svete");

        // Initialize DocumentMappingService with dependencies
        var connectionString = "Server=localhost;Database=AlfrescoMigration;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";
        var cache = new MemoryCache(new MemoryCacheOptions());

        // Create unit of work and open connection
        var uow = new SqlServerUnitOfWork(connectionString);
        await uow.BeginAsync(); // IMPORTANT: Open connection before using repository

        // Create repository and service
        // DocumentMappingRepository automatically handles CategoryMapping enrichment internally
        var repository = new DocumentMappingRepository(uow, cache);
        var documentMappingService = new DocumentMappingService(repository);

        var cfg = new Config()
        {
            BaseUrl = "http://localhost:8080/",
            Username =  "admin",
            Password = "admin",
            RootParentId = "32f14d10-59e6-4783-b14d-1059e64783f4",
            FolderCount = 10,
            DocsPerFolder = 3,
            DegreeOfParallelism = 8,
            MaxRetries = 5,
            RetryBaseDelayMs = 100,
            UseNewFolderStructure = true,           // Enable new folder structure
            ClientTypes = new[] { "PI", "LE","D" },  // NOTE: ACC dossiers are created DURING migration, not as old dossiers
            StartingCoreId = 102206,                // Start from realistic CoreId
            AddFolderProperties = true,             // Set to true after deploying bankContentModel.xml
            DocumentMappingService = documentMappingService  // Inject document mapping service
        };


        var sw = Stopwatch.StartNew();

        //var foldersCount = 10000; //promeniti da se cita iz args
        //var docPerFolder = 3; //isto

        var createdFolders = 0;
        var createdDocument = 0;
        var failed = 0;

        var start = DateTime.UtcNow;
        var totalDocs = (long)cfg.FolderCount * cfg.DocsPerFolder;
        using var http = CreateHttpClient(cfg);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Create dosie folders if using new structure
        var dosieFolders = new Dictionary<string, string>();
        if (cfg.UseNewFolderStructure)
        {
            Console.WriteLine("Creating dosie folder structure...");
            // Add DE (Deposit) to the list of client types to create folders for
            var allClientTypes = cfg.ClientTypes.Concat(new[] { "D" }).ToArray();

            foreach (var clientType in allClientTypes)
            {
                // Use correct naming: DOSSIERS-PI, DOSSIERS-LE, DOSSIERS-DE, DOSSIERS-ACC
                var dosieFolderName = $"DOSSIERS-{clientType}";
                try
                {
                    var dosieFolderId = await GetOrCreateFolderAsync(http, cfg, cfg.RootParentId, dosieFolderName, cts.Token);
                    dosieFolders[clientType] = dosieFolderId;
                    Console.WriteLine($"Dosie folder created: {dosieFolderName} (ID: {dosieFolderId})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to create dosie folder {dosieFolderName}: {ex.Message}");
                    return;
                }
            }
            Console.WriteLine($"All dosie folders ready. Starting client folder creation...\n");
        }

        var ch = Channel.CreateBounded<int>(new BoundedChannelOptions(cfg.DegreeOfParallelism * 8)
        {
            SingleWriter = true,
            SingleReader = false
        });

        var writer = Task.Run(async () =>
        {
            for (int i = 0; i < cfg.FolderCount; i++)
            {
                await ch.Writer.WriteAsync(i, cts.Token);
            }
            ch.Writer.TryComplete();
        }, cts.Token);

        var workers = Enumerable.Range(0, cfg.DegreeOfParallelism).Select(

            async wid =>
            {
                while (await ch.Reader.WaitToReadAsync(cts.Token))
                {
                    while (ch.Reader.TryRead(out var i))
                    {
                        string folderName;
                        string parentId;

                        if (cfg.UseNewFolderStructure)
                        {
                            // Distribute folders across client types
                            var clientType = cfg.ClientTypes[i % cfg.ClientTypes.Length];
                            var coreId = cfg.StartingCoreId + i;
                            // Format: Create without "-" separator
                            if (i % 4 == 0) folderName = $"LN{coreId}";
                            else folderName = $"{clientType}{coreId}"; // e.g., PI102206, LE500342, ACC13001926
                            parentId = dosieFolders[clientType]; // DOSSIERS-PI, DOSSIERS-LE, etc.
                        }
                        else
                        {
                            // Old structure: MockFolders-000001
                            folderName = $"MockFolders-{i:D6}";
                            parentId = cfg.RootParentId;
                        }

                        try
                        {
                            // Generate properties if enabled
                            Dictionary<string, object>? properties = null;
                            if (cfg.UseNewFolderStructure)
                            {
                                var clientType1 = cfg.ClientTypes[i % cfg.ClientTypes.Length];
                                var coreId1 = cfg.StartingCoreId + i;
                                properties = GenerateFolderProperties(clientType1, coreId1);
                            }

                            string folderId;
                            try
                            {
                                folderId = await CreateFolderAsync(http, cfg, parentId, folderName, cts.Token, properties);
                            }
                            catch (HttpRequestException ex) when (ex.Message.Contains("400") && properties != null)
                            {
                                // If properties failed (likely Content Model not deployed), try without properties
                                Console.WriteLine($"[WARNING] Failed to create folder with properties. Trying without properties...");
                                folderId = await CreateFolderAsync(http, cfg, parentId, folderName, cts.Token, null);
                            }

                            Interlocked.Increment(ref createdFolders);

                            var clientType = cfg.ClientTypes[i % cfg.ClientTypes.Length];
                            var coreId = cfg.StartingCoreId + i;

                            // Generate test case documents based on requirements
                            var testDocs = await GenerateTestCaseDocumentsAsync(cfg, clientType, coreId, i, cts.Token);

                            for (var x = 0; x < testDocs.Count; x++)
                            {
                                var testDoc = testDocs[x];

                                using var content = GenerateDoc(i, x, testDoc.Name);

                                try
                                {
                                    await CreateDocumentAsync(http, cfg, folderId, testDoc.Name, content, cts.Token, testDoc.Properties);
                                }
                                catch (HttpRequestException ex) when (ex.Message.Contains("400") && testDoc.Properties != null)
                                {
                                    // If properties failed, try without properties
                                    Console.WriteLine($"[WARNING] Failed to create document with properties. Trying without properties...");
                                    content.Position = 0; // Reset stream
                                    await CreateDocumentAsync(http, cfg, folderId, testDoc.Name, content, cts.Token, null);
                                }

                                Interlocked.Increment(ref createdDocument);
                            }

                            // Create separate Deposit Dossier folders for deposit documents (every 5th folder)
                            if (cfg.UseNewFolderStructure && i % 5 == 0)
                            {
                                // Generate contract number as YYYYMMDD format
                                var contractDate = DateTime.UtcNow.AddDays(-new Random(coreId).Next(1, 365));
                                var contractNumber = contractDate.ToString("yyyyMMdd");

                                // Determine typeOfProduct (5-digit format: 00008 for PI, 00010 for LE)
                                var typeOfProduct = clientType == "PI" ? "00008" : "00010";

                                // Create D folder with different naming patterns:
                                // - Variant 0: D{coreId}-{typeOfProduct}-{contractNumber} (full format)
                                // - Variant 1: D{coreId}-{typeOfProduct} (with ecm:bnkNumberOfContract)
                                // - Variant 2: D{coreId}-{contractNumber} (with ecm:bnkNumberOfContract)
                                // - Variant 3: D{coreId} (without ecm:bnkNumberOfContract)
                                var folderVariant = (i / 5) % 4; // Use i/5 to get deposit folder index, then % 4 for variant
                                string depositFolderName;
                                bool includeContractInProperty;

                                if (folderVariant == 0)
                                {
                                    // Full format: D{coreId}-{typeOfProduct}-{contractNumber}
                                    depositFolderName = $"D{coreId}-{typeOfProduct}-{contractNumber}";
                                    includeContractInProperty = true;
                                }
                                else if (folderVariant == 1)
                                {
                                    // D{coreId}-{typeOfProduct} with contract in property only
                                    depositFolderName = $"D{coreId}-{typeOfProduct}";
                                    includeContractInProperty = true;
                                }
                                else if (folderVariant == 2)
                                {
                                    // D{coreId}-{contractNumber} with contract in property
                                    // Use contractNumber in name to avoid duplicates with variant 3
                                    depositFolderName = $"D{coreId}-{contractNumber}";
                                    includeContractInProperty = true;
                                }
                                else
                                {
                                    // Minimal: D{coreId} without contract property
                                    depositFolderName = $"D{coreId}";
                                    includeContractInProperty = false;
                                }

                                var depositParentId = dosieFolders["D"]; // DOSSIERS-D folder

                                try
                                {
                                    // Generate deposit-specific properties
                                    var depositProps = GenerateDepositFolderProperties(coreId, contractNumber, clientType, typeOfProduct, includeContractInProperty);

                                    string depositFolderId;
                                    try
                                    {
                                        depositFolderId = await CreateFolderAsync(http, cfg, depositParentId, depositFolderName, cts.Token, depositProps);
                                    }
                                    catch (HttpRequestException ex) when (ex.Message.Contains("400") && depositProps != null)
                                    {
                                        Console.WriteLine($"[WARNING] Failed to create deposit folder with properties. Trying without properties...");
                                        depositFolderId = await CreateFolderAsync(http, cfg, depositParentId, depositFolderName, cts.Token, null);
                                    }

                                    Console.WriteLine($"[INFO] Created Deposit Dossier: {depositFolderName}");

                                    // Generate deposit documents
                                    var depositDocs = await GenerateDepositDocumentsAsync(cfg, clientType, coreId, contractNumber, i, cts.Token);

                                    for (var x = 0; x < depositDocs.Count; x++)
                                    {
                                        var depositDoc = depositDocs[x];
                                        using var depositContent = GenerateDoc(i, x + 1000, depositDoc.Name); // Offset to avoid duplicates

                                        try
                                        {
                                            await CreateDocumentAsync(http, cfg, depositFolderId, depositDoc.Name, depositContent, cts.Token, depositDoc.Properties);
                                        }
                                        catch (HttpRequestException ex) when (ex.Message.Contains("400") && depositDoc.Properties != null)
                                        {
                                            Console.WriteLine($"[WARNING] Failed to create deposit document with properties. Trying without properties...");
                                            depositContent.Position = 0;
                                            await CreateDocumentAsync(http, cfg, depositFolderId, depositDoc.Name, depositContent, cts.Token, null);
                                        }

                                        Interlocked.Increment(ref createdDocument);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[ERROR] Failed to create deposit dossier {depositFolderName}: {ex.Message}");
                                }
                            }

                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            Console.WriteLine($"[ERROR] w{wid} item:{i} ({folderName}): {ex.Message}");
                        }

                        if ((createdFolders + failed) % 200 == 0)
                        {
                            var elapsed = DateTime.UtcNow - start;
                            Console.WriteLine($"Progress | Folders {createdFolders}/{cfg.FolderCount} | Docs {createdDocument}/{totalDocs} | Failed {failed} | Elapsed {elapsed:hh\\:mm\\:ss}");
                        }
                    }
                }
            }).ToArray();

        await writer;
        await Task.WhenAll(workers);

        var totalElapsed = DateTime.UtcNow - start;
        Console.WriteLine($"DONE | Folders {createdFolders}/{cfg.FolderCount} | Docs {createdDocument}/{totalDocs} | Failed {failed} | Time {totalElapsed:hh\\:mm\\:ss}");

        // Cleanup resources
        await uow.DisposeAsync();
        cache.Dispose();

    }

    private static HttpClient CreateHttpClient(Config cfg)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(cfg.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.Password}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        return http;
    }


    private static async Task<string> CreateFolderAsync(
        HttpClient http,
        Config cfg,
        string parentId,
        string name,
        CancellationToken ct,
        Dictionary<string, object>? properties = null)
    {
        var url = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children";

        object payload;
        var requiredAspects = new[] { "ecm:dossierMetadata", "cm:titled" };
        if (properties != null && properties.Count > 0)
        {
            // Create payload with properties using custom ecm:clientFolder type
            payload = new
            {
                name,
                nodeType = "cm:folder",
                aspectNames = requiredAspects,
                properties
            };
        }
        else
        {
            // Simple payload without properties
            payload = new { name, nodeType = "cm:folder" };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        using var res = await SendWithRetryAsync(http, cfg, req, ct);
        using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.GetProperty("entry").GetProperty("id").GetString()!;
    }

    private static async Task<string> GetOrCreateFolderAsync(HttpClient http, Config cfg, string parentId, string name, CancellationToken ct)
    {
        // Try to get existing folder first by listing children and filtering by name
        try
        {
            var searchUrl = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children?where=(nodeType='cm:folder')";
            using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            using var searchRes = await SendWithRetryAsync(http, cfg, searchReq, ct);
            using var searchDoc = await searchRes.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);

            var entries = searchDoc!.RootElement.GetProperty("list").GetProperty("entries");

            // Search for folder with matching name
            foreach (var entry in entries.EnumerateArray())
            {
                var folderName = entry.GetProperty("entry").GetProperty("name").GetString();
                if (folderName == name)
                {
                    // Folder already exists, return its ID
                    return entry.GetProperty("entry").GetProperty("id").GetString()!;
                }
            }
        }
        catch (Exception ex)
        {
            // Folder doesn't exist or error occurred, will create it below
            Console.WriteLine($"[DEBUG] GetOrCreate search failed: {ex.Message}. Will create new folder.");
        }

        // Create folder if it doesn't exist
        return await CreateFolderAsync(http, cfg, parentId, name, ct);
    }
    private static async Task CreateDocumentAsync(
        HttpClient http,
        Config cfg,
        string parentId,
        string name,
        Stream content,
        CancellationToken ct,
        Dictionary<string, object>? properties = null)
    {
        // Step 1: Create document without properties (multipart API doesn't support properties well)
        var url = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent("cm:content", Encoding.UTF8), "nodeType");
        form.Add(new StringContent("false"), "autoRename");

        var sc = new StreamContent(content);
        sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(sc, "filedata", name);

        using var createReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        using var createRes = await SendWithRetryAsync(http, cfg, createReq, ct);

        // Read response and get document ID
        var responseContent = await createRes.Content.ReadAsStringAsync(ct);
        string? documentId = null;

        try
        {
            using var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("entry", out var entry))
            {
                if (entry.TryGetProperty("id", out var idProp))
                {
                    documentId = idProp.GetString()!;
                }
            }

            if (documentId == null)
            {
                Console.WriteLine($"[ERROR] Could not extract document ID from response:");
                Console.WriteLine($"[ERROR] Full response: {responseContent}");
                throw new Exception("Document creation returned unexpected response structure");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse document creation response:");
            Console.WriteLine($"Response: {responseContent}");
            throw new Exception($"Invalid JSON response: {ex.Message}", ex);
        }

        // Step 2: Update document with properties if provided
        if (properties != null && properties.Count > 0)
        {
            await UpdateDocumentPropertiesAsync(http, cfg, documentId, properties, ct);
        }
    }

    /// <summary>
    /// Updates document properties and aspects using the Alfresco REST API.
    /// This is done as a separate step because multipart upload doesn't reliably support properties.
    /// </summary>
    private static async Task UpdateDocumentPropertiesAsync(
        HttpClient http,
        Config cfg,
        string nodeId,
        Dictionary<string, object> properties,
        CancellationToken ct)
    {
        var url = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}";

        // Build update payload with aspects and properties
        var payload = new
        {
           aspectNames = new[] { "ecm:dossierMetadata", "cm:titled" },
            properties
        };

        using var updateReq = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(payload)
        };

        try
        {
            using var updateRes = await SendWithRetryAsync(http, cfg, updateReq, ct);
            var updateContent = await updateRes.Content.ReadAsStringAsync(ct);

            // Verify update was successful
            using var doc = JsonDocument.Parse(updateContent);
            if (!doc.RootElement.TryGetProperty("entry", out _))
            {
                Console.WriteLine($"[WARNING] Document properties update may have failed for node {nodeId}");
                Console.WriteLine($"[WARNING] Response: {updateContent}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Failed to update document properties for node {nodeId}: {ex.Message}");
            // Don't throw - document is created, just without properties
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(HttpClient http, Config cfg, HttpRequestMessage req, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            HttpResponseMessage? res = null;
            try
            {
                res = await http.SendAsync(Clone(req), HttpCompletionOption.ResponseHeadersRead, ct);

                // If 400-499 client error (not 429), don't retry - log and throw immediately
                if ((int)res.StatusCode >= 400 && (int)res.StatusCode < 500 && res.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                {
                    var errorContent = await res.Content.ReadAsStringAsync(ct);
                    Console.WriteLine($"[ERROR] HTTP {(int)res.StatusCode} Bad Request");
                    Console.WriteLine($"URL: {req.RequestUri}");
                    Console.WriteLine($"Method: {req.Method}");

                    // Try to log request content if it's JSON
                    if (req.Content != null)
                    {
                        try
                        {
                            var requestContent = await req.Content.ReadAsStringAsync();
                            Console.WriteLine($"Request Body: {requestContent}");
                        }
                        catch { }
                    }

                    Console.WriteLine($"Response: {errorContent}");
                    Console.WriteLine();

                    res.EnsureSuccessStatusCode(); // This will throw with error details
                }

                // If 5xx server error or 429, retry
                if (res.StatusCode != System.Net.HttpStatusCode.TooManyRequests && (int)res.StatusCode < 500)
                {
                    res.EnsureSuccessStatusCode();
                    return res; // success
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // per-request timeout
                if (attempt >= cfg.MaxRetries) throw;
            }
            catch (HttpRequestException ex) when (attempt < cfg.MaxRetries && !ex.Message.Contains("400"))
            {
                // will retry only for server errors, not client errors (400-499)
            }


            if (attempt >= cfg.MaxRetries)
            {
                res?.EnsureSuccessStatusCode();
                throw new Exception($"HTTP failed after {attempt + 1} attempts. Status={(int?)res?.StatusCode}");
            }


            var delay = TimeSpan.FromMilliseconds(cfg.RetryBaseDelayMs * Math.Pow(2, attempt));
            await Task.Delay(delay, ct);
        }
    }


    // NOTE: HttpRequestMessage is single-use -> clone for retries (.NET 8)
    private static HttpRequestMessage Clone(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        foreach (var h in req.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);


        if (req.Content != null)
        {
            var ms = new MemoryStream();
            req.Content.CopyToAsync(ms).GetAwaiter().GetResult();
            ms.Position = 0;
            var body = new StreamContent(ms);
            foreach (var h in req.Content.Headers)
                body.Headers.TryAddWithoutValidation(h.Key, h.Value);
            clone.Content = body;
        }
        return clone;
    }


    private static Stream GenerateDoc(int folderIndex, int docIndex, string folderName)
    {
        var text = $"Test document\n" +
                   $"Folder: {folderName}\n" +
                   $"Folder Index: {folderIndex}\n" +
                   $"Document Index: {docIndex}\n" +
                   $"Created: {DateTime.UtcNow:o}\n" +
                   $"---\n" +
                   $"This is a mock document generated for testing purposes.";
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Represents a test document with name and properties
    /// </summary>
    private class TestDocument
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, object>? Properties { get; set; }
    }

    /// <summary>
    /// Gets document type code (SifraDocMigracija) from IDocumentMappingService
    /// Uses the centralized mapping service instead of static mapper
    /// </summary>
    private static async Task<string?> GetDocumentTypeCodeAsync(Config cfg, string documentName, CancellationToken ct)
    {
        // Try to find by original name (Naziv)
        var mapping = await cfg.DocumentMappingService.FindByOriginalNameAsync(documentName, ct);

        if (mapping != null)
        {
            return mapping.SifraDokumenta;
        }

        // Try to find by Serbian name (NazivDokumenta)
        var mappingBySerbianName = await cfg.DocumentMappingService.FindBySerbianNameAsync(documentName, ct);

        if (mappingBySerbianName != null)
        {
            return mappingBySerbianName.SifraDokumenta;
        }

        // Document not found in mapper
        return null;
    }


    /// <summary>
    /// Generates test case documents based on TestCase-migracija.txt requirements.
    /// Creates a variety of documents to cover all test scenarios.
    /// Uses IDocumentMappingService for accurate names and codes.
    /// </summary>
    private static async Task<List<TestDocument>> GenerateTestCaseDocumentsAsync(Config cfg, string clientType, int coreId, int folderIndex, CancellationToken ct)
    {
        var documents = new List<TestDocument>();
        var random = new Random(coreId);

        // Track document names to avoid duplicates
        var usedFileNames = new HashSet<string>();

        // Helper function to create document using IDocumentMappingService
        async Task AddDocumentAsync(string documentName, int? versionNumber = null, bool addMigrationSuffix = false, string? customStatus = null, bool? includeAccountNumber = null)
        {
            // Get document type code from IDocumentMappingService
            var docTypeCode = await GetDocumentTypeCodeAsync(cfg, documentName, ct);

            if (string.IsNullOrEmpty(docTypeCode))
            {
                Console.WriteLine($"[WARNING] Document '{documentName}' not found in IDocumentMappingService");
                return;
            }

            // Sanitize filename: replace spaces and special characters with underscores
            var baseFileName = documentName.Replace(" ", "_").Replace("/", "_");
            var fileName = baseFileName + ".pdf";

            // If duplicate, add version number or unique suffix
            if (usedFileNames.Contains(fileName))
            {
                if (versionNumber.HasValue)
                {
                    fileName = $"{baseFileName}_v{versionNumber.Value}.pdf";
                }
                else
                {
                    // Add incremental suffix
                    int counter = 1;
                    while (usedFileNames.Contains(fileName))
                    {
                        fileName = $"{baseFileName}_{counter}.pdf";
                        counter++;
                    }
                }
            }

            usedFileNames.Add(fileName);

            // Get full mapping info from IDocumentMappingService
            var mapping = await cfg.DocumentMappingService.FindByOriginalNameAsync(documentName, ct);

            // TC 1 & 2: Determine if document should have "-migracija" suffix
            // If addMigrationSuffix is true, use NazivDokumentaMigracija, otherwise use original documentName
            string opisDokumenta;
            string tipDosiea;

            if (mapping != null)
            {
                // Use original name
                opisDokumenta = mapping.Naziv;

                tipDosiea = mapping.TipDosijea;
            }
            else
            {
                // Fallback if mapping not found
                opisDokumenta = addMigrationSuffix ? $"{documentName} - migracija" : documentName;
                tipDosiea = clientType == "PI" ? "Dosije klijenta FL" : "Dosije klijenta PL";
            }

            // Determine document status (TC 11: support for pre-existing "poništen" documents)
            string docStatus = customStatus ?? "validiran";

            var props = CreateDocumentProps(clientType, coreId, docTypeCode, opisDokumenta, tipDosiea, docStatus, random, includeAccountNumber);

            if (tipDosiea == "Dosije depozita")
            {
                var productType = clientType == "PI" ? "00008" : "00010";
                props["ecm:productType"] = productType;
                var contractDate = DateTime.UtcNow.AddDays(-new Random(coreId).Next(1, 365));
                var contractNumber = contractDate.ToString("yyyyMMdd");
                props["ecm:bnkNumberOfContract"] = contractNumber;
                //props["ecm:versionType"] = versionNumber.Value == 1 ? "Initial" : "Revision";
            }

            // Add version label if specified (TC 10: Multiple versions)
            if (versionNumber.HasValue)
            {
                props["ecm:versionLabel"] = $"{versionNumber.Value}.0";
                props["ecm:versionType"] = versionNumber.Value == 1 ? "Initial" : "Revision";
            }

            documents.Add(new TestDocument
            {
                Name = fileName,
                Properties = props
            });
        }

        // Helper to add multiple versions of the same document (TC 10)
        async Task AddDocumentWithVersionsAsync(string documentName, int versionCount, bool addMigrationSuffix = false)
        {
            for (int v = 1; v <= versionCount; v++)
            {
                await AddDocumentAsync(documentName, versionNumber: v, addMigrationSuffix: addMigrationSuffix);
            }
        }

        // Generate documents based on client type
        // NOTE: ACC dossiers are NOT generated here, they are created DURING migration process
        // However, we generate documents with TipDosiea="Dosije paket računa" that will be migrated to ACC
        if (clientType == "PI")
        {
            // Test Case 4: Dosije fizičkog lica documents (single versions)
            await AddDocumentAsync("KYC Questionnaire MDOC");
            await AddDocumentAsync("Specimen Card for Authorized Person");
            await AddDocumentAsync("Pre-Contract Info");
            await AddDocumentAsync("KYC Questionnaire for LE");
            await AddDocumentAsync("Contact Data Change Email");
            await AddDocumentAsync("Contact Data Change Phone");

            // TC 10: Add documents with multiple versions
            await AddDocumentWithVersionsAsync("Personal Notice", versionCount: 3);
            await AddDocumentWithVersionsAsync("KYC Questionnaire", versionCount: 2);
            await AddDocumentWithVersionsAsync("Communication Consent", versionCount: 3);
            await AddDocumentWithVersionsAsync("Specimen card", versionCount: 2);

            // TC 3: Add Account Package documents (will be migrated to DOSSIERS-ACC)
            // These documents have TipDosiea="Dosije paket računa"
            await AddDocumentAsync("Current Accounts Contract");
            await AddDocumentAsync("Saving Accounts Contract");
            //await AddDocumentAsync("Account Package RSD Instruction for Resident");
            await AddDocumentWithVersionsAsync("Account Package", versionCount: 2);

            // TC 1 & 2: Add documents with "-migracija" suffix (should become "poništen")
            await AddDocumentAsync("KYC Questionnaire", addMigrationSuffix: true);
            await AddDocumentAsync("Personal Notice", addMigrationSuffix: true);

            // TC 11: Add pre-existing "poništen" documents
            await AddDocumentAsync("Communication Consent", customStatus: "poništen");
            await AddDocumentAsync("Specimen card", customStatus: "poništen");

            // TC 12-14: Add KDP documents (old documents that should be marked inactive after migration)
            await AddDocumentAsync("Specimen Card for Authorized Person");  // 00099 - nova verzija policy
            await AddDocumentAsync("Specimen card for LE"); // 00101 - novi dokument policy

            // TC 15: Add exclusion document (should NOT be migrated)
           // await AddDocumentAsync("Ovlašćenje licima za donošenje instrumenata PP-a u Banku"); // 00702

            // TC 16: Add KDP 00824 documents - edge cases with/without account number
            await AddDocumentAsync("Travel Insurance");  // WITH account number - should be active
            await AddDocumentAsync("PiPonuda");  // WITH account number - should be active
            await AddDocumentAsync("PiVazeciUgovorOroceniDepozitOstaleValute", includeAccountNumber: false); // WITHOUT account number - should NOT be active
        }
        else if (clientType == "LE")
        {
            // Test Case 5: Dosije pravnog lica documents (single versions)
            await AddDocumentAsync("GDPR Revoke");
            await AddDocumentAsync("GL Transaction");
            await AddDocumentAsync("FX Transaction");
            await AddDocumentAsync("Pre-Contract Info");
            await AddDocumentAsync("Contact Data Change Email");
            await AddDocumentAsync("Contact Data Change Phone");
            await AddDocumentAsync("PiPonuda");  // WITH account number - should be active

            await AddDocumentAsync("Travel Insurance");  // WITH account number - should be active

            // TC 10: Add documents with multiple versions
            await AddDocumentWithVersionsAsync("KYC Questionnaire for LE", versionCount: 2);
            await AddDocumentWithVersionsAsync("Specimen card for LE", versionCount: 2);
            await AddDocumentWithVersionsAsync("Communication Consent", versionCount: 3);

            // TC 3: Add Account Package documents (will be migrated to DOSSIERS-ACC)
            // These documents have TipDosiea="Dosije paket računa"
            await AddDocumentAsync("Current Accounts Contract");
            await AddDocumentWithVersionsAsync("Account Package", versionCount: 2);
            await AddDocumentAsync("Prestige Package Tariff for LE");
            await AddDocumentAsync("Specimen card");

            // TC 1 & 2: Add documents with "-migracija" suffix (should become "poništen")
            await AddDocumentAsync("Communication Consent", addMigrationSuffix: true);
            await AddDocumentAsync("KYC Questionnaire for LE", addMigrationSuffix: true);

            // TC 11: Add pre-existing "poništen" documents
            await AddDocumentAsync("Specimen card for LE", customStatus: "poništen");

            // TC 12-14: Add KDP documents for LE (old documents that should be marked inactive)
           // await AddDocumentAsync("KDP za pravna lica iz aplikacije"); // 00100 - nova verzija policy

            // TC 15: Add exclusion document (should NOT be migrated)
            //await AddDocumentAsync("Ovlašćenje licima za donošenje instrumenata PP-a u Banku"); // 00702
        }

        return documents;
    }

    /// <summary>
    /// Generates deposit documents for Dosije Depozita folders.
    /// Creates documents based on client type (PI or LE).
    /// </summary>
    private static async Task<List<TestDocument>> GenerateDepositDocumentsAsync(Config cfg, string clientType, int coreId, string contractNumber, int folderIndex, CancellationToken ct)
    {
        var documents = new List<TestDocument>();
        var random = new Random(coreId);
        var usedFileNames = new HashSet<string>();

        // Helper function to create deposit document with version support (TC 22)
        async Task AddDepositDocumentAsync(string documentName, int? versionNumber = null)
        {
            var docTypeCode = await GetDocumentTypeCodeAsync(cfg, documentName, ct);

            if (string.IsNullOrEmpty(docTypeCode))
            {
                Console.WriteLine($"[WARNING] Deposit document '{documentName}' not found in IDocumentMappingService");
                return;
            }

            var baseFileName = documentName.Replace(" ", "_").Replace("/", "_");
            var fileName = baseFileName + ".pdf";

            // Add version number to filename if specified
            if (usedFileNames.Contains(fileName) || versionNumber.HasValue)
            {
                if (versionNumber.HasValue)
                {
                    fileName = $"{baseFileName}_v{versionNumber.Value}.pdf";
                }
                else
                {
                    int counter = 1;
                    while (usedFileNames.Contains(fileName))
                    {
                        fileName = $"{baseFileName}_{counter}.pdf";
                        counter++;
                    }
                }
            }

            usedFileNames.Add(fileName);

            var mapping = await cfg.DocumentMappingService.FindByOriginalNameAsync(documentName, ct);

            string opisDokumenta = mapping?.Naziv ?? documentName;
            string tipDosiea = mapping?.TipDosijea ?? "Dosije depozita";

            // Create deposit-specific properties
            var props = new Dictionary<string, object>();

            // Standard properties
            props["cm:title"] = opisDokumenta;
            props["cm:description"] = $"Deposit document {opisDokumenta} for contract {contractNumber}";

            // CRITICAL: ecm:docDesc - key for migration mapping
            props["ecm:docDesc"] = opisDokumenta;

            // Core ID
            props["ecm:coreId"] = coreId.ToString();

            // Document status - all deposit documents are "validiran" (TC 25)
            props["ecm:docStatus"] = "validiran";

            // Document type
            props["ecm:docType"] = docTypeCode;

            // Dossier type - Dosije depozita
            props["ecm:docDossierType"] = "Dosije depozita";

            // Client type
            props["ecm:docClientType"] = clientType;

            // Source - DUT for deposit documents (TC 7)
            props["ecm:source"] = "DUT";

            // Contract number - CRITICAL for deposit documents
            props["ecm:contractNumber"] = contractNumber;

            // Dates
            var creationDate = DateTime.ParseExact(contractNumber, "yyyyMMdd", null);
            props["ecm:docCreationDate"] = creationDate.ToString("o");

            // TC 22: Add version label if specified
            if (versionNumber.HasValue)
            {
                props["ecm:versionLabel"] = $"{versionNumber.Value}.0";
                props["ecm:versionType"] = versionNumber.Value == 1 ? "Initial" : "Revision";
            }

            documents.Add(new TestDocument
            {
                Name = fileName,
                Properties = props
            });
        }

        // Helper to add multiple versions of deposit document (TC 22)
        async Task AddDepositDocumentWithVersionsAsync(string documentName, int versionCount)
        {
            for (int v = 1; v <= versionCount; v++)
            {
                await AddDepositDocumentAsync(documentName, versionNumber: v);
            }
        }

        // Generate deposit documents based on client type
        if (clientType == "PI")
        {
            // TC 24: Minimum required deposit documents for PI (00008)
            // 1. Ugovor o oročenom depozitu
            await AddDepositDocumentWithVersionsAsync("PiVazeciUgovorOroceniDepozitDvojezicniRSD", versionCount: 3);

            // 2. Ponuda (REQUIRED - previously missing)
            await AddDepositDocumentAsync("PiPonuda");

            // 3. Plan isplate depozita (REQUIRED - previously missing)
            await AddDepositDocumentAsync("PiAnuitetniPlan");

            // 4. Obavezni elementi Ugovora
            await AddDepositDocumentWithVersionsAsync("PiObavezniElementiUgovora", versionCount: 2);

            // TC 22: Additional deposit documents with versions
            await AddDepositDocumentWithVersionsAsync("ZahtevZaOtvaranjeRacunaOrocenogDepozita", versionCount: 2);
        }
        else if (clientType == "LE")
        {
            // TC 24: Minimum required deposit documents for LE (00010)
            // 1. Ugovor o oročenom depozitu
            await AddDepositDocumentWithVersionsAsync("SmeUgovorOroceniDepozitPreduzetnici", versionCount: 3);

            // 2. Ponuda (REQUIRED - previously missing)
            await AddDepositDocumentAsync("SmePonuda");

            // 3. Plan isplate depozita (REQUIRED - previously missing)
            await AddDepositDocumentAsync("SmeAnuitetniPlan");

            // 4. Obavezni elementi Ugovora (REQUIRED - previously missing)
            await AddDepositDocumentWithVersionsAsync("SmeObavezniElementiUgovora", versionCount: 2);
        }

        return documents;
    }

    /// <summary>
    /// Generates properties for Deposit Dossier folders (Dosije depozita).
    /// Format: D{CoreId}-{typeOfProduct}-{contractNumber} (with optional parts)
    /// </summary>
    private static Dictionary<string, object> GenerateDepositFolderProperties(int coreId, string contractNumber, string clientType, string typeOfProduct, bool includeContractInProperty)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId);

        // Standard Content Model properties
        string title = includeContractInProperty
            ? $"Deposit Dossier {coreId} - {contractNumber}"
            : $"Deposit Dossier {coreId}";
        properties["cm:title"] = title;
        properties["cm:description"] = $"Deposit dossier for CoreId {coreId}";

        // Unique folder identifier: Always use simplified format D{coreId}
        // (The actual folder name is set separately and may include typeOfProduct and contractNumber)
        var uniqueFolderId = $"D{coreId}";
        properties["ecm:uniqueFolderId"] = uniqueFolderId;
        properties["ecm:folderId"] = uniqueFolderId;

        // Dossier type - Dosije depozita
        properties["ecm:bnkDossierType"] = "Dosije depozita";

        // Core ID (REQUIRED)
        properties["ecm:coreId"] = coreId.ToString();

        // Product type (REQUIRED)
        // ecm:bnkTypeOfProduct stores short version: "8" or "10"
        var shortProductType = typeOfProduct.TrimStart('0'); // "00008" -> "8", "00010" -> "10"
        properties["ecm:productType"] = typeOfProduct; // Full format: "00008" or "00010"
        properties["ecm:bnkTypeOfProduct"] = shortProductType; // Short format: "8" or "10"

        // Contract number - OPTIONAL (only if includeContractInProperty is true)
        if (includeContractInProperty)
        {
            properties["ecm:bnkNumberOfContract"] = contractNumber;
        }

        // Source - DUT for deposit dossiers
        properties["ecm:source"] = "DUT";
        properties["ecm:bnkSource"] = "DUT";
        properties["ecm:bnkSourceId"] = "DUT";

        // Status
        properties["ecm:docStatus"] = "ACTIVE";
        properties["ecm:bnkStatus"] = "ACTIVE";
        properties["ecm:active"] = true;

        // Client type (REQUIRED) - PI for fizička lica, LE for pravna lica
        properties["ecm:clientType"] = clientType;
        properties["ecm:bnkClientType"] = clientType; // "PI" or "LE"

        // Client name
        if (clientType == "PI")
        {
            var firstNames = new[] { "Petar", "Marko", "Ana", "Jovana", "Milan" };
            var lastNames = new[] { "Petrović", "Jovanović", "Nikolić", "Marković" };
            properties["ecm:clientName"] = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";

            // JMBG for PI
            var jmbg = (1000000000000L + random.Next(1000000000)).ToString();
            properties["ecm:jmbg"] = jmbg;
            properties["ecm:mbrJmbg"] = jmbg;
        }
        else
        {
            var companies = new[] { "Privredno Društvo", "DOO Kompanija", "AD Firma" };
            properties["ecm:clientName"] = $"{companies[random.Next(companies.Length)]} {coreId}";

            // MBR for LE
            var mbr = (10000000 + random.Next(90000000)).ToString();
            properties["ecm:mbrJmbg"] = mbr;
        }

        // Deposit processed date
        var depositDate = DateTime.ParseExact(contractNumber, "yyyyMMdd", null);
        properties["ecm:depositProcessedDate"] = depositDate.ToString("o");

        // Creation date
        properties["ecm:datumKreiranja"] = DateTime.UtcNow.ToString("o");

        // Segment
        properties["ecm:segment"] = clientType == "PI" ? "Retail" : "Corporate";
        properties["ecm:bnkClientType"] = properties["ecm:segment"];

        // Office ID
        properties["ecm:bnkOfficeId"] = $"OPU-{random.Next(100, 999)}";

        // Residence
        var residency = random.Next(2) == 0 ? "Resident" : "Non-resident";
        properties["ecm:residency"] = residency;
        properties["ecm:bnkResidence"] = residency;

        // Creator
        properties["ecm:creator"] = "DUT Migration System";
        properties["ecm:kreiraoId"] = random.Next(1000, 9999).ToString();

        // Staff indicator
        var staffValue = random.Next(2) == 0 ? "Y" : "N";
        properties["ecm:staff"] = staffValue;
        properties["ecm:docStaff"] = staffValue;

        return properties;
    }

    /// <summary>
    /// Helper method to create document properties with common fields
    /// Uses data from HeimdallDocumentMapper for accurate mapping
    /// </summary>
    private static Dictionary<string, object> CreateDocumentProps(
        string clientType,
        int coreId,
        string docTypeCode,
        string docTypeName,
        string tipDosijea,
        string docStatus,
        Random random,
        bool? includeAccountNumber = null)
    {
        var properties = new Dictionary<string, object>();

        // Standard properties
        properties["cm:title"] = docTypeName;
        properties["cm:description"] = $"Test document {docTypeName} for {clientType} client {coreId}";

        // CRITICAL: ecm:docDesc - ključ za mapiranje u migraciji
        // Ovo polje se koristi u DocumentStatusDetector.GetMigrationInfoByDocDesc()
        properties["ecm:docDesc"] = docTypeName;

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();

        // Document status (Test Cases 1-2)
        properties["ecm:docStatus"] = docStatus;

        // Document type (ecm:tipDokumenta)
        properties["ecm:docType"] = docTypeCode;

        // Dossier type (Test Cases 3-5)
        // This comes from TipDosiea field in HeimdallDocumentMapper
        properties["ecm:docDossierType"] = tipDosijea;

        // Client segment (CRITICAL for migration)
        properties["ecm:docClientType"] = clientType;

        // Source (will be set by migration, but can add for reference)
        // Note: Migration will set this based on destination folder
        properties["ecm:source"] = "Heimdall";

        // Dates
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(1, 365));
        properties["ecm:docCreationDate"] = creationDate.ToString("o");

        // TC 16: KDP 00824 edge case - account number validation
        // If includeAccountNumber is explicitly set, respect it; otherwise use default logic
        if (docTypeCode == "00824")
        {
            if (includeAccountNumber == true)
            {
                // Include account number - document should be active after migration
                properties["ecm:contractNumber"] = $"{coreId}{random.Next(100, 999)}";
            }
            else if (includeAccountNumber == false)
            {
                // Explicitly exclude account number - document should NOT be active
                // Do not add ecm:contractNumber property
            }
            else
            {
                // Default behavior: add account number if status is validiran
                if (docStatus == "validiran")
                {
                    properties["ecm:contractNumber"] = $"{coreId}{random.Next(100, 999)}";
                }
            }
        }

        return properties;
    }

    /// <summary>
    /// Generates custom properties for a client folder (dossier) based on client type and CoreId.
    /// Properties follow banking content model: ecm:propertyName
    /// ONLY generates required properties for migration testing
    /// </summary>
    private static Dictionary<string, object> GenerateFolderProperties(string clientType, int coreId)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId); // Seed with coreId for consistent mock data

        // Standard Content Model (cm:) properties - built-in Alfresco properties
        properties["cm:title"] = $"{clientType} Client {coreId}";
        properties["cm:description"] = $"Mock dossier for {clientType} client with CoreId {coreId}";

        // ========================================
        // REQUIRED PROPERTIES FOR MIGRATION
        // Based on provided list
        // ========================================

        // 1. ecm:coreId
        properties["ecm:coreId"] = coreId.ToString();

        // 2. ecm:jmbg / ecm:mbrJmbg (bnkJmbg)
        if (clientType == "LE")
        {
            var mbr = (10000000 + random.Next(90000000)).ToString(); // MBR (8 digits)
            properties["ecm:mbrJmbg"] = mbr;
            properties["ecm:jmbg"] = mbr;
        }
        else if (clientType == "PI")
        {
            var jmbg = (1000000000000L + random.Next(1000000000)).ToString(); // JMBG (13 digits)
            properties["ecm:mbrJmbg"] = jmbg;
            properties["ecm:jmbg"] = jmbg;
        }

        // 3. ecm:clientName (bnkClientName)
        if (clientType == "LE")
        {
            var companies = new[] { "Privredno Društvo", "DOO Kompanija", "AD Firma", "JP Preduzeće", "OD Organizacija" };
            properties["ecm:clientName"] = $"{companies[random.Next(companies.Length)]} {coreId}";
        }
        else // PI
        {
            var firstNames = new[] { "Petar", "Marko", "Ana", "Jovana", "Milan", "Nikola", "Stefan", "Milica" };
            var lastNames = new[] { "Petrović", "Jovanović", "Nikolić", "Marković", "Đorđević", "Stojanović" };
            properties["ecm:clientName"] = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";
        }

        // 4. ecm:bnkClientType (segment from ClientAPI)
        var segments = new[] { "Retail", "Corporate", "SME", "Premium", "Standard" };
        properties["ecm:bnkClientType"] = segments[random.Next(segments.Length)];

        // 5. ecm:clientSubtype (clientSubType from ClientAPI)
        if (clientType == "LE")
        {
            var subtypes = new[] { "SME", "Corporate", "Public Sector", "Non-Profit" };
            properties["ecm:clientSubtype"] = subtypes[random.Next(subtypes.Length)];
        }
        else if (clientType == "PI")
        {
            var subtypes = new[] { "Retail", "Premium", "Private Banking", "Standard" };
            properties["ecm:clientSubtype"] = subtypes[random.Next(subtypes.Length)];
        }

        // 6. ecm:bnkOfficeId (barCLEXOpu from ClientAPI)
        properties["ecm:bnkOfficeId"] = $"OPU-{random.Next(100, 999)}";

        // 7. ecm:staff / ecm:docStaff (staff from ClientAPI)
        var staffIndicators = new[] { "Y", "N", "" };
        var staffValue = staffIndicators[random.Next(staffIndicators.Length)];
        properties["ecm:staff"] = staffValue;
        properties["ecm:docStaff"] = staffValue;

        // 8. ecm:bnkTypeOfProduct
        properties["ecm:bnkTypeOfProduct"] = clientType == "LE" ? "00010" : "00008";

        // 9. ecm:bnkAccountNumber (keeping for compatibility)
        properties["ecm:bnkAccountNumber"] = $"{coreId}{random.Next(100, 999)}";

        // 10. ecm:barclex (barCLEXGroupCode + barCLEXGroupName from ClientAPI)
        var barclexGroupCode = $"BXG{random.Next(100, 999)}";
        var barclexGroupName = $"Group-{random.Next(1, 20)}";
        properties["ecm:barclex"] = $"{barclexGroupCode} - {barclexGroupName}";

        // 11. ecm:collaborator (barCLEXCode + barCLEXName from ClientAPI - mapped as contributor)
        var barclexCode = $"BXC{random.Next(1000, 9999)}";
        var barclexName = $"Collaborator-{random.Next(1, 50)}";
        properties["ecm:collaborator"] = $"{barclexCode} - {barclexName}";

        // 12. ecm:bnkSourceId / ecm:source
        var source = "Heimdall";
        properties["ecm:bnkSourceId"] = source;
        properties["ecm:source"] = source;

        // 13. ecm:opuRealization (bnkRealizationOPUID)
        properties["ecm:opuRealization"] = $"OPU-{random.Next(100, 999)}/ID-{random.Next(1000, 9999)}";

        // 14. ecm:contractNumber / ecm:bnkNumberOfContract
        var contractNumber = $"{coreId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        properties["ecm:contractNumber"] = contractNumber;
        properties["ecm:bnkNumberOfContract"] = contractNumber;

        // 15. ecm:residency / ecm:bnkResidence
        var residencies = new[] { "Resident", "Non-resident" };
        var residency = residencies[random.Next(residencies.Length)];
        properties["ecm:residency"] = residency;
        properties["ecm:bnkResidence"] = residency;

        // 16. ecm:bnkSource
        properties["ecm:bnkSource"] = source;

        // 17. ecm:docStatus / ecm:bnkStatus
        properties["ecm:docStatus"] = "ACTIVE";
        properties["ecm:bnkStatus"] = "ACTIVE";

        // 18. ecm:bnkDossierType
        string dossierType = clientType == "PI" ? "Dosije klijenta FL" : "Dosije klijenta PL";
        properties["ecm:bnkDossierType"] = dossierType;

        // Additional properties for compatibility
        properties["ecm:clientType"] = clientType;
        properties["ecm:segment"] = properties["ecm:bnkClientType"]; // Same as bnkClientType
        properties["ecm:productType"] = properties["ecm:bnkTypeOfProduct"];

        // Unique folder identifier
        string uniqueFolderId = clientType == "PI" ? $"PI{coreId}" : $"LE{coreId}";
        properties["ecm:uniqueFolderId"] = uniqueFolderId;
        properties["ecm:folderId"] = uniqueFolderId;

        // Creation date
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(30, 730));
        properties["ecm:datumKreiranja"] = creationDate.ToString("o");

        // Active flag
        properties["ecm:active"] = true;

        return properties;
    }
    
}