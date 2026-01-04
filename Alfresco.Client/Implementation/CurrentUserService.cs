using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;

namespace Alfresco.Client.Implementation
{
   
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;

        private AlfrescoUserInfo? _currentUser;
        private bool _initialized = false;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public string UserId => _currentUser?.Id ?? string.Empty;
        public string DisplayName => _currentUser?.DisplayName ?? string.Empty;
        public string Email => _currentUser?.Email ?? string.Empty;
        public AlfrescoUserInfo? CurrentUser => _currentUser;

        public CurrentUserService(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            // Fast path - already initialized
            if (_initialized)
            {
                _fileLogger.LogInformation("CurrentUserService already initialized - UserId: {UserId}, DisplayName: {DisplayName}",
                    UserId, DisplayName);
                return;
            }

            // Acquire lock to prevent concurrent initialization
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock (another thread might have initialized)
                if (_initialized)
                {
                    _fileLogger.LogInformation("CurrentUserService already initialized by another thread - UserId: {UserId}, DisplayName: {DisplayName}",
                        UserId, DisplayName);
                    return;
                }

                _fileLogger.LogInformation("Initializing CurrentUserService - fetching current user from Alfresco...");

                // Create HttpClient from factory
                var httpClient = _httpClientFactory.CreateClient("AlfrescoCurrentUserClient");

                var url = "/alfresco/api/-default-/public/alfresco/versions/1/people/-me-";
                _fileLogger.LogInformation("CurrentUserService: REQUEST -> GET {Url}", url);

                using var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                _fileLogger.LogInformation("CurrentUserService: RESPONSE -> Status: {StatusCode}, Body: {ResponseBody}",
                    (int)response.StatusCode, body);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = $"Failed to fetch current user from Alfresco: {response.StatusCode}";
                    _fileLogger.LogError("{ErrorMessage}, Response: {Body}", errorMessage, body);
                    _dbLogger.LogError("{ErrorMessage}", errorMessage);
                    throw new HttpRequestException(errorMessage);
                }

                // Parse JSON response
                var jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };

                var userResponse = JsonConvert.DeserializeObject<AlfrescoUserInfoResponse>(body, jsonSettings);

                if (userResponse?.Entry == null)
                {
                    var errorMessage = "Alfresco returned empty user info";
                    _fileLogger.LogError("{ErrorMessage}", errorMessage);
                    _dbLogger.LogError("{ErrorMessage}", errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                // Map to AlfrescoUserInfo
                _currentUser = new AlfrescoUserInfo
                {
                    Id = userResponse.Entry.Id,
                    DisplayName = userResponse.Entry.DisplayName,
                    Email = userResponse.Entry.Email,
                    FirstName = userResponse.Entry.FirstName,
                    LastName = userResponse.Entry.LastName,
                    GoogleId = userResponse.Entry.GoogleId
                };

                _initialized = true;

                _fileLogger.LogInformation(
                    "CurrentUserService initialized successfully - UserId: {UserId}, DisplayName: {DisplayName}, Email: {Email}, FirstName: {FirstName}, LastName: {LastName}",
                    _currentUser.Id, _currentUser.DisplayName, _currentUser.Email, _currentUser.FirstName, _currentUser.LastName);

                _dbLogger.LogInformation(
                    "Current Alfresco user: {DisplayName} ({UserId})",
                    _currentUser.DisplayName, _currentUser.Id);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(ex, "Error initializing CurrentUserService - {ErrorType}: {Message}",
                    ex.GetType().Name, ex.Message);
                _dbLogger.LogError(ex, "Failed to fetch current Alfresco user");
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
