namespace MockClientAPI.Models
{
    /// <summary>
    /// Basic client detail information
    /// Endpoint: GET /api/Client/GetClientDetail/{coreId}
    /// </summary>
    public class ClientDetailInfo
    {
        public string CoreId { get; set; } = string.Empty;
        public string IdentityNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string ClientStatus { get; set; } = string.Empty;
        public DateTime RegistrationDate { get; set; }
    }
}
