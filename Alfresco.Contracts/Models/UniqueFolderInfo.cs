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

        /// <summary>
        /// Product type code from DocumentMapping.TipProizvoda
        /// Example: "00008" (FL deposits), "00010" (PL deposits)
        /// Used for ecm:bnkTypeOfProduct property
        /// </summary>
        public string? TipProizvoda { get; set; }

        /// <summary>
        /// CoreId extracted from folder name or document
        /// Used for ecm:CoreId property
        /// </summary>
        public string? CoreId { get; set; }

        /// <summary>
        /// Document creation date (OriginalCreatedAt)
        /// Used for creating ecm:bnkNumberOfControcat in YYYYMMDD format
        /// </summary>
        public DateTime? CreationDate { get; set; }

        /// <summary>
        /// Target dossier type (300, 400, 500, 700, etc.)
        /// Used to determine if folder needs special properties (e.g., deposits = 700)
        /// </summary>
        public int? TargetDossierType { get; set; }

        /// <summary>
        /// Flag indicating whether this folder was created during migration
        /// TRUE = folder was created (didn't exist on Alfresco before)
        /// FALSE = folder already existed on Alfresco
        /// Populated after folder resolution
        /// </summary>
        public bool IsCreated { get; set; }
    }
}
