using System;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Service for generating unique folder identifiers for deposit folders.
    /// Per documentation line 156-163: Specific format required for deposit folder structure.
    /// </summary>
    public interface IUniqueFolderIdentifierService
    {
        /// <summary>
        /// Generates unique identifier for deposit subfolder (Jedinstveni identifikator dosijea).
        /// Per documentation line 159-163:
        /// Format: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}
        /// Example: DE-10194302-00008-10104302_20241105154459
        /// </summary>
        /// <param name="coreId">Client's Core ID</param>
        /// <param name="productType">Product type code (00008 for FL, 00010 for PL)</param>
        /// <param name="contractNumber">Contract number (Broj Ugovora)</param>
        /// <param name="processDate">Date when deposit was processed (for timestamp)</param>
        /// <returns>Unique identifier string</returns>
        string GenerateDepositIdentifier(
            string coreId,
            string productType,
            string contractNumber,
            DateTime processDate);

        /// <summary>
        /// Generates folder reference (parent folder identifier).
        /// Per documentation line 156:
        /// Format: DE-{CoreId}{ProductType}-{ContractNumber}
        /// Example: DE-1019430200008-10104302
        /// </summary>
        /// <param name="coreId">Client's Core ID</param>
        /// <param name="productType">Product type code</param>
        /// <param name="contractNumber">Contract number</param>
        /// <returns>Folder reference string</returns>
        string GenerateFolderReference(
            string coreId,
            string productType,
            string contractNumber);

        /// <summary>
        /// Parses unique identifier back to components (for validation/debugging).
        /// </summary>
        /// <param name="uniqueIdentifier">Unique identifier string</param>
        /// <returns>Parsed components (CoreId, ProductType, ContractNumber, Timestamp)</returns>
        (string CoreId, string ProductType, string ContractNumber, DateTime? ProcessDate) ParseIdentifier(
            string uniqueIdentifier);

        /// <summary>
        /// Validates if unique identifier follows correct format.
        /// </summary>
        /// <param name="uniqueIdentifier">Unique identifier to validate</param>
        /// <returns>True if valid format</returns>
        bool IsValidIdentifier(string uniqueIdentifier);
    }
}
