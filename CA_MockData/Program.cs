
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
            RootParentId = "c157f49e-39de-4d3a-97f4-9e39debd3aed",
            FolderCount = 10000,
            DocsPerFolder =3,
            DegreeOfParallelism = 8,
            MaxRetries = 5,
            RetryBaseDelayMs = 100
        };


        var sw = Stopwatch.StartNew();

        //var foldersCount = 10000; //promeniti da se cita iz args
        //var docPerFolder = 3; //isto

        var createdFolders = 0;
        var createdDocument = 0;
        var failed = 0;
        //var RootParentId = "";

        //var maxRetries = 3;
        //var DoP = Math.Min(Environment.ProcessorCount, 16);
        var start = DateTime.UtcNow;
        var totalDocs = (long)cfg.FolderCount * cfg.DocsPerFolder;
        using var http = CreateHttpClient(cfg);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        
        //var xx = Environment.ProcessorCount; -- 8

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
                        var folderName = $"MockFolder-{i:D6}";
                        try
                        {
                            var folderId = await CreateFolderAsync(http,cfg,cfg.RootParentId, folderName,cts.Token);

                            Interlocked.Increment(ref createdFolders);

                            for (var x = 0; x < cfg.DocsPerFolder; x++) 
                            {
                                var docName = $"MockDoc_{i:D6}_{x:D3}";
                                using var contet = GenerateDoc(i,x);
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
                            Console.WriteLine($"[ERROR] w{wid} item:{i}: {ex.Message}");
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


    private static async Task<string> CreateFolderAsync(HttpClient http, Config cfg, string parentId, string name, CancellationToken ct)
    {
        var url = $"alfresco/api/-default-/public/alfresco/versions/1/nodes/{parentId}/children";
        var payload = new { name, nodeType = "cm:folder" };
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        using var res = await SendWithRetryAsync(http, cfg, req, ct);
        using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return doc!.RootElement.GetProperty("entry").GetProperty("id").GetString()!;
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
            catch (HttpRequestException) when (attempt < cfg.MaxRetries)
            {
                // will retry
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


    private static Stream GenerateDoc(int folderIndex, int docIndex)
    {
        var text = $"Test doc for folder {folderIndex}, doc {docIndex}. UTC: {DateTime.UtcNow:o}";
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}