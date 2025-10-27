namespace MockClientAPI.Models
{
    /// <summary>
    /// Business data for client including balance, orders, and metadata
    /// Endpoint: GET /api/Client/GetClientData/{clientId}
    /// </summary>
    public class ClientDataInfo
    {
        public string ClientId { get; set; } = string.Empty;
        public string CoreId { get; set; } = string.Empty;
        public string IdentityNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string ClientStatus { get; set; } = string.Empty;
        public DateTime RegistrationDate { get; set; }
        public decimal TotalBalance { get; set; }
        public int TotalOrders { get; set; }
        public DateTime? LastOrderDate { get; set; }
        public string? PreferredPaymentMethod { get; set; }
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
