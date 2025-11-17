using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Migration.Infrastructure.Implementation.Mappers
{
    /// <summary>
    /// In-memory cache za document mappings.
    /// Učitava SVE mappinge JEDNOM iz baze i drži ih u memoriji za brze O(1) lookup-e.
    ///
    /// Performance: ~100 mappinga u memoriji = ~100 KB memorije
    /// Lookup time: O(1) = <1ms (umesto 10-50ms po SQL query-ju)
    /// </summary>
    public class DocumentMappingCache
    {
        private readonly Dictionary<string, string> _originalNameToCode;
        private readonly Dictionary<string, string> _serbianNameToCode;
        private readonly Dictionary<string, string> _migratedNameToCode;
        private readonly Dictionary<string, DocumentMapping> _allMappings;

        public int MappingCount => _allMappings.Count;

        /// <summary>
        /// Creates in-memory cache from all document mappings.
        /// This should be called ONCE at application startup.
        /// </summary>
        /// <param name="mappings">All document mappings from database</param>
        public DocumentMappingCache(IReadOnlyList<DocumentMapping> mappings)
        {
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));

            // Initialize dictionaries with case-insensitive comparers
            _originalNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _serbianNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _migratedNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _allMappings = new Dictionary<string, DocumentMapping>(StringComparer.OrdinalIgnoreCase);

            // Build lookup dictionaries
            foreach (var mapping in mappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija))
                    continue;

                var code = mapping.SifraDokumentaMigracija;

                // Add original name mapping (English)
                if (!string.IsNullOrWhiteSpace(mapping.Naziv))
                {
                    var key = mapping.Naziv.Trim();
                    if (!_originalNameToCode.ContainsKey(key))
                    {
                        _originalNameToCode[key] = code;
                        _allMappings[key] = mapping;
                    }
                }

                // Add Serbian name mapping
                if (!string.IsNullOrWhiteSpace(mapping.NazivDokumenta))
                {
                    var key = mapping.NazivDokumenta.Trim();
                    if (!_serbianNameToCode.ContainsKey(key))
                    {
                        _serbianNameToCode[key] = code;
                        if (!_allMappings.ContainsKey(key))
                            _allMappings[key] = mapping;
                    }
                }

                // Add migrated name mapping (with "- migracija" suffix)
                if (!string.IsNullOrWhiteSpace(mapping.NazivDokumentaMigracija))
                {
                    var key = mapping.NazivDokumentaMigracija.Trim();
                    if (!_migratedNameToCode.ContainsKey(key))
                    {
                        _migratedNameToCode[key] = code;
                        if (!_allMappings.ContainsKey(key))
                            _allMappings[key] = mapping;
                    }
                }
            }
        }

        /// <summary>
        /// Gets document type code from document description.
        /// Tries to match by original name, then Serbian name, then migrated name.
        /// </summary>
        /// <param name="opisDokumenta">Document description</param>
        /// <returns>Document type code or "UNKNOWN" if not found</returns>
        public string GetDocumentType(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return "UNKNOWN";

            var trimmed = opisDokumenta.Trim();

            // Try original name (English)
            if (_originalNameToCode.TryGetValue(trimmed, out var code))
                return code;

            // Try Serbian name
            if (_serbianNameToCode.TryGetValue(trimmed, out code))
                return code;

            // Try migrated name (with "- migracija" suffix)
            if (_migratedNameToCode.TryGetValue(trimmed, out code))
                return code;

            return "UNKNOWN";
        }

        /// <summary>
        /// Gets full document mapping from document description.
        /// </summary>
        /// <param name="opisDokumenta">Document description</param>
        /// <returns>Full mapping or null if not found</returns>
        public DocumentMapping? GetFullMapping(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return null;

            var trimmed = opisDokumenta.Trim();

            if (_allMappings.TryGetValue(trimmed, out var mapping))
                return mapping;

            return null;
        }

        /// <summary>
        /// Checks if document description has a known mapping.
        /// </summary>
        /// <param name="opisDokumenta">Document description</param>
        /// <returns>True if mapping exists, false otherwise</returns>
        public bool IsKnownOpis(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            var trimmed = opisDokumenta.Trim();

            return _originalNameToCode.ContainsKey(trimmed) ||
                   _serbianNameToCode.ContainsKey(trimmed) ||
                   _migratedNameToCode.ContainsKey(trimmed);
        }

        /// <summary>
        /// Gets all mappings as a read-only dictionary (for debugging/testing).
        /// </summary>
        /// <returns>Dictionary of all mappings (description → code)</returns>
        public IReadOnlyDictionary<string, string> GetAllMappingsAsDictionary()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _originalNameToCode)
                result[kvp.Key] = kvp.Value;

            foreach (var kvp in _serbianNameToCode)
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = kvp.Value;

            foreach (var kvp in _migratedNameToCode)
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = kvp.Value;

            return result;
        }
    }
}
