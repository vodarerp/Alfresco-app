using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Alfresco.Contracts.Extensions
{
   
    public static class ClientPropertiesExtensions
    {
        public static ClientProperties? ParseFromAlfrescoProperties(this Dictionary<string, object>? alfrescoProperties)
        {
            if (alfrescoProperties == null || alfrescoProperties.Count == 0)
                return null;

            // Check if we have at least CoreId (ecm:coreId)
            if (!alfrescoProperties.ContainsKey("ecm:coreId"))
                return null;

            var clientProps = new ClientProperties
            {
                CoreId = GetStringValue(alfrescoProperties, "ecm:coreId"),
                MbrJmbg = GetStringValue(alfrescoProperties, "ecm:jmbg"),
                ClientName = GetStringValue(alfrescoProperties, "ecm:clientName"),
                ClientType = GetStringValue(alfrescoProperties, "ecm:docClientType"),
                ClientSubtype = GetStringValue(alfrescoProperties, "ecm:docClientSubtype"),
                //Residency = GetStringValue(alfrescoProperties, "ecm:residency"),
                //Segment = GetStringValue(alfrescoProperties, "ecm:segment"),
                Staff = GetStringValue(alfrescoProperties, "ecm:bnkStaff"),
                OpuUser = GetStringValue(alfrescoProperties, "ecm:opuUser"),
                OpuRealization = GetStringValue(alfrescoProperties, "ecm:opuRealization"),
                Barclex = GetStringValue(alfrescoProperties, "ecm:barclex"),
                Collaborator = GetStringValue(alfrescoProperties, "ecm:collaborator")
            };

            return clientProps;
        }
      
        public static void PopulateClientProperties(this Entry entry)
        {
            if (entry.Properties != null && entry.Properties.Count > 0)
            {
                entry.ClientProperties = entry.Properties.ParseFromAlfrescoProperties();
            }
        }
       
        public static bool HasClientProperties(this Entry entry)
        {
            return entry.ClientProperties?.HasClientData == true;
        }


        public static string? TryExtractCoreIdFromName(string? folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return null;

            string normalizedName = folderName.ToUpper();

            // Pattern 1: {Prefix}{CoreId} ili {Prefix}-{CoreId} sa opcionalnim sufiksom
            // Prefix: 2-3 slova, CoreId: sve cifre do prvog ne-cifra znaka
            // Examples: PL10000123, PI123321-211, PI123321-111-222, DE10101-0009_123321, PI-102206
            var match1 = Regex.Match(normalizedName, @"^[A-Z]{2,3}-?(\d+)");
            if (match1.Success)
                return match1.Groups[1].Value;

            // Pattern 2: FALLBACK - Samo digits na poèetku
            // Example: 102206, 123456-789
            var match2 = Regex.Match(normalizedName, @"^(\d+)");
            if (match2.Success)
                return match2.Groups[1].Value;

            return null;
        }

        public static string? TryExtractCoreIdFromName_v2(this Entry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                return null;

            string folderName = entry.Name.ToUpper();

            // Expected format: {ClientType}-{CoreId}TTT
            // Examples: PL-10000123TTT, FL-10000456TTT, ACC-10000789TTT
            var match1 = Regex.Match(folderName, @"^([A-Z]{2,3})(\d{6,})$");
            if (match1.Success)
            {
                var coreId = match1.Groups[2].Value;
               
                return coreId;
            }

            // Pattern 2: FALLBACK - Stari format {prefix}-{digits} - PI-102206
            var match2 = Regex.Match(folderName, @"^([A-Z]{2,3})-(\d{6,})$");
            if (match2.Success)
            {
                var coreId = match2.Groups[2].Value;
               
                    
                return coreId;
            }

            // Pattern 3: ULTRA-FALLBACK - Samo digits (ako folder se zove "102206")
            var match3 = Regex.Match(folderName, @"^(\d{6,})$");
            if (match3.Success)
            {
                var coreId = match3.Groups[1].Value;
              
                return coreId;
            }

           

            return null;
        }

       

       

        #region Helper Methods

        private static string? GetStringValue(Dictionary<string, object> properties, string key)
        {
            if (properties.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
            return null;
        }

        #endregion
    }
}
