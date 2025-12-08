namespace Alfresco.Contracts.Options
{
    /// <summary>
    /// Centralized configuration for all external connections (APIs and databases).
    /// Loaded from appsettings.Connections.json
    /// </summary>
    public class ConnectionsOptions
    {
        public const string SectionName = "Connections";

        /// <summary>
        /// Alfresco PostgreSQL database connection
        /// </summary>
        public AlfrescoDatabaseOptions AlfrescoDatabase { get; set; } = new();

        /// <summary>
        /// Alfresco API connection
        /// </summary>
        public AlfrescoApiOptions Alfresco { get; set; } = new();

        /// <summary>
        /// Client API connection
        /// </summary>
        public ClientApiConnectionOptions ClientApi { get; set; } = new();

        /// <summary>
        /// SQL Server database connection
        /// </summary>
        public SqlServerConnectionOptions SqlServer { get; set; } = new();
    }

    public class AlfrescoDatabaseOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
    }

    public class AlfrescoApiOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ClientApiConnectionOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
        public int TimeoutSeconds { get; set; } = 30;
        public int RetryCount { get; set; } = 3;

        // Endpoint paths
        public string GetClientDataEndpoint { get; set; } = "/api/Client/GetClientDetailExtended";
        public string GetActiveAccountsEndpoint { get; set; } = "/api/Client";
        public string ValidateClientEndpoint { get; set; } = "/api/Client/GetClientDetail";
    }

    public class SqlServerConnectionOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
        public int CommandTimeoutSeconds { get; set; } = 120;
        public int BulkBatchSize { get; set; } = 1000;
    }
}
