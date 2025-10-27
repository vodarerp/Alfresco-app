namespace MockClientAPI.Models
{
    /// <summary>
    /// Extended client detail information with additional fields
    /// Endpoint: GET /api/Client/GetClientDetailExtended/{coreId}
    /// </summary>
    public class ClientDetailExtended
    {
        public string CoreId { get; set; } = string.Empty;
        public string IdentityNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Nationality { get; set; }
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string ClientStatus { get; set; } = string.Empty;
        public string ClientType { get; set; } = string.Empty;
        public DateTime RegistrationDate { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public string? TaxNumber { get; set; }
        public string? BankAccount { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public decimal CreditLimit { get; set; }
        public string PreferredLanguage { get; set; } = "sr-RS";
    }
}
