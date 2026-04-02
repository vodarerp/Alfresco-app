using System;

namespace Migration.Abstraction.Models
{
    
    public class ClientData
    {
        
        public string CoreId { get; set; } = string.Empty;     
        public string MbrJmbg { get; set; } = string.Empty;        
        public string ClientName { get; set; } = string.Empty;       
        public string ClientType { get; set; } = string.Empty;        
        public string ClientSubtype { get; set; } = string.Empty;              
        public string Residency { get; set; } = string.Empty;
        public string Segment { get; set; } = string.Empty;      
        public string? Staff { get; set; }
        public string? OpuUser { get; set; }        
        public string? OpuRealization { get; set; }        
        public string? Barclex { get; set; }     
        public string? Collaborator { get; set; }       
        public string? BarCLEXName { get; set; }        
        public string? BarCLEXOpu { get; set; }        
        public string? BarCLEXGroupName { get; set; }
        public string? BarCLEXGroupCode { get; set; }        
        public string? BarCLEXCode { get; set; }
        public bool HasError { get; set; } = false;
        public string? ErrorMessage { get; set; }
    }
}
