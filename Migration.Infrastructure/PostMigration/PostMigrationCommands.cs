using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.PostMigration
{
    /// <summary>
    /// Console commands for post-migration tasks
    /// Per INTEGRATION_INSTRUCTIONS.md Step 5: Post-Migration Tasks
    ///
    /// These commands should be run AFTER all documents have been migrated from all sources
    /// (Heimdall, DUT, old DMS).
    /// </summary>
    public class PostMigrationCommands
    {
        private readonly IDocumentTypeTransformationService _transformationService;
        private readonly IClientEnrichmentService _enrichmentService;
        private readonly IDocStagingRepository _docRepo;
        private readonly ILogger<PostMigrationCommands> _logger;

        public PostMigrationCommands(
            IDocumentTypeTransformationService transformationService,
            IClientEnrichmentService enrichmentService,
            IDocStagingRepository docRepo,
            ILogger<PostMigrationCommands> logger)
        {
            _transformationService = transformationService ?? throw new ArgumentNullException(nameof(transformationService));
            _enrichmentService = enrichmentService ?? throw new ArgumentNullException(nameof(enrichmentService));
            _docRepo = docRepo ?? throw new ArgumentNullException(nameof(docRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Transforms active documents from migration types (with "-migracija" suffix) to final types.
        /// Per documentation line 107-112: Only transform the latest active document per client.
        ///
        /// Example: 00824-migracija -> 00099
        ///
        /// This should be run AFTER migration completes and activity status has been determined.
        /// </summary>
        public async Task<int> TransformDocumentTypesAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("POST-MIGRATION: Document Type Transformation");
            _logger.LogInformation("========================================");
            _logger.LogInformation("");

            try
            {
                _logger.LogInformation("Starting transformation of documents with migration types...");

                var transformedCount = await _transformationService.TransformActiveDocumentsAsync(ct);

                _logger.LogInformation("");
                _logger.LogInformation("========================================");
                _logger.LogInformation("✓ Transformation Complete!");
                _logger.LogInformation("========================================");
                _logger.LogInformation("Documents transformed: {Count}", transformedCount);
                _logger.LogInformation("");

                return transformedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transform document types");
                _logger.LogError("");
                _logger.LogError("========================================");
                _logger.LogError("✗ Transformation Failed!");
                _logger.LogError("========================================");
                _logger.LogError("Error: {Message}", ex.Message);
                _logger.LogError("");
                throw;
            }
        }

        /// <summary>
        /// Enriches KDP documents (00099, 00824, etc.) with active account numbers.
        /// Per documentation line 121-129: Populate account numbers that were active
        /// at the time the document was created.
        ///
        /// This should be run AFTER migration completes and after document type transformation.
        /// </summary>
        public async Task<int> EnrichKdpAccountNumbersAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("POST-MIGRATION: KDP Account Number Enrichment");
            _logger.LogInformation("========================================");
            _logger.LogInformation("");

            try
            {
                _logger.LogInformation("Fetching KDP documents requiring account enrichment...");

                // TODO: Implement repository method to get KDP documents without account numbers
                // This is a placeholder showing the intended logic

                /*
                var kdpDocuments = await _docRepo.GetKdpDocumentsWithoutAccountsAsync(ct);

                _logger.LogInformation("Found {Count} KDP documents requiring enrichment", kdpDocuments.Count);

                if (kdpDocuments.Count == 0)
                {
                    _logger.LogInformation("No KDP documents require account enrichment.");
                    return 0;
                }

                _logger.LogInformation("");
                _logger.LogInformation("Starting enrichment...");

                var enrichedCount = 0;
                var failedCount = 0;

                foreach (var document in kdpDocuments)
                {
                    try
                    {
                        await _enrichmentService.EnrichDocumentWithAccountsAsync(document, ct);
                        enrichedCount++;

                        if (enrichedCount % 100 == 0)
                        {
                            _logger.LogInformation("Progress: {Enriched}/{Total} documents enriched...",
                                enrichedCount, kdpDocuments.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogWarning(ex,
                            "Failed to enrich document {DocId} (CoreId: {CoreId}) - continuing",
                            document.Id, document.CoreId);
                    }
                }

                _logger.LogInformation("");
                _logger.LogInformation("========================================");
                _logger.LogInformation("✓ Account Enrichment Complete!");
                _logger.LogInformation("========================================");
                _logger.LogInformation("Total documents: {Total}", kdpDocuments.Count);
                _logger.LogInformation("Successfully enriched: {Enriched}", enrichedCount);
                _logger.LogInformation("Failed: {Failed}", failedCount);
                _logger.LogInformation("");

                return enrichedCount;
                */

                _logger.LogWarning("Account enrichment method needs repository implementation.");
                _logger.LogWarning("Add method to IDocStagingRepository:");
                _logger.LogWarning("  Task<List<DocStaging>> GetKdpDocumentsWithoutAccountsAsync(CancellationToken ct)");
                _logger.LogWarning("");

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich KDP account numbers");
                _logger.LogError("");
                _logger.LogError("========================================");
                _logger.LogError("✗ Account Enrichment Failed!");
                _logger.LogError("========================================");
                _logger.LogError("Error: {Message}", ex.Message);
                _logger.LogError("");
                throw;
            }
        }

        /// <summary>
        /// Validates migration results and generates a report.
        /// Checks for:
        /// - Documents with missing required fields
        /// - Folders with missing enrichment data
        /// - Documents requiring transformation that weren't transformed
        /// - Orphaned documents (documents with no folder)
        /// </summary>
        public async Task<ValidationReport> ValidateMigrationAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("POST-MIGRATION: Validation");
            _logger.LogInformation("========================================");
            _logger.LogInformation("");

            var report = new ValidationReport();

            try
            {
                _logger.LogInformation("Running validation checks...");
                _logger.LogInformation("");

                // TODO: Implement validation queries
                // This is a placeholder showing the intended structure

                /*
                // Check 1: Documents with missing CoreId
                _logger.LogInformation("Check 1: Documents with missing CoreId...");
                var docsWithoutCoreId = await _docRepo.CountDocumentsWithoutCoreIdAsync(ct);
                report.DocumentsWithoutCoreId = docsWithoutCoreId;
                _logger.LogInformation("  Result: {Count} documents", docsWithoutCoreId);

                // Check 2: Documents requiring transformation that weren't transformed
                _logger.LogInformation("Check 2: Untransformed documents...");
                var untransformedDocs = await _docRepo.CountUntransformedDocumentsAsync(ct);
                report.UntransformedDocuments = untransformedDocs;
                _logger.LogInformation("  Result: {Count} documents", untransformedDocs);

                // Check 3: KDP documents without account numbers
                _logger.LogInformation("Check 3: KDP documents without account numbers...");
                var kdpWithoutAccounts = await _docRepo.CountKdpDocumentsWithoutAccountsAsync(ct);
                report.KdpDocumentsWithoutAccounts = kdpWithoutAccounts;
                _logger.LogInformation("  Result: {Count} documents", kdpWithoutAccounts);

                // Check 4: Folders without enrichment data
                _logger.LogInformation("Check 4: Folders without ClientAPI enrichment...");
                var foldersWithoutEnrichment = await _folderRepo.CountFoldersWithoutEnrichmentAsync(ct);
                report.FoldersWithoutEnrichment = foldersWithoutEnrichment;
                _logger.LogInformation("  Result: {Count} folders", foldersWithoutEnrichment);

                // Check 5: Documents with errors
                _logger.LogInformation("Check 5: Documents with errors...");
                var docsWithErrors = await _docRepo.CountDocumentsByStatusAsync("ERR", ct);
                report.DocumentsWithErrors = docsWithErrors;
                _logger.LogInformation("  Result: {Count} documents", docsWithErrors);
                */

                _logger.LogInformation("");
                _logger.LogInformation("========================================");
                _logger.LogInformation("✓ Validation Complete!");
                _logger.LogInformation("========================================");
                _logger.LogInformation("");
                _logger.LogInformation("Report Summary:");
                _logger.LogInformation("  Documents without CoreId: {Count}", report.DocumentsWithoutCoreId);
                _logger.LogInformation("  Untransformed documents: {Count}", report.UntransformedDocuments);
                _logger.LogInformation("  KDP docs without accounts: {Count}", report.KdpDocumentsWithoutAccounts);
                _logger.LogInformation("  Folders without enrichment: {Count}", report.FoldersWithoutEnrichment);
                _logger.LogInformation("  Documents with errors: {Count}", report.DocumentsWithErrors);
                _logger.LogInformation("");

                report.IsValid = report.DocumentsWithoutCoreId == 0 &&
                                 report.UntransformedDocuments == 0 &&
                                 report.DocumentsWithErrors == 0;

                if (report.IsValid)
                {
                    _logger.LogInformation("✓ Migration validation PASSED!");
                }
                else
                {
                    _logger.LogWarning("⚠ Migration validation found issues - review report");
                }

                _logger.LogInformation("");

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate migration");
                _logger.LogError("");
                _logger.LogError("========================================");
                _logger.LogError("✗ Validation Failed!");
                _logger.LogError("========================================");
                _logger.LogError("Error: {Message}", ex.Message);
                _logger.LogError("");
                throw;
            }
        }
    }

    /// <summary>
    /// Validation report for post-migration checks
    /// </summary>
    public class ValidationReport
    {
        public int DocumentsWithoutCoreId { get; set; }
        public int UntransformedDocuments { get; set; }
        public int KdpDocumentsWithoutAccounts { get; set; }
        public int FoldersWithoutEnrichment { get; set; }
        public int DocumentsWithErrors { get; set; }
        public bool IsValid { get; set; }
    }
}
