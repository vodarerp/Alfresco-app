using Alfresco.Contracts.Oracle.Models;
using System.Collections.Generic;

namespace Alfresco.Contracts.Extensions
{
    /// <summary>
    /// Extension methods for working with FolderStaging
    /// </summary>
    public static class FolderStagingExtensions
    {
        /// <summary>
        /// Converts FolderStaging client properties to Alfresco custom properties format (ecm: prefix)
        /// </summary>
        /// <param name="folder">FolderStaging with client properties</param>
        /// <returns>Dictionary with ecm: prefixed keys for Alfresco API, or null if no client data</returns>
        public static Dictionary<string, object>? ToAlfrescoProperties(this FolderStaging? folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.CoreId))
                return null;

            var alfrescoProperties = new Dictionary<string, object>();

            //alfrescoProperties["ecm:TestError123"] = "Test123321";
            // Only add non-null values
            if (!string.IsNullOrWhiteSpace(folder.CoreId))
                alfrescoProperties["ecm:coreId"] = folder.CoreId;

            if (!string.IsNullOrWhiteSpace(folder.MbrJmbg))
                alfrescoProperties["ecm:mbrJmbg"] = folder.MbrJmbg;

            if (!string.IsNullOrWhiteSpace(folder.ClientName))
                alfrescoProperties["ecm:clientName"] = folder.ClientName;

            if (!string.IsNullOrWhiteSpace(folder.ClientType))
                alfrescoProperties["ecm:clientType"] = folder.ClientType;

            if (!string.IsNullOrWhiteSpace(folder.ClientSubtype))
                alfrescoProperties["ecm:clientSubtype"] = folder.ClientSubtype;

            if (!string.IsNullOrWhiteSpace(folder.Residency))
                alfrescoProperties["ecm:residency"] = folder.Residency;

            if (!string.IsNullOrWhiteSpace(folder.Segment))
                alfrescoProperties["ecm:segment"] = folder.Segment;

            if (!string.IsNullOrWhiteSpace(folder.Staff))
                alfrescoProperties["ecm:staff"] = folder.Staff;

            if (!string.IsNullOrWhiteSpace(folder.OpuUser))
                alfrescoProperties["ecm:opuUser"] = folder.OpuUser;

            if (!string.IsNullOrWhiteSpace(folder.OpuRealization))
                alfrescoProperties["ecm:opuRealization"] = folder.OpuRealization;

            if (!string.IsNullOrWhiteSpace(folder.Barclex))
                alfrescoProperties["ecm:barclex"] = folder.Barclex;

            if (!string.IsNullOrWhiteSpace(folder.Collaborator))
                alfrescoProperties["ecm:collaborator"] = folder.Collaborator;

            return alfrescoProperties.Count > 0 ? alfrescoProperties : null;
        }
    }
}
