namespace Alfresco.Contracts.Models
{
    /// <summary>
    /// Model for Alfresco user information fetched from /people/-me- endpoint
    /// </summary>
    public class AlfrescoUserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? GoogleId { get; set; }
    }

    /// <summary>
    /// Response DTO for Alfresco /people/-me- endpoint
    /// Matches JSON structure: { "entry": { "id": "...", "displayName": "...", ... } }
    /// </summary>
    public class AlfrescoUserInfoResponse
    {
        public AlfrescoUserInfoEntry? Entry { get; set; }
    }

    public class AlfrescoUserInfoEntry
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? GoogleId { get; set; }
        public bool Enabled { get; set; }
        public bool EmailNotificationsEnabled { get; set; }
    }
}
