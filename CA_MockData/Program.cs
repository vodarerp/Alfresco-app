
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
            RootParentId = "ba1bab40-d408-4622-9bab-40d408862208",
            FolderCount = 10000,
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
                var dosieFolderName = $"dosier-{clientType}";
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
                            if (cfg.AddFolderProperties && cfg.UseNewFolderStructure)
                            {
                                var clientType = cfg.ClientTypes[i % cfg.ClientTypes.Length];
                                var coreId = cfg.StartingCoreId + i;
                                properties = GenerateFolderProperties(clientType, coreId);
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

                            for (var x = 0; x < cfg.DocsPerFolder; x++)
                            {
                                var docName = cfg.UseNewFolderStructure
                                    ? $"Doc_{folderName}_{x:D3}.pdf"
                                    : $"MockDoc_{i:D6}_{x:D3}";
                                using var contet = GenerateDoc(i, x, folderName);
                                await CreateDocumentAsync(http, cfg, folderId, docName, contet, cts.Token);
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
        if (properties != null && properties.Count > 0)
        {
            // Create payload with properties using custom ecm:clientFolder type
            payload = new
            {
                name,
                nodeType = "cm:folder",
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
    private static async Task<string> CreateDocumentAsync(HttpClient http, Config cfg, string parentId, string name, Stream content, CancellationToken ct)
    {
        var url = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent("cm:content"), "nodeType");
        form.Add(new StringContent("true"), "autoRename");
        var sc = new StreamContent(content);
        sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(sc, "filedata", name);


        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        using var res = await SendWithRetryAsync(http, cfg, req, ct);
        using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.GetProperty("entry").GetProperty("id").GetString()!;
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
    /// Generates custom properties for a client folder based on client type and CoreId.
    /// Properties follow banking content model: ecm:propertyName
    /// </summary>
    private static Dictionary<string, object> GenerateFolderProperties(string clientType, int coreId)
    {
        var properties = new Dictionary<string, object>();
        var random = new Random(coreId); // Seed with coreId for consistent mock data

        // Standard Content Model (cm:) properties - built-in Alfresco properties
        properties["cm:title"] = $"{clientType} Client {coreId}";
        properties["cm:description"] = $"Mock folder for {clientType} client with CoreId {coreId}";

        // Banking Content Model (ecm:) properties - requires bankContentModel.xml to be deployed

        // Tip klijenta (Client Type)
        properties["ecm:clientType"] = clientType;

        // Kreator (Creator)
        var creators = new[] { "Admin User", "Migration System", "John Doe", "Jane Smith", "System Bot" };
        properties["ecm:creator"] = creators[random.Next(creators.Length)];

        // Jedinstveni identifikator dosijea (Unique Folder Identifier)
        // Format: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}
        var productType = clientType == "PL" ? "00010" : "00008";
        var contractNumber = (10000000 + random.Next(1000000)).ToString();
        var timestamp = DateTime.UtcNow.AddDays(-random.Next(1, 365)).ToString("yyyyMMddHHmmss");
        properties["ecm:uniqueFolderId"] = $"DE-{coreId}-{productType}-{contractNumber}_{timestamp}";

        // Barclex
        properties["ecm:barclex"] = $"BX{random.Next(10000, 99999)}";

        // Saradnik (Collaborator)
        var collaborators = new[] { "Partner Bank A", "Agency XYZ", "Branch 001", "External Consultant", "" };
        properties["ecm:collaborator"] = collaborators[random.Next(collaborators.Length)];

        // MBR/JMBG (Company ID / Personal ID)
        properties["ecm:mbrJmbg"] = clientType == "PL"
            ? (10000000 + random.Next(90000000)).ToString() // MBR for legal entities (8 digits)
            : (1000000000000L + random.Next(1000000000)).ToString(); // JMBG for natural persons (13 digits)

        // Core ID
        properties["ecm:coreId"] = coreId.ToString();

        // Naziv klijenta (Client Name)
        if (clientType == "PL")
        {
            var companies = new[] { "Privredno Društvo", "DOO Kompanija", "AD Firma", "JP Preduzeće", "OD Organizacija" };
            properties["ecm:clientName"] = $"{companies[random.Next(companies.Length)]} {coreId}";
        }
        else
        {
            var firstNames = new[] { "Petar", "Marko", "Ana", "Jovana", "Milan", "Nikola", "Stefan", "Milica" };
            var lastNames = new[] { "Petrović", "Jovanović", "Nikolić", "Marković", "Đorđević", "Stojanović" };
            properties["ecm:clientName"] = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";
        }

        // Partija (Batch)
        var year = DateTime.UtcNow.Year;
        var month = DateTime.UtcNow.Month;
        properties["ecm:batch"] = $"BATCH-{year}-{month:D2}-{random.Next(1, 10):D3}";

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
        else
        {
            properties["ecm:clientSubtype"] = "Account";
        }

        // Staff
        var staffIndicators = new[] { "Y", "N", "" };
        properties["ecm:staff"] = staffIndicators[random.Next(staffIndicators.Length)];

        // OPU korisnika (User OPU)
        properties["ecm:opuUser"] = $"OPU-{random.Next(100, 999)}";

        // OPU/ID realizacije (Realization OPU/ID)
        properties["ecm:opuRealization"] = $"OPU-{random.Next(100, 999)}/ID-{random.Next(1000, 9999)}";

        // Tip proizvoda (Product Type)
        properties["ecm:productType"] = productType;

        // Broj ugovora (Contract Number)
        properties["ecm:contractNumber"] = contractNumber;

        // Datum kreiranja / Datum procesiranja depozita (Deposit Processed Date)
        // This is NOT migration date, but when deposit was processed (in the past)
        var depositDate = DateTime.UtcNow.AddDays(-random.Next(30, 730)); // 1 month to 2 years ago
        properties["ecm:depositProcessedDate"] = depositDate.ToString("o");

        // Rezidentnost (Residency)
        var residencies = new[] { "Resident", "Non-resident" };
        properties["ecm:residency"] = residencies[random.Next(residencies.Length)];

        // Izvor (Source)
        var sources = new[] { "Migration", "Manual Entry", "Import", "System Generated", "External API" };
        properties["ecm:source"] = sources[random.Next(sources.Length)];

        // Status
        var statuses = new[] { "ACTIVE", "ACTIVE", "ACTIVE", "INACTIVE", "MIGRATED" }; // Bias towards ACTIVE
        properties["ecm:status"] = statuses[random.Next(statuses.Length)];

        // Datum arhiviranja (Archive Date) - only if status is ARCHIVED
        if (properties["ecm:status"].ToString() == "ARCHIVED")
        {
            var archiveDate = DateTime.UtcNow.AddDays(-random.Next(1, 180)); // Up to 6 months ago
            properties["ecm:archiveDate"] = archiveDate.ToString("o");
        }

        // Segment
        var segments = new[] { "Retail", "Corporate", "SME", "Premium", "Standard", "VIP" };
        properties["ecm:segment"] = segments[random.Next(segments.Length)];

        return properties;
    }
}