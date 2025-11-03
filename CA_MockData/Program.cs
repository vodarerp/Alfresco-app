
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
            ClientTypes = new[] { "PL", "FL", "ACC" },
            StartingCoreId = 10000000,
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
                var dosieFolderName = $"DOSSIER-{clientType}";
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
                            folderName = $"{clientType}-{coreId}TTT"; // e.g., PL-10000000, FL-10000001
                            parentId = dosieFolders[clientType]; // dosie-PL, dosie-FL, etc.
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
        var baseUrl = "http://localhost:8080/";
        var http = new HttpClient
        {
            BaseAddress = new Uri(cfg.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:admin"));
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
    /// Generates test case documents based on TestCase-migracija.txt requirements.
    /// Creates a variety of documents to cover all test scenarios.
    /// </summary>
    private static List<TestDocument> GenerateTestCaseDocuments(string clientType, int coreId, int folderIndex)
    {
        var documents = new List<TestDocument>();
        var random = new Random(coreId);

        // Test Cases 1-2: Mix of active and inactive documents
        // Some with "_migracija" suffix (inactive), some without (active)

        if (clientType == "ACC")
        {
            // Test Case 3: Dosije paket računa documents
            documents.Add(new TestDocument
            {
                Name = "Current_Accounts_Contract.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00001", "Current Accounts Contract", "validiran", random)
            });

            documents.Add(new TestDocument
            {
                Name = "Saving_Accounts_Contract.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00002", "Saving Accounts Contract", "validiran", random)
            });

            documents.Add(new TestDocument
            {
                Name = $"TEKUCI_DEVIZNI_RACUN_{coreId}001.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00003", $"TEKUCI DEVIZNI RACUN {coreId}001", "validiran", random)
            });

            // Test Case 1: Inactive document with "_migracija" suffix
            documents.Add(new TestDocument
            {
                Name = "Account_Package_migracija.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00004", "Account Package", "poništen", random)
            });

            documents.Add(new TestDocument
            {
                Name = "Specimen_card.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00005", "Specimen card", "validiran", random)
            });
        }
        else if (clientType == "FL")
        {
            // Test Case 4: Dosije fizičkog lica documents
            documents.Add(new TestDocument
            {
                Name = "Personal_Notice.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00010", "Personal Notice", "validiran", random)
            });

            documents.Add(new TestDocument
            {
                Name = "KYC_Questionnaire_MDOC.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00011", "KYC Questionnaire MDOC", "validiran", random)
            });

            // Test Case 1: Inactive document with "_migracija"
            documents.Add(new TestDocument
            {
                Name = "Communication_Consent_migracija.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00012", "Communication Consent", "poništen", random)
            });

            documents.Add(new TestDocument
            {
                Name = "Credit_Bureau_Reports_Consent.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00013", "Credit Bureau Reports Consent", "validiran", random)
            });

            // Test Case 12: KDP za fizička lica (00099) - should be marked inactive if retention policy is "nova verzija"
            documents.Add(new TestDocument
            {
                Name = "KDP_za_fizicka_lica.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00099", "KDP za fizička lica", "validiran", random)
            });

            // Test Case 13: KDP za ovlašćena lica (00101)
            documents.Add(new TestDocument
            {
                Name = "KDP_za_ovlascena_lica.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00101", "KDP za ovlašćena lica", "validiran", random)
            });

            // Test Case 16: KDP vlasnika za FL (00824) - requires contract number
            var contractNumber = $"{coreId}{random.Next(100, 999)}";
            var kdpProps = CreateDocumentProps(clientType, coreId, "00824", "KDP vlasnika za FL", "validiran", random);
            kdpProps["ecm:contractNumber"] = contractNumber;
            documents.Add(new TestDocument
            {
                Name = "KDP_vlasnika_za_FL.pdf",
                Properties = kdpProps
            });

            // Test Case 10: Multiple versions of the same document
            for (int version = 1; version <= 3; version++)
            {
                var versionProps = CreateDocumentProps(clientType, coreId, "00020", "Family Insurance", "validiran", random);
                versionProps["ecm:versionLabel"] = $"{version}.0";
                versionProps["ecm:versionType"] = version == 1 ? "Initial" : "Revision";
                documents.Add(new TestDocument
                {
                    Name = $"Family_Insurance_v{version}.pdf",
                    Properties = versionProps
                });
            }
        }
        else if (clientType == "PL")
        {
            // Test Case 5: Dosije pravnog lica documents
            documents.Add(new TestDocument
            {
                Name = "KYC_Questionnaire_for_LE.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00030", "KYC Questionnaire for LE", "validiran", random)
            });

            documents.Add(new TestDocument
            {
                Name = "APR_Certificate.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00031", "APR Certificate", "validiran", random)
            });

            documents.Add(new TestDocument
            {
                Name = "Current_Account_Contract_for_LE.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00032", "Current Account Contract for LE", "validiran", random)
            });

            // Test Case 1: Inactive with "_migracija"
            documents.Add(new TestDocument
            {
                Name = "Specimen_card_for_LE_migracija.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00033", "Specimen card for LE", "poništen", random)
            });

            // Test Case 14: KDP za pravna lica (00100)
            documents.Add(new TestDocument
            {
                Name = "KDP_za_pravna_lica.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00100", "KDP za pravna lica", "validiran", random)
            });

            // Test Case 4: Documents that should be in PL dossier
            documents.Add(new TestDocument
            {
                Name = "Communication_Consent_LE.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00034", "Communication Consent", "validiran", random)
            });

            documents.Add(new TestDocument
            {
                Name = "Personal_Notice_LE.pdf",
                Properties = CreateDocumentProps(clientType, coreId, "00035", "Personal Notice", "validiran", random)
            });

            // Test Case 10: Multiple versions
            for (int version = 1; version <= 2; version++)
            {
                var versionProps = CreateDocumentProps(clientType, coreId, "00036", "Card Limit Change", "validiran", random);
                versionProps["ecm:versionLabel"] = $"{version}.0";
                versionProps["ecm:versionType"] = version == 1 ? "Initial" : "Revision";
                documents.Add(new TestDocument
                {
                    Name = $"Card_Limit_Change_v{version}.pdf",
                    Properties = versionProps
                });
            }
        }

        // Add some general active documents for all types (Test Case 2)
        documents.Add(new TestDocument
        {
            Name = "Pre-Contract_Info.pdf",
            Properties = CreateDocumentProps(clientType, coreId, "00050", "Pre-Contract Info", "validiran", random)
        });

        documents.Add(new TestDocument
        {
            Name = "Account_Package_RSD_Instruction.pdf",
            Properties = CreateDocumentProps(clientType, coreId, "00051", "Account Package RSD Instruction for Resident", "validiran", random)
        });

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

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();
        properties["ecm:docClientId"] = coreId.ToString();
        properties["ecm:bnkClientId"] = coreId.ToString();

        // Document status (Test Cases 1-2)
        properties["ecm:docStatus"] = docStatus;
        properties["ecm:status"] = docStatus == "validiran" ? "ACTIVE" : "INACTIVE";
        properties["ecm:active"] = docStatus == "validiran";

        // Document type
        properties["ecm:docTypeCode"] = docTypeCode;
        properties["ecm:docType"] = docTypeCode;
        properties["ecm:bnkTypeId"] = docTypeCode;
        properties["ecm:typeId"] = docTypeCode;
        properties["ecm:docTypeName"] = docTypeName;

        // Dossier type (Test Cases 3-5)
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

        // Source (Test Case 6)
        properties["ecm:source"] = "Heimdall";
        properties["ecm:bnkSource"] = "Heimdall";
        properties["ecm:docSourceId"] = random.Next(1000, 9999).ToString();

        // Version information (Test Case 10)
        properties["ecm:versionLabel"] = "1.0";
        properties["ecm:versionType"] = "Major";

        // Category
        properties["ecm:docCategoryId"] = "CAT001";
        properties["ecm:docCategoryName"] = "Category CAT001";

        // Description
        properties["ecm:docDesc"] = $"Document {docTypeName} for client {coreId}";
        properties["ecm:opis"] = properties["ecm:docDesc"];

        // Creator
        properties["ecm:creator"] = "Migration System";
        properties["ecm:createdByName"] = "Migration System";
        properties["ecm:kreiraoId"] = random.Next(1000, 9999).ToString();
        properties["ecm:ojKreiranId"] = $"OJ-{random.Next(100, 999)}";

        // Dates
        var creationDate = DateTime.UtcNow.AddDays(-random.Next(1, 365));
        properties["ecm:datumKreiranja"] = creationDate.ToString("o");

        // Status fields
        properties["ecm:statusOdobravanjaId"] = "2"; // Approved
        properties["ecm:stepenZavodjenjaId"] = random.Next(1, 4).ToString();
        properties["ecm:nivoArhiviranja"] = random.Next(1, 4).ToString();
        properties["ecm:creationType"] = "Migration";

        // Boolean flags
        properties["ecm:exported"] = false;
        properties["ecm:storniran"] = false;
        properties["ecm:kompletiran"] = true;
        properties["ecm:ibUDelovodniku"] = false;
        properties["ecm:poslataOriginalnaDokumentacija"] = false;

        // Other fields
        properties["ecm:editabilityStatus"] = "Read-only";
        properties["ecm:staff"] = "N";
        properties["ecm:docStaff"] = "N";
        properties["ecm:opuUser"] = $"OPU-{random.Next(100, 999)}";
        properties["ecm:segment"] = clientType == "PL" ? "Corporate" : "Retail";
        properties["ecm:residency"] = "Resident";
        properties["ecm:bnkResidence"] = "Resident";
        properties["ecm:bnkMTBR"] = $"MTBR-{random.Next(1000, 9999)}";
        properties["ecm:bnkOfficeId"] = $"OFF-{random.Next(100, 999)}";
        properties["ecm:bnkTypeOfProduct"] = clientType == "PL" ? "00010" : "00008";
        properties["ecm:productType"] = properties["ecm:bnkTypeOfProduct"];
        properties["ecm:collaborator"] = "Branch 001";
        properties["ecm:barclex"] = $"BX{random.Next(10000, 99999)}";
        properties["ecm:lastThumbnailModification"] = DateTime.UtcNow.ToString("o");

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

        if (clientType == "ACC")
        {
            // Test Case 21: ACC-{CoreId} for account package dossiers
            uniqueFolderId = $"ACC-{coreId}";
            dossierType = "300"; // Dosije paket računa
        }
        else if (clientType == "FL")
        {
            // Test Case 19: PI-{CoreId} for natural persons
            uniqueFolderId = $"PI-{coreId}";
            dossierType = "500"; // Dosije fizičkog lica
        }
        else // PL
        {
            // Test Case 20: LE-{CoreId} for legal entities
            uniqueFolderId = $"LE-{coreId}";
            dossierType = "400"; // Dosije pravnog lica
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
        if (clientType == "PL")
        {
            var companies = new[] { "Privredno Društvo", "DOO Kompanija", "AD Firma", "JP Preduzeće", "OD Organizacija" };
            properties["ecm:clientName"] = $"{companies[random.Next(companies.Length)]} {coreId}";
        }
        else if (clientType == "FL")
        {
            var firstNames = new[] { "Petar", "Marko", "Ana", "Jovana", "Milan", "Nikola", "Stefan", "Milica" };
            var lastNames = new[] { "Petrović", "Jovanović", "Nikolić", "Marković", "Đorđević", "Stojanović" };
            properties["ecm:clientName"] = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";
        }
        else // ACC
        {
            properties["ecm:clientName"] = $"Account Package Client {coreId}";
        }

        properties["ecm:naziv"] = properties["ecm:clientName"]; // Same as clientName

        // MBR/JMBG (Company ID / Personal ID)
        if (clientType == "PL")
        {
            properties["ecm:mbrJmbg"] = (10000000 + random.Next(90000000)).ToString(); // MBR (8 digits)
        }
        else if (clientType == "FL")
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
        if (clientType == "PL")
        {
            var subtypes = new[] { "SME", "Corporate", "Public Sector", "Non-Profit" };
            properties["ecm:clientSubtype"] = subtypes[random.Next(subtypes.Length)];
        }
        else if (clientType == "FL")
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

        // Test Cases 1-2: Document status based on name suffix
        // Documents with "_migracija" suffix should be "poništen" (cancelled)
        // Documents without suffix should be "validiran" (validated)
        bool isMigrationDoc = documentName.Contains("_migracija", StringComparison.OrdinalIgnoreCase);
        string docStatus = isMigrationDoc ? "poništen" : "validiran";

        properties["ecm:docStatus"] = docStatus;
        properties["ecm:status"] = docStatus == "validiran" ? "ACTIVE" : "INACTIVE";

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