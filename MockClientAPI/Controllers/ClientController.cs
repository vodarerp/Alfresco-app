using Microsoft.AspNetCore.Mvc;
using MockClientAPI.Models;

namespace MockClientAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClientController : ControllerBase
    {
        private readonly ILogger<ClientController> _logger;

        public ClientController(ILogger<ClientController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Returns basic client information based on CoreId
        /// </summary>
        /// <param name="coreId">Unique client identifier in Core system</param>
        /// <returns>Client detail information</returns>
        [HttpGet("GetClientDetail/{coreId}")]
        [ProducesResponseType(typeof(ClientDetailInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetClientDetail(string coreId)
        {
            _logger.LogInformation("GetClientDetail called with CoreId: {CoreId}", coreId);

            // Handle error simulation
            if (coreId.Equals("notfound", StringComparison.OrdinalIgnoreCase) ||
                coreId == "0" || coreId == "0000000000")
            {
                return NotFound($"Client with CoreId '{coreId}' not found");
            }

            if (coreId.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(500, "Internal server error occurred");
            }

            // Generate mock data
            var clientDetail = new ClientDetailInfo
            {
                CoreId = coreId,
                IdentityNumber = $"ID{new Random().Next(100000, 999999)}",
                FirstName = "Marko",
                LastName = "Markovic",
                Email = $"marko.markovic.{coreId}@example.com",
                PhoneNumber = "+381 11 1234567",
                DateOfBirth = new DateTime(1985, 5, 15),
                Address = "Bulevar Kralja Aleksandra 123",
                City = "Beograd",
                Country = "Srbija",
                PostalCode = "11000",
                ClientStatus = "Active",
                RegistrationDate = new DateTime(2020, 1, 15)
            };

            return Ok(clientDetail);
        }

        /// <summary>
        /// Returns extended client information with additional fields
        /// </summary>
        /// <param name="coreId">Unique client identifier</param>
        /// <returns>Extended client detail information</returns>
        [HttpGet("GetClientDetailExtended/{coreId}")]
        [ProducesResponseType(typeof(ClientDetailExtended), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetClientDetailExtended(string coreId)
        {
            _logger.LogInformation("GetClientDetailExtended called with CoreId: {CoreId}", coreId);

            // Handle error simulation
            if (coreId.Equals("notfound", StringComparison.OrdinalIgnoreCase) ||
                coreId == "0" || coreId == "0000000000")
            {
                return NotFound($"Client with CoreId '{coreId}' not found");
            }

            if (coreId.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(500, "Internal server error occurred");
            }

            // Generate mock data
            var random = new Random(coreId.GetHashCode());
            var clientTypes = new[] { "Premium", "Standard", "VIP", "Regular" };
            var genders = new[] { "Male", "Female" };
            var residencies = new[] { "Resident", "Non-resident" };

            var clientDetail = new ClientDetailExtended
            {
                CoreId = coreId,
                IdentityNumber = $"ID{random.Next(100000, 999999)}",
                FirstName = "Marko",
                LastName = "Markovic",
                MiddleName = "Petrovic",
                Email = $"marko.markovic.{coreId}@example.com",
                PhoneNumber = "+381 11 1234567",
                MobileNumber = "+381 64 1234567",
                DateOfBirth = new DateTime(1985, 5, 15),
                Gender = genders[random.Next(genders.Length)],
                Nationality = "Serbian",
                Address = "Bulevar Kralja Aleksandra 123",
                City = "Beograd",
                Country = "Srbija",
                PostalCode = "11000",
                Region = "Central Serbia",
                ClientStatus = "Active",
                ClientType = clientTypes[random.Next(clientTypes.Length)],
                RegistrationDate = new DateTime(2020, 1, 15),
                LastModifiedDate = DateTime.UtcNow.AddDays(-random.Next(1, 30)),
                TaxNumber = $"{random.Next(100000000, 999999999)}",
                BankAccount = $"160-{random.Next(100000, 999999)}-{random.Next(10, 99)}",
                Notes = "VIP client with excellent payment history",
                IsActive = true,
                CreditLimit = random.Next(10000, 100000),
                PreferredLanguage = "sr-RS"
            };

            return Ok(clientDetail);
        }

        /// <summary>
        /// Returns business data for a client including balance, orders, and metadata
        /// </summary>
        /// <param name="clientId">Unique client identifier</param>
        /// <returns>Client business data</returns>
        [HttpGet("GetClientData/{clientId}")]
        [ProducesResponseType(typeof(ClientDataInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetClientData(string clientId)
        {
            _logger.LogInformation("GetClientData called with ClientId: {ClientId}", clientId);

            // Handle error simulation
            if (clientId.Equals("notfound", StringComparison.OrdinalIgnoreCase) ||
                clientId == "0" || clientId == "0000000000")
            {
                return NotFound($"Client with ClientId '{clientId}' not found");
            }

            if (clientId.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(500, "Internal server error occurred");
            }

            // Generate mock data
            var random = new Random(clientId.GetHashCode());
            var clientData = new ClientDataInfo
            {
                ClientId = clientId,
                CoreId = $"CORE{random.Next(1000, 9999)}",
                IdentityNumber = $"ID{random.Next(100000, 999999)}",
                FullName = "Marko Markovic",
                Email = $"marko.markovic.{clientId}@example.com",
                PhoneNumber = "+381 64 1234567",
                ClientStatus = "Active",
                RegistrationDate = new DateTime(2020, 1, 15),
                TotalBalance = (decimal)(random.NextDouble() * 50000),
                TotalOrders = random.Next(10, 100),
                LastOrderDate = DateTime.UtcNow.AddDays(-random.Next(1, 30)),
                PreferredPaymentMethod = "Credit Card",
                Tags = new List<string> { "VIP", "Frequent Buyer", "Premium" },
                Metadata = new Dictionary<string, object>
                {
                    { "loyaltyPoints", random.Next(100, 5000) },
                    { "preferredCategory", "Electronics" },
                    { "newsletter", true },
                    { "referralCode", $"REF{random.Next(100, 999)}" }
                }
            };

            return Ok(clientData);
        }

        /// <summary>
        /// Checks if a client exists by identity number and returns CoreId
        /// </summary>
        /// <param name="identityNumber">Client's identity number (MBR/JMBG)</param>
        /// <returns>CoreId if client exists</returns>
        [HttpGet("ClientExists/{identityNumber}")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult ClientExists(string identityNumber)
        {
            _logger.LogInformation("ClientExists called with IdentityNumber: {IdentityNumber}", identityNumber);

            // Handle error simulation
            if (identityNumber.Equals("notfound", StringComparison.OrdinalIgnoreCase) ||
                identityNumber == "0" || identityNumber == "0000000000")
            {
                return NotFound($"Client with IdentityNumber '{identityNumber}' not found");
            }

            if (identityNumber.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(500, "Internal server error occurred");
            }

            // Generate mock CoreId
            var coreId = $"CORE{new Random(identityNumber.GetHashCode()).Next(1000, 9999)}";
            return Ok(coreId);
        }

        /// <summary>
        /// Returns client's identity number based on CoreId
        /// </summary>
        /// <param name="coreId">Core identifier</param>
        /// <returns>Identity number (MBR/JMBG)</returns>
        [HttpGet("GetClientIdentityNumber/{coreId}")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetClientIdentityNumber(string coreId)
        {
            _logger.LogInformation("GetClientIdentityNumber called with CoreId: {CoreId}", coreId);

            // Handle error simulation
            if (coreId.Equals("notfound", StringComparison.OrdinalIgnoreCase) ||
                coreId == "0" || coreId == "0000000000")
            {
                return NotFound($"Client with CoreId '{coreId}' not found");
            }

            if (coreId.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(500, "Internal server error occurred");
            }

            // Generate mock identity number
            var identityNumber = $"ID{new Random(coreId.GetHashCode()).Next(100000, 999999)}";
            return Ok(identityNumber);
        }
    }
}
