using System;
using System.Collections.Generic;
using System.Linq;

namespace Alfresco.Contracts.Mapper
{
    /// <summary>
    /// Maps ecm:opisDokumenta (document description) to ecm:tipDokumenta (document type code)
    /// Supports both Serbian and English descriptions from old Alfresco system
    ///
    /// VERZIJA 2.0: Koristi HeimdallDocumentMapper kao centralni izvor podataka
    /// </summary>
    public static class OpisToTipMapper
    {

        /// <summary>
        /// Gets the document type code (ecm:tipDokumenta) from document description (ecm:opisDokumenta)
        /// NOVA LOGIKA: Koristi HeimdallDocumentMapper za mapiranje
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco (ecm:docDesc)</param>
        /// <returns>Document type code (SifraDocMigracija) or "UNKNOWN" if not found</returns>
        public static string GetTipDokumenta(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return "UNKNOWN";

            // Try to find by original name (Naziv field)
            var mapping = HeimdallDocumentMapper.FindByOriginalName(opisDokumenta);

            if (mapping != null)
            {
                return mapping.Value.SifraDocMigracija;
            }

            // Try to find by Serbian name (NazivDoc field)
            var mappingBySerbianName = HeimdallDocumentMapper.DocumentMappings
                .FirstOrDefault(m => m.NazivDoc.Equals(opisDokumenta?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (mappingBySerbianName.Naziv != null)
            {
                return mappingBySerbianName.SifraDocMigracija;
            }

            // Try to find by migrated name (NazivDocMigracija field) - supports "- migracija" suffix
            var mappingByMigratedName = HeimdallDocumentMapper.DocumentMappings
                .FirstOrDefault(m => m.NazivDocMigracija.Equals(opisDokumenta?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (mappingByMigratedName.Naziv != null)
            {
                return mappingByMigratedName.SifraDocMigracija;
            }

            return "UNKNOWN";
        }

        /// <summary>
        /// Checks if the given document description has a known mapping
        /// NOVA LOGIKA: Koristi HeimdallDocumentMapper za proveru
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <returns>True if mapping exists, false otherwise</returns>
        public static bool IsKnownOpis(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            var tipDokumenta = GetTipDokumenta(opisDokumenta);
            return tipDokumenta != "UNKNOWN";
        }

        /// <summary>
        /// Gets all registered mappings from HeimdallDocumentMapper (for debugging/testing purposes)
        /// </summary>
        /// <returns>Dictionary of all mappings (Naziv/NazivDoc → SifraDocMigracija)</returns>
        public static IReadOnlyDictionary<string, string> GetAllMappings()
        {
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in HeimdallDocumentMapper.DocumentMappings)
            {
                // Add English name mapping
                if (!mappings.ContainsKey(mapping.Naziv))
                {
                    mappings[mapping.Naziv] = mapping.SifraDocMigracija;
                }

                // Add Serbian name mapping
                if (!mappings.ContainsKey(mapping.NazivDoc))
                {
                    mappings[mapping.NazivDoc] = mapping.SifraDocMigracija;
                }

                // Add migrated name mapping (with "- migracija" suffix)
                if (!mappings.ContainsKey(mapping.NazivDocMigracija))
                {
                    mappings[mapping.NazivDocMigracija] = mapping.SifraDocMigracija;
                }
            }

            return mappings;
        }

        /// <summary>
        /// Gets the full mapping info from HeimdallDocumentMapper for given document description
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <returns>Full mapping tuple or null if not found</returns>
        public static (string Naziv, string SifraDoc, string NazivDoc, string TipDosiea, string SifraDocMigracija, string NazivDocMigracija)? GetFullMapping(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return null;

            // Try all search methods
            var mapping = HeimdallDocumentMapper.FindByOriginalName(opisDokumenta);

            if (mapping == null)
            {
                mapping = HeimdallDocumentMapper.DocumentMappings
                    .FirstOrDefault(m => m.NazivDoc.Equals(opisDokumenta?.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (mapping == null || mapping.Value.Naziv == null)
            {
                mapping = HeimdallDocumentMapper.DocumentMappings
                    .FirstOrDefault(m => m.NazivDocMigracija.Equals(opisDokumenta?.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            return mapping?.Naziv != null ? mapping : null;
        }
    }
}
