using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Extensions.Oracle;
using Oracle.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Service for handling document type transformations during migration.
    ///
    /// IMPORTANT: This service handles the complex document type mapping logic per documentation.
    /// Review the TypeMappings dictionary and update based on final mapping table from business.
    /// </summary>
    public class DocumentTypeTransformationService : IDocumentTypeTransformationService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<DocumentTypeTransformationService> _logger;

        /// <summary>
        /// Mapping: Migration Document Type -> Final Document Type
        /// Per documentation line 25-26, 31-34, 67-68, 76-77, 84-85, 107-108
        ///
        /// IMPORTANT: Update this mapping based on final business mapping table!
        /// Current mappings are from documentation examples.
        /// </summary>
        private static readonly Dictionary<string, string> TypeMappings = new()
        {
            // KDP for Natural Persons (FL)
            // Per doc line 67-68: 00824 (migracija) -> 00099 (final)
            { "00824", "00099" },

            // KDP for Authorized Persons (FL)
            // Per doc line 76-77: 00825 (migracija) -> 00101 (final)
            { "00825", "00101" },

            // KDP for Legal Entities (PL)
            // Per doc line 84-85: 00827 (migracija) -> 00100 (final)
            { "00827", "00100" },

            // KYC Questionnaire
            // Per doc line 107-108: 00841 (migracija) -> 00130 (final)
            { "00841", "00130" }

            // TODO: Add other document types from mapping table when available
            // Review "Analiza_za_migr_novo – mapiranje v3.xlsx" column C (migration type)
            // and column G (final type) for complete mappings
        };

        public DocumentTypeTransformationService(
            IServiceProvider sp,
            ILogger<DocumentTypeTransformationService> logger)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<DocStaging> DetermineDocumentTypesAsync(
            DocStaging document,
            CancellationToken ct = default)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (string.IsNullOrWhiteSpace(document.DocumentType))
            {
                _logger.LogWarning(
                    "Document {DocId} has no DocumentType set - cannot determine migration types",
                    document.Id);
                return Task.FromResult(document);
            }

            try
            {
                // Check if document has "nova verzija" retention policy
                if (HasVersioningPolicy(document.DocumentType))
                {
                    // Per doc line 31-34: Add "migracija" suffix for versioned documents
                    document.DocumentTypeMigration = $"{document.DocumentType}-migracija";
                    document.RequiresTypeTransformation = true;

                    // Determine final type after transformation
                    document.FinalDocumentType = GetFinalDocumentType(document.DocumentType);

                    // Per doc line 95-97: Migrated documents with suffix start as inactive
                    document.IsActive = false;

                    _logger.LogDebug(
                        "Document {DocId} type {DocType} has versioning policy: " +
                        "MigrationType={MigrationType}, FinalType={FinalType}, IsActive=false",
                        document.Id, document.DocumentType, document.DocumentTypeMigration,
                        document.FinalDocumentType);
                }
                else
                {
                    // Per doc line 113-115: "novi dokument" policy - no suffix, remains active
                    document.DocumentTypeMigration = document.DocumentType;
                    document.RequiresTypeTransformation = false;
                    document.FinalDocumentType = document.DocumentType;
                    // IsActive stays as set (typically true for "novi dokument" if was active in old system)

                    _logger.LogDebug(
                        "Document {DocId} type {DocType} has 'novi dokument' policy - no transformation needed",
                        document.Id, document.DocumentType);
                }

                return Task.FromResult(document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to determine document types for document {DocId}, type {DocType}",
                    document.Id, document.DocumentType);
                throw;
            }
        }

        public async Task<int> TransformActiveDocumentsAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Starting transformation of active documents from migration types to final types");

            var transformedCount = 0;

            await using var scope = _sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

            await uow.BeginAsync(ct: ct).ConfigureAwait(false);

            try
            {
                // Get all documents requiring transformation that are now active
                // Per doc line 107-112: Transform only active documents with migration suffix
                var documentsToTransform = await GetDocumentsRequiringTransformationAsync(docRepo, ct)
                    .ConfigureAwait(false);

                if (!documentsToTransform.Any())
                {
                    _logger.LogInformation("No documents require type transformation");
                    await uow.CommitAsync(ct: ct).ConfigureAwait(false);
                    return 0;
                }

                _logger.LogInformation(
                    "Found {Count} documents requiring type transformation",
                    documentsToTransform.Count);

                // Group by CoreId and DocumentType to find latest document per client
                var groupedDocs = documentsToTransform
                    .GroupBy(d => new { d.CoreId, d.DocumentType });

                foreach (var group in groupedDocs)
                {
                    // Per doc line 111-112: Determine latest document by creation date
                    var latestDoc = group
                        .OrderByDescending(d => d.OriginalCreatedAt ?? d.CreatedAt)
                        .First();

                    if (string.IsNullOrWhiteSpace(latestDoc.FinalDocumentType))
                    {
                        _logger.LogWarning(
                            "Document {DocId} has no FinalDocumentType set - skipping transformation",
                            latestDoc.Id);
                        continue;
                    }

                    // Transform type from migration to final
                    var oldType = latestDoc.DocumentType;
                    latestDoc.DocumentType = latestDoc.FinalDocumentType;
                    latestDoc.IsActive = true;

                    await docRepo.UpdateAsync(latestDoc, ct).ConfigureAwait(false);

                    transformedCount++;

                    _logger.LogInformation(
                        "Transformed document {DocId} from type {OldType} to {NewType} (CoreId: {CoreId})",
                        latestDoc.Id, oldType, latestDoc.DocumentType, latestDoc.CoreId);
                }

                await uow.CommitAsync(ct: ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Successfully transformed {Count} documents from migration types to final types",
                    transformedCount);

                return transformedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transform active documents");
                await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
                throw;
            }
        }

        public bool HasVersioningPolicy(string documentType)
        {
            if (string.IsNullOrWhiteSpace(documentType))
            {
                return false;
            }

            // Document has versioning policy if it's in the mapping dictionary
            return TypeMappings.ContainsKey(documentType);
        }

        public string? GetFinalDocumentType(string migrationDocumentType)
        {
            if (string.IsNullOrWhiteSpace(migrationDocumentType))
            {
                return null;
            }

            return TypeMappings.TryGetValue(migrationDocumentType, out var finalType)
                ? finalType
                : null;
        }

        /// <summary>
        /// Retrieves documents from staging that require type transformation.
        /// These are documents marked with RequiresTypeTransformation = true.
        /// </summary>
        private async Task<List<DocStaging>> GetDocumentsRequiringTransformationAsync(
            IDocStagingRepository docRepo,
            CancellationToken ct)
        {
            // TODO: Add repository method to efficiently query documents requiring transformation
            // For now, this is a placeholder that should be implemented in IDocStagingRepository

            _logger.LogWarning(
                "GetDocumentsRequiringTransformationAsync needs repository implementation - " +
                "Add method to IDocStagingRepository: " +
                "Task<List<DocStaging>> GetDocumentsRequiringTransformationAsync(CancellationToken ct)");

            // Placeholder: Return empty list until repository method is implemented
            // In real implementation, this should query:
            // SELECT * FROM DOC_STAGING
            // WHERE REQUIRES_TYPE_TRANSFORMATION = 1
            //   AND IS_ACTIVE = 1
            //   AND STATUS = 'DONE'

            return new List<DocStaging>();
        }
    }

    // TODO: Add this extension method to IDocStagingRepository
    // This is a placeholder showing what needs to be added:
    /*
    public static class DocStagingRepositoryExtensions
    {
        public static async Task<List<DocStaging>> GetDocumentsRequiringTransformationAsync(
            this IDocStagingRepository repo,
            CancellationToken ct = default)
        {
            // Implementation should query documents where:
            // - RequiresTypeTransformation = true
            // - IsActive = true (these are the ones that became active)
            // - Status = DONE (successfully migrated)

            throw new NotImplementedException(
                "Add this method to IDocStagingRepository and Oracle repository implementation");
        }

        public static async Task UpdateAsync(
            this IDocStagingRepository repo,
            DocStaging document,
            CancellationToken ct = default)
        {
            // Implementation should update document in database

            throw new NotImplementedException(
                "Add this method to IDocStagingRepository and Oracle repository implementation");
        }
    }
    */
}
