using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CA_MockData
{
    public sealed class Config
    {
        public string BaseUrl { get; set; } = default!;
        public string Username { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string RootParentId { get; set; } = default!;
        public int FolderCount { get; set; }
        public int DocsPerFolder { get; set; }
        public int DegreeOfParallelism { get; set; }
        public int MaxRetries { get; set; }
        public int RetryBaseDelayMs { get; set; }

        /// <summary>
        /// If true, creates folder structure: ROOT -> dosie-{Type} -> {Type}{CoreId}
        /// If false, uses old structure: ROOT -> MockFolders-{Index}
        /// </summary>
        public bool UseNewFolderStructure { get; set; } = false;

        /// <summary>
        /// Client types to create (PI, LE, DE). Used only if UseNewFolderStructure = true
        /// PI = Physical Individual (Fizičko lice)
        /// LE = Legal Entity (Pravno lice)
        /// DE = Deposit Dossier (Dosije depozita) - created separately with contract number
        /// NOTE: ACC (Account Package) is NOT included - those dossiers are created DURING migration
        /// </summary>
        public string[] ClientTypes { get; set; } = new[] { "PI", "LE" };

        /// <summary>
        /// Starting CoreId for generating mock data. Default: 10000000
        /// </summary>
        public int StartingCoreId { get; set; } = 10000000;

        /// <summary>
        /// If true, adds custom properties/metadata to folders
        /// </summary>
        public bool AddFolderProperties { get; set; } = false;
    }
}
