namespace Migration.Abstraction.Configuration
{
    
    public class FolderNodeTypeMappingConfig
    {
        
        public string ClientFL { get; set; } = "cm:folder";        
        public string ClientPL { get; set; } = "cm:folder";
        public string AccountPackage { get; set; } = "cm:folder";       
        public string Deposit { get; set; } = "cm:folder";
        public string Other { get; set; } = "cm:folder";
        public string Default { get; set; } = "cm:folder";

        
        public string GetNodeType(int dossierTypeCode)
        {
            return dossierTypeCode switch
            {
                500 => ClientFL,        // ClientFL (PI)
                400 => ClientPL,        // ClientPL (LE)
                300 => AccountPackage,  // AccountPackage (ACC)
                700 => Deposit,         // Deposit (DE)
                _ => Default
            };
        }
    }
}
