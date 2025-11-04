
using CA_MockData;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

public static class Program
{
    private static async Task Main(string[] args)
    {
        //var cfg = new ConfigureAwaitOptions {}
        Console.WriteLine("Cao svete");

        var cfg = new Config()
        {
            BaseUrl = "http://localhost:8080/",
            Username =  "admin",
            Password = "admin",
            RootParentId = "9932a531-53d9-4fdf-b2a5-3153d94fdf29",
            FolderCount = 10,
            DocsPerFolder = 3,
            DegreeOfParallelism = 8,
            MaxRetries = 5,
            RetryBaseDelayMs = 100,
            UseNewFolderStructure = true,           // Enable new folder structure
            ClientTypes = new[] { "PI", "LE" },  // NOTE: ACC dossiers are created DURING migration, not as old dossiers
            StartingCoreId = 102206,                // Start from realistic CoreId
            AddFolderProperties = true             // Set to true after deploying bankContentModel.xml
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
            foreach (var clientType in cfg.ClientTypes)
            {
                // Use correct naming: DOSSIERS-PI, DOSSIERS-LE, DOSSIERS-ACC
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
                            // IMPORTANT: Create OLD format WITH "-" for migration testing
                            folderName = $"{clientType}-{coreId}"; // e.g., PI-102206, LE-500342, ACC-13001926
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
                            var testDocs = GenerateTestCaseDocuments(clientType, coreId, i);

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
    /// Document name to document type code mapping from Analiza_migracije_v2.md
    /// Uses exact ecm:opisDokumenta -> ecm:tipDokumenta mappings
    /// </summary>
    private static readonly Dictionary<string, string?> DocumentTypeMapping = new()
    {
        // ENGLESKI OPISI (iz starog Alfresca) - from Analiza_migracije_v2.md
        ["Personal Notice"] = "00849",
        ["KYC Questionnaire"] = "00841",
        ["KYC Questionnaire MDOC"] = "00841",
        ["KYC Questionnaire for LE"] = "00841",
        ["Communication Consent"] = "00842",
        ["Specimen card"] = "00824",
        ["Specimen card for LE"] = "00827",
        ["Specimen Card for Authorized Person"] = "00825",
        ["Account Package"] = "00834",
        ["Account Package RSD Instruction for Resident"] = "00834",
        ["Pre-Contract Info"] = "00838",
        ["GL Transaction"] = "00844",
        ["SMS info modify request"] = "00835",
        ["SMS card alarm change"] = "00836",
        ["FX Transaction"] = "00843",
        ["GDPR Revoke"] = "00840",
        ["Contact Data Change Email"] = "00847",
        ["Contact Data Change Phone"] = "00846",
        ["Current Accounts Contract"] = "00110",
        ["Current Account Contract for LE"] = "00110",
        ["Current Accounts Contract for LE"] = "00117",

        // DEPOSIT DOKUMENTI
        ["Ugovor o oročenom depozitu"] = "00008",
        ["Ponuda"] = "00889",
        ["Plan isplate depozita"] = "00879",
        ["Obavezni elementi Ugovora"] = "00882",
        ["PiVazeciUgovorOroceniDepozitDvojezicniRSD"] = "00008",
        ["PiVazeciUgovorOroceniDepozitOstaleValute"] = "00008",
        ["PiVazeciUgovorOroceniDepozitDinarskiTekuci"] = "00008",
        ["PiVazeciUgovorOroceniDepozitNa36Meseci"] = "00008",
        ["PiVazeciUgovorOroceniDepozitNa24MesecaRSD"] = "00008",
        ["PiVazeciUgovorOroceniDepozitNa25Meseci"] = "00008",
        ["PiPonuda"] = "00889",
        ["PiAnuitetniPlan"] = "00879",
        ["PiObavezniElementiUgovora"] = "00882",
        ["ZahtevZaOtvaranjeRacunaOrocenogDepozita"] = "00890",
    };

    /// <summary>
    /// Generates test case documents based on TestCase-migracija.txt requirements.
    /// Creates a variety of documents to cover all test scenarios.
    /// Uses DocumentTypeMapping dictionary for accurate names and codes.
    /// </summary>
    private static List<TestDocument> GenerateTestCaseDocuments(string clientType, int coreId, int folderIndex)
    {
        var documents = new List<TestDocument>();
        var random = new Random(coreId);

        // Track document names to avoid duplicates
        var usedFileNames = new HashSet<string>();

        // Helper function to create document from dictionary entry
        void AddDocument(string documentName, int? versionNumber = null, bool addMigrationSuffix = false)
        {
            if (!DocumentTypeMapping.TryGetValue(documentName, out var docTypeCode))
            {
                Console.WriteLine($"[WARNING] Document '{documentName}' not found in DocumentTypeMapping");
                return;
            }

            // Skip documents with null codes (deposit documents without proper mapping)
            if (string.IsNullOrEmpty(docTypeCode))
            {
                Console.WriteLine($"[INFO] Skipping document '{documentName}' - no document type code assigned");
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

            // TC 1 & 2: Add "-migracija" suffix to ecm:opisDokumenta for testing
            var opisDokumenta = addMigrationSuffix ? $"{documentName} - migracija" : documentName;
            var props = CreateDocumentProps(clientType, coreId, docTypeCode, opisDokumenta, "validiran", random);

            // Add version label if specified
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

        // Generate documents based on client type
        // NOTE: ACC (Account Package) documents are NOT generated here
        // ACC dossiers are created DURING migration process
        if (clientType == "PI")
        {
            // Test Case 4: Dosije fizičkog lica documents
            AddDocument("Personal Notice");
            AddDocument("KYC Questionnaire");
            AddDocument("KYC Questionnaire MDOC");
            AddDocument("Communication Consent");
            AddDocument("Specimen card");
            AddDocument("Specimen Card for Authorized Person");
            AddDocument("Pre-Contract Info");
            AddDocument("Contact Data Change Email");
            AddDocument("Contact Data Change Phone");

            // TC 1 & 2: Add documents with "-migracija" suffix (should become "poništen")
            AddDocument("KYC Questionnaire", addMigrationSuffix: true);
            AddDocument("Personal Notice", addMigrationSuffix: true);
        }
        else if (clientType == "LE")
        {
            // Test Case 5: Dosije pravnog lica documents
            AddDocument("KYC Questionnaire for LE");
            AddDocument("Current Accounts Contract");
            AddDocument("Specimen card for LE");
            AddDocument("Communication Consent");
            AddDocument("GDPR Revoke");
            AddDocument("GL Transaction");
            AddDocument("FX Transaction");
            AddDocument("Pre-Contract Info");
            AddDocument("Contact Data Change Email");
            AddDocument("Contact Data Change Phone");

            // TC 3: Add Account Package document (will be migrated to DOSSIERS-ACC)
            // Migration should detect "Account Package" and move to ACC folder
            AddDocument("Account Package");

            // TC 1 & 2: Add documents with "-migracija" suffix (should become "poništen")
            AddDocument("Communication Consent", addMigrationSuffix: true);
            AddDocument("KYC Questionnaire for LE", addMigrationSuffix: true);
        }

        return documents;
    }

    /// <summary>
    /// Helper method to create document properties with common fields
    /// </summary>
    private static Dictionary<string, object> CreateDocumentProps(
        string clientType,
        int coreId,
        string docTypeCode,
        string docTypeName,
        string docStatus,
        Random random)
    {
        var properties = new Dictionary<string, object>();

        // Standard properties
        properties["cm:title"] = docTypeName;
        properties["cm:description"] = $"Test document {docTypeName} for {clientType} client {coreId}";

        // CRITICAL: ecm:opisDokumenta - ključ za mapiranje u migraciji
        properties["ecm:docDesc"] = docTypeName;

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();

        // Document status (Test Cases 1-2)
        properties["ecm:status"] = docStatus;

        // Document type (ecm:tipDokumenta)
        properties["ecm:docType"] = docTypeCode;

        // Dossier type (Test Cases 4-5)
        string tipDosijea;
        if (clientType == "PI")
        {
            tipDosijea = "Dosije klijenta FL"; // TC 4
        }
        else // LE
        {
            tipDosijea = "Dosije klijenta PL"; // TC 5
        }
        properties["ecm:docDossierType"] = tipDosijea;//docDossierType

        // Client segment (CRITICAL for migration) 
        properties["ecm:docClientType"] = clientType;

        // Source (will be set by migration, but can add for reference)
        // Note: Migration will set this based on destination folder
        properties["ecm:source"] = "Heimdall";

        // Dates
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(1, 365));
        properties["ecm:docCreationDate"] = creationDate.ToString("o");

        return properties;
    }

    /// <summary>
    /// Generates custom properties for a client folder (dossier) based on client type and CoreId.
    /// Properties follow banking content model: ecm:propertyName
    /// Implements Test Cases from TestCase-migracija.txt
    /// </summary>
    private static Dictionary<string, object> GenerateFolderProperties(string clientType, int coreId)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId); // Seed with coreId for consistent mock data

        // Standard Content Model (cm:) properties - built-in Alfresco properties
        properties["cm:title"] = $"{clientType} Client {coreId}";
        properties["cm:description"] = $"Mock dossier for {clientType} client with CoreId {coreId}";

        // Banking Content Model (ecm:) properties - requires bankContentModel.xml to be deployed

        // Jedinstveni identifikator dosijea (Unique Folder Identifier)
        // Test Cases 19-21: Format based on dossier type
        string uniqueFolderId;
        string dossierType;

        if (clientType == "PI")
        {
            // Test Case 19: PI-{CoreId} for natural persons
            uniqueFolderId = $"PI-{coreId}";
            dossierType = "Dosije klijenta FL";
        }
        else // LE
        {
            // Test Case 20: LE-{CoreId} for legal entities
            uniqueFolderId = $"LE-{coreId}";
            dossierType = "Dosije klijenta PL";
        }

        properties["ecm:uniqueFolderId"] = uniqueFolderId;
        properties["ecm:folderId"] = uniqueFolderId; // Same as uniqueFolderId

        // Tip dosijea (Dossier Type) - Test Cases 3-7, 17
        properties["ecm:bnkDossierType"] = dossierType;

        // Tip klijenta (Client Type) - Test Case 4
        properties["ecm:clientType"] = clientType;
        properties["ecm:bnkClientType"] = clientType;

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();

        // Naziv klijenta (Client Name)
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

        properties["ecm:naziv"] = properties["ecm:clientName"]; // Same as clientName

        // MBR/JMBG (Company ID / Personal ID)
        if (clientType == "LE")
        {
            properties["ecm:mbrJmbg"] = (10000000 + random.Next(90000000)).ToString(); // MBR (8 digits)
        }
        else if (clientType == "PI")
        {
            var jmbg = (1000000000000L + random.Next(1000000000)).ToString(); // JMBG (13 digits)
            properties["ecm:mbrJmbg"] = jmbg;
            properties["ecm:jmbg"] = jmbg;
        }

        // Rezidentnost (Residency)
        var residencies = new[] { "Resident", "Non-resident" };
        properties["ecm:residency"] = residencies[random.Next(residencies.Length)];
        properties["ecm:bnkResidence"] = properties["ecm:residency"];

        // Izvor (Source) - Test Cases 6-7
        string source;
        if (dossierType == "700") // Deposit dossier
        {
            source = "DUT"; // Test Case 7
        }
        else
        {
            source = "Heimdall"; // Test Case 6
        }
        properties["ecm:source"] = source;
        properties["ecm:bnkSource"] = source;

        // Segment
        var segments = new[] { "Retail", "Corporate", "SME", "Premium", "Standard" };
        properties["ecm:segment"] = segments[random.Next(segments.Length)];

        // Podtip klijenta (Client Subtype)
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

        // Status - Test Case 11: Očekivanje je da su sva dokumenta aktivna
        properties["ecm:status"] = "ACTIVE";

        // Kreator (Creator)
        var creators = new[] { "Admin User", "Migration System", "System Bot" };
        properties["ecm:creator"] = creators[random.Next(creators.Length)];
        properties["ecm:kreiraoId"] = random.Next(1000, 9999).ToString();

        // OJ Kreiran ID (Organizational unit where created)
        properties["ecm:ojKreiranId"] = $"OJ-{random.Next(100, 999)}";

        // Barclex
        properties["ecm:barclex"] = $"BX{random.Next(10000, 99999)}";

        // Saradnik (Collaborator)
        var collaborators = new[] { "Partner Bank A", "Branch 001", "External Consultant", "" };
        properties["ecm:collaborator"] = collaborators[random.Next(collaborators.Length)];

        // Staff
        var staffIndicators = new[] { "Y", "N", "" };
        properties["ecm:staff"] = staffIndicators[random.Next(staffIndicators.Length)];
        properties["ecm:docStaff"] = properties["ecm:staff"];

        // Partija (Batch)
        var year = DateTime.UtcNow.Year;
        var month = DateTime.UtcNow.Month;
        properties["ecm:batch"] = $"BATCH-{year}-{month:D2}-{random.Next(1, 10):D3}";

        // OPU korisnika (User OPU)
        properties["ecm:opuUser"] = $"OPU-{random.Next(100, 999)}";

        // OPU/ID realizacije (Realization OPU/ID)
        properties["ecm:opuRealization"] = $"OPU-{random.Next(100, 999)}/ID-{random.Next(1000, 9999)}";

        // Datum kreiranja (Creation Date)
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(30, 730));
        properties["ecm:datumKreiranja"] = creationDate.ToString("o");

        // Aktivan (Active)
        properties["ecm:active"] = true;

        return properties;
    }

    /// <summary>
    /// Generates custom properties for documents within a dossier.
    /// Implements migration test cases for document-level properties.
    /// </summary>
    private static Dictionary<string, object> GenerateDocumentProperties(
        string clientType,
        int coreId,
        int docIndex,
        string documentName)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId + docIndex);

        // Standard Content Model properties
        properties["cm:title"] = documentName;
        properties["cm:description"] = $"Mock document for {clientType} client {coreId}";

        // Core ID linking document to client
        properties["ecm:coreId"] = coreId.ToString();
        properties["ecm:docClientId"] = coreId.ToString();

        // Test Cases 1-2: All documents are created as "validiran" (active)
        // The migration process will determine which ones should become "poništen" based on DocumentNameMapper
        string docStatus = "validiran";

        properties["ecm:docStatus"] = docStatus;
        properties["ecm:status"] = "ACTIVE";

        // Tip dokumenta (Document Type)
        var docTypes = new[] { "00001", "00002", "00003", "00099", "00100", "00101", "00824" };
        var docTypeCode = docTypes[random.Next(docTypes.Length)];
        properties["ecm:docTypeCode"] = docTypeCode;
        properties["ecm:docType"] = docTypeCode;
        properties["ecm:bnkTypeId"] = docTypeCode;
        properties["ecm:typeId"] = docTypeCode;

        // Document type names based on code
        var docTypeNames = new Dictionary<string, string>
        {
            { "00001", "Current Accounts Contract" },
            { "00002", "Saving Accounts Contract" },
            { "00003", "Personal Notice" },
            { "00099", "KDP za fizička lica" },
            { "00100", "KDP za pravna lica" },
            { "00101", "KDP za ovlašćena lica" },
            { "00824", "KDP vlasnika za FL" }
        };
        properties["ecm:docTypeName"] = docTypeNames.ContainsKey(docTypeCode)
            ? docTypeNames[docTypeCode]
            : "Generic Document";

        // Tip dosijea dokumenta (Document's Dossier Type)
        string docDossierType;
        if (clientType == "ACC")
        {
            docDossierType = "300"; // Dosije paket računa
        }
        else if (clientType == "FL")
        {
            docDossierType = "500"; // Dosije fizičkog lica
        }
        else // PL
        {
            docDossierType = "400"; // Dosije pravnog lica
        }
        properties["ecm:docDossierType"] = docDossierType;

        // Client type
        properties["ecm:clientType"] = clientType;
        properties["ecm:docClientType"] = clientType;
        properties["ecm:bnkClientType"] = clientType;

        // Source - Test Cases 6-7
        string source = docDossierType == "700" ? "DUT" : "Heimdall";
        properties["ecm:source"] = source;
        properties["ecm:bnkSource"] = source;
        properties["ecm:docSourceId"] = random.Next(1000, 9999).ToString();

        // Version information - Test Case 10: Multiple versions support
        properties["ecm:versionLabel"] = "1.0";
        properties["ecm:versionType"] = "Major";

        // Category
        var categoryIds = new[] { "CAT001", "CAT002", "CAT003", "CAT004" };
        var categoryId = categoryIds[random.Next(categoryIds.Length)];
        properties["ecm:docCategoryId"] = categoryId;
        properties["ecm:docCategoryName"] = $"Category {categoryId}";

        // Opis dokumenta (Document Description)
        properties["ecm:docDesc"] = $"Document {documentName} for client {coreId}";
        properties["ecm:opis"] = properties["ecm:docDesc"];

        // Kreirao (Created by)
        var creators = new[] { "Admin", "Migration Bot", "System" };
        properties["ecm:creator"] = creators[random.Next(creators.Length)];
        properties["ecm:createdByName"] = properties["ecm:creator"];
        properties["ecm:kreiraoId"] = random.Next(1000, 9999).ToString();

        // OJ Kreiran ID
        properties["ecm:ojKreiranId"] = $"OJ-{random.Next(100, 999)}";

        // Datum kreiranja (Creation Date)
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(1, 365));
        properties["ecm:datumKreiranja"] = creationDate.ToString("o");

        // Broj ugovora (Contract Number) - Test Case 16: Required for KDP documents
        if (docTypeCode == "00824" && docStatus == "validiran")
        {
            properties["ecm:contractNumber"] = $"{coreId}{random.Next(100, 999)}";
        }

        // Status odobravanja (Approval Status)
        var approvalStatuses = new[] { "1", "2", "3" }; // 1=pending, 2=approved, 3=rejected
        properties["ecm:statusOdobravanjaId"] = approvalStatuses[random.Next(approvalStatuses.Length)];

        // Stepen zavođenja (Registration level)
        properties["ecm:stepenZavodjenjaId"] = random.Next(1, 4).ToString();

        // Nivo arhiviranja (Archiving level)
        properties["ecm:nivoArhiviranja"] = random.Next(1, 4).ToString();

        // Tip kreiranja (Creation type)
        var creationTypes = new[] { "Manual", "Automatic", "Migration", "Import" };
        properties["ecm:creationType"] = creationTypes[random.Next(creationTypes.Length)];

        // Boolean flags
        properties["ecm:exported"] = random.Next(2) == 0;
        properties["ecm:storniran"] = false;
        properties["ecm:kompletiran"] = true;
        properties["ecm:ibUDelovodniku"] = random.Next(2) == 0;
        properties["ecm:poslataOriginalnaDokumentacija"] = random.Next(2) == 0;
        properties["ecm:active"] = docStatus == "validiran";

        // Status editabilnosti (Editability status)
        var editStatuses = new[] { "Editable", "Read-only", "Locked" };
        properties["ecm:editabilityStatus"] = editStatuses[random.Next(editStatuses.Length)];

        // Staff indicator
        properties["ecm:staff"] = random.Next(2) == 0 ? "Y" : "N";
        properties["ecm:docStaff"] = properties["ecm:staff"];

        // OPU user
        properties["ecm:opuUser"] = $"OPU-{random.Next(100, 999)}";

        // Segment
        var segments = new[] { "Retail", "Corporate", "SME", "Premium" };
        properties["ecm:segment"] = segments[random.Next(segments.Length)];

        // Residence
        properties["ecm:residency"] = random.Next(2) == 0 ? "Resident" : "Non-resident";
        properties["ecm:bnkResidence"] = properties["ecm:residency"];

        // MTBR
        properties["ecm:bnkMTBR"] = $"MTBR-{random.Next(1000, 9999)}";

        // Office ID
        properties["ecm:bnkOfficeId"] = $"OFF-{random.Next(100, 999)}";

        // Type of product
        properties["ecm:bnkTypeOfProduct"] = clientType == "PL" ? "00010" : "00008";
        properties["ecm:productType"] = properties["ecm:bnkTypeOfProduct"];

        // Client ID
        properties["ecm:bnkClientId"] = coreId.ToString();

        // Collaborator
        var collaborators = new[] { "Branch 001", "Partner Bank", "" };
        properties["ecm:collaborator"] = collaborators[random.Next(collaborators.Length)];

        // Barclex
        properties["ecm:barclex"] = $"BX{random.Next(10000, 99999)}";

        // Last thumbnail modification
        properties["ecm:lastThumbnailModification"] = DateTime.UtcNow.AddHours(-random.Next(1, 48)).ToString("o");

        return properties;
    }

    /// <summary>
    /// Generates properties specifically for deposit dossiers (Dosije depozita).
    /// Test Cases 17-25: Special handling for deposit documents.
    /// </summary>
    private static Dictionary<string, object> GenerateDepositDossierProperties(
        int coreId,
        string productType,
        string contractNumber)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId);

        // Standard properties
        properties["cm:title"] = $"Deposit Dossier {coreId}";
        properties["cm:description"] = $"Deposit dossier for CoreId {coreId}";

        // Test Case 18: Unique identifier format DE-{CoreId}-{ProductType}-{ContractNumber}
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var uniqueFolderId = $"DE-{coreId}-{productType}-{contractNumber}_{timestamp}";
        properties["ecm:uniqueFolderId"] = uniqueFolderId;
        properties["ecm:folderId"] = uniqueFolderId;

        // Dossier type
        properties["ecm:bnkDossierType"] = "700"; // Dosije depozita

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();

        // Product type (00008 for FL, 00010 for SB)
        properties["ecm:productType"] = productType;
        properties["ecm:bnkTypeOfProduct"] = productType;

        // Contract number
        properties["ecm:contractNumber"] = contractNumber;

        // Test Case 7: Source must be DUT for deposit dossiers
        properties["ecm:source"] = "DUT";
        properties["ecm:bnkSource"] = "DUT";

        // Test Case 25: Status should be ACTIVE for migrated deposit documents
        properties["ecm:status"] = "ACTIVE";
        properties["ecm:active"] = true;

        // Client type (determined by product type)
        var clientType = productType == "00008" ? "FL" : "PL";
        properties["ecm:clientType"] = clientType;
        properties["ecm:bnkClientType"] = clientType;

        // Client name
        if (clientType == "FL")
        {
            var firstNames = new[] { "Petar", "Marko", "Ana", "Jovana", "Milan" };
            var lastNames = new[] { "Petrović", "Jovanović", "Nikolić", "Marković" };
            properties["ecm:clientName"] = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";
        }
        else
        {
            var companies = new[] { "Privredno Društvo", "DOO Kompanija", "AD Firma" };
            properties["ecm:clientName"] = $"{companies[random.Next(companies.Length)]} {coreId}";
        }

        // Deposit processed date
        var depositDate = DateTime.UtcNow.AddDays(-random.Next(30, 730));
        properties["ecm:depositProcessedDate"] = depositDate.ToString("o");

        // Creation date
        properties["ecm:datumKreiranja"] = DateTime.UtcNow.AddDays(-random.Next(1, 30)).ToString("o");

        // Segment
        properties["ecm:segment"] = clientType == "FL" ? "Retail" : "Corporate";

        // Creator
        properties["ecm:creator"] = "DUT Migration System";
        properties["ecm:kreiraoId"] = random.Next(1000, 9999).ToString();

        return properties;
    }

    /// <summary>
    /// Generates properties for deposit documents with specific requirements.
    /// Test Cases 22-25: Deposit document migration rules.
    /// </summary>
    private static Dictionary<string, object> GenerateDepositDocumentProperties(
        int coreId,
        string productType,
        string contractNumber,
        int versionNumber,
        string documentType)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId + versionNumber);

        // Standard properties
        properties["cm:title"] = $"{documentType} v{versionNumber}";
        properties["cm:description"] = $"Deposit document for contract {contractNumber}";

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();
        properties["ecm:docClientId"] = coreId.ToString();

        // Dossier type
        properties["ecm:docDossierType"] = "700";

        // Test Case 25: All deposit documents should be ACTIVE
        properties["ecm:status"] = "ACTIVE";
        properties["ecm:docStatus"] = "validiran";
        properties["ecm:active"] = true;

        // Document type
        properties["ecm:docTypeCode"] = documentType;
        properties["ecm:docType"] = documentType;

        // Document type names for deposits (Test Case 24)
        var depositDocNames = new Dictionary<string, string>
        {
            { "DEP_UGV", "Ugovor o oročenom depozitu" },
            { "DEP_PON", "Ponuda" },
            { "DEP_PLA", "Plan isplate depozita" },
            { "DEP_OBE", "Obavezni elementi Ugovora" }
        };
        properties["ecm:docTypeName"] = depositDocNames.ContainsKey(documentType)
            ? depositDocNames[documentType]
            : documentType;

        // Test Case 7: Source DUT for deposits
        properties["ecm:source"] = "DUT";
        properties["ecm:bnkSource"] = "DUT";

        // Product type
        properties["ecm:productType"] = productType;
        properties["ecm:bnkTypeOfProduct"] = productType;

        // Contract number
        properties["ecm:contractNumber"] = contractNumber;

        // Test Case 22: Version support - multiple versions per document
        properties["ecm:versionLabel"] = $"{versionNumber}.0";
        properties["ecm:versionType"] = versionNumber == 1 ? "Initial" : "Revision";

        // Client type
        var clientType = productType == "00008" ? "FL" : "PL";
        properties["ecm:clientType"] = clientType;
        properties["ecm:docClientType"] = clientType;

        // Creation date
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(1, 365));
        properties["ecm:datumKreiranja"] = creationDate.ToString("o");

        // Creator
        properties["ecm:creator"] = "DUT System";
        properties["ecm:createdByName"] = "DUT System";

        // Completion status
        properties["ecm:kompletiran"] = true;

        return properties;
    }
}