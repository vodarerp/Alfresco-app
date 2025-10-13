using Alfresco.Contracts.Oracle.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Service for handling document type transformations during migration.
    /// Per documentation: Documents with "nova verzija" policy get suffix "migracija"
    /// during migration, then transform to final type for active documents after migration completes.
    /// </summary>
    public interface IDocumentTypeTransformationService
    {
        /// <summary>
        /// Determines migration and final document types based on retention policy.
        /// Per documentation line 31-34: Documents with "nova verzija" policy get
        /// new types with "migracija" suffix, then transform to final type if become active.
        /// </summary>
        /// <param name="document">Document to analyze</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Document with DocumentTypeMigration and FinalDocumentType populated</returns>
        Task<DocStaging> DetermineDocumentTypesAsync(
            DocStaging document,
            CancellationToken ct = default);

        /// <summary>
        /// Transforms active documents from migration types to final types after migration completes.
        /// Per documentation line 107-112: Active documents with "migracija" suffix
        /// should be transformed to final type (e.g., 00824-migracija -> 00099)
        ///
        /// This should be run AFTER complete migration from all sources.
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Number of documents transformed</returns>
        Task<int> TransformActiveDocumentsAsync(CancellationToken ct = default);

        /// <summary>
        /// Checks if document type has "nova verzija" retention policy.
        /// These documents require migration type suffix.
        /// </summary>
        /// <param name="documentType">Document type code (e.g., "00099", "00824")</param>
        /// <returns>True if document has versioning policy</returns>
        bool HasVersioningPolicy(string documentType);

        /// <summary>
        /// Gets final document type from migration document type.
        /// Example: "00824" (migration type) -> "00099" (final type)
        /// </summary>
        /// <param name="migrationDocumentType">Migration document type code</param>
        /// <returns>Final document type code, or null if no mapping exists</returns>
        string? GetFinalDocumentType(string migrationDocumentType);
    }
}
