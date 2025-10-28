using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;

namespace Alfresco.Contracts.Extensions
{
    /// <summary>
    /// Extension methods for working with ClientProperties
    /// </summary>
    public static class ClientPropertiesExtensions
    {
        /// <summary>
        /// Parses Alfresco custom properties (ecm:* prefix) into ClientProperties model
        /// </summary>
        /// <param name="alfrescoProperties">Raw properties dictionary from Alfresco API</param>
        /// <returns>ClientProperties populated from Alfresco properties, or null if no client properties found</returns>
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
                MbrJmbg = GetStringValue(alfrescoProperties, "ecm:mbrJmbg"),
                ClientName = GetStringValue(alfrescoProperties, "ecm:clientName"),
                ClientType = GetStringValue(alfrescoProperties, "ecm:clientType"),
                ClientSubtype = GetStringValue(alfrescoProperties, "ecm:clientSubtype"),
                Residency = GetStringValue(alfrescoProperties, "ecm:residency"),
                Segment = GetStringValue(alfrescoProperties, "ecm:segment"),
                Staff = GetStringValue(alfrescoProperties, "ecm:staff"),
                OpuUser = GetStringValue(alfrescoProperties, "ecm:opuUser"),
                OpuRealization = GetStringValue(alfrescoProperties, "ecm:opuRealization"),
                Barclex = GetStringValue(alfrescoProperties, "ecm:barclex"),
                Collaborator = GetStringValue(alfrescoProperties, "ecm:collaborator")
            };

            return clientProps;
        }

        /// <summary>
        /// Populates ClientProperties on an Entry from its Alfresco properties
        /// </summary>
        /// <param name="entry">Entry to populate</param>
        public static void PopulateClientProperties(this Entry entry)
        {
            if (entry.Properties != null && entry.Properties.Count > 0)
            {
                entry.ClientProperties = entry.Properties.ParseFromAlfrescoProperties();
            }
        }

        /// <summary>
        /// Checks if Entry has client properties populated (either from Alfresco or ClientAPI)
        /// </summary>
        public static bool HasClientProperties(this Entry entry)
        {
            return entry.ClientProperties?.HasClientData == true;
        }

        /// <summary>
        /// Tries to extract CoreId from Entry name (e.g., "PL-10000123" -> "10000123")
        /// </summary>
        public static string? TryExtractCoreIdFromName(this Entry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                return null;

            // Expected format: {ClientType}-{CoreId}TTT
            // Examples: PL-10000123TTT, FL-10000456TTT, ACC-10000789TTT
            var parts = entry.Name.Split('-');
            if (parts.Length >= 2)
            {
                // Remove "TTT" suffix if present
                var coreIdPart = parts[1].Replace("TTT", "");

                // Verify it's numeric
                if (long.TryParse(coreIdPart, out _))
                {
                    return coreIdPart;
                }
            }

            return null;
        }

        /// <summary>
        /// Converts ClientProperties to Alfresco custom properties format (ecm: prefix)
        /// </summary>
        /// <param name="clientProperties">ClientProperties to convert</param>
        /// <returns>Dictionary with ecm: prefixed keys for Alfresco API</returns>
        public static Dictionary<string, object>? ToAlfrescoProperties(this ClientProperties? clientProperties)
        {
            if (clientProperties == null || !clientProperties.HasClientData)
                return null;

            var alfrescoProperties = new Dictionary<string, object>();
            //alfrescoProperties["ecm:TestError123"] = "Test123321";
            // Only add non-null values
            if (!string.IsNullOrWhiteSpace(clientProperties.CoreId))
                alfrescoProperties["ecm:coreId"] = clientProperties.CoreId;

            if (!string.IsNullOrWhiteSpace(clientProperties.MbrJmbg))
                alfrescoProperties["ecm:mbrJmbg"] = clientProperties.MbrJmbg;

            if (!string.IsNullOrWhiteSpace(clientProperties.ClientName))
                alfrescoProperties["ecm:clientName"] = clientProperties.ClientName;

            if (!string.IsNullOrWhiteSpace(clientProperties.ClientType))
                alfrescoProperties["ecm:clientType"] = clientProperties.ClientType;

            if (!string.IsNullOrWhiteSpace(clientProperties.ClientSubtype))
                alfrescoProperties["ecm:clientSubtype"] = clientProperties.ClientSubtype;

            if (!string.IsNullOrWhiteSpace(clientProperties.Residency))
                alfrescoProperties["ecm:residency"] = clientProperties.Residency;

            if (!string.IsNullOrWhiteSpace(clientProperties.Segment))
                alfrescoProperties["ecm:segment"] = clientProperties.Segment;

            if (!string.IsNullOrWhiteSpace(clientProperties.Staff))
                alfrescoProperties["ecm:staff"] = clientProperties.Staff;

            if (!string.IsNullOrWhiteSpace(clientProperties.OpuUser))
                alfrescoProperties["ecm:opuUser"] = clientProperties.OpuUser;

            if (!string.IsNullOrWhiteSpace(clientProperties.OpuRealization))
                alfrescoProperties["ecm:opuRealization"] = clientProperties.OpuRealization;

            if (!string.IsNullOrWhiteSpace(clientProperties.Barclex))
                alfrescoProperties["ecm:barclex"] = clientProperties.Barclex;

            if (!string.IsNullOrWhiteSpace(clientProperties.Collaborator))
                alfrescoProperties["ecm:collaborator"] = clientProperties.Collaborator;

            return alfrescoProperties.Count > 0 ? alfrescoProperties : null;
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
