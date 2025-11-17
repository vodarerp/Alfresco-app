using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Models
{
    public class UniqueFolderInfo
    {
        /// <summary>
        /// The root folder ID where the folder structure begins (DOSSIER folder)
        /// Example: "PI", "LE", "D" folder IDs
        /// </summary>
        public string DestinationRootId { get; set; } = string.Empty;

        /// <summary>
        /// The relative folder path from DestinationRootId
        /// Example: "ACC-12345/2024/01"
        /// </summary>
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Optional properties to set on the created folder
        /// </summary>
        public Dictionary<string, object>? Properties { get; set; }

        /// <summary>
        /// Cache key for folder lookup (TargetDossierType_DossierDestFolderId)
        /// Example: "500_PI102206"
        /// </summary>
        public string CacheKey { get; set; } = string.Empty;
    }
}
