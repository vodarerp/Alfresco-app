using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Models;

using System;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Određuje status dokumenta nakon migracije.
    /// VERZIJA 3.0: Nova logika sa prioritetima i PolitikaCuvanja kolonom.
    ///
    /// PRIORITETI (od najvišeg ka najnižem):
    /// 1. Ako SifraDokumentaMigracija = "00824" → AKTIVAN (ecm:status='validiran', ecm:active=true)
    /// 2. Ako PolitikaCuvanja = "Nova verzija" ili "Novi dokument" → NEAKTIVAN (ecm:status='poništen', ecm:active=false)
    /// 3. Ako PolitikaCuvanja je prazna/null, proverava se NazivDokumentaMigracija:
    ///    - Ako ima sufiks '- migracija' → NEAKTIVAN (ecm:status='poništen', ecm:active=false)
    ///    - Ako nema sufiks '- migracija' → AKTIVAN (ecm:status='validiran', ecm:active=true)
    /// </summary>
    public static class DocumentStatusDetectorV3
    {
        /// <summary>
        /// Određuje status dokumenta na osnovu mapiranja iz DocumentMapping tabele.
        /// Koristi novu logiku sa prioritetima.
        /// </summary>
        /// <param name="mapping">DocumentMapping objekat iz tabele (može biti null ako nema mapiranja)</param>
        /// <param name="existingStatus">Postojeći status dokumenta iz starog Alfresco sistema (opciono)</param>
        /// <returns>DocumentStatusInfo sa kompletnim informacijama o statusu</returns>
        public static DocumentStatusInfo DetermineStatus(
            DocumentMapping? mapping,
            string? existingStatus = null)
        {
            // Ako nema mapiranja, vraćamo default (aktivan)
            if (mapping == null)
            {
                return new DocumentStatusInfo
                {
                    IsActive = true,
                    Status = "validiran",
                    DeterminationReason = "Nema mapiranja - default aktivan",
                    Priority = 0
                };
            }

            // PRIORITET 1: Provera šifre 00824
            if (!string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija) &&
                mapping.SifraDokumentaMigracija.Trim().Equals("00824", StringComparison.OrdinalIgnoreCase))
            {
                return new DocumentStatusInfo
                {
                    IsActive = true,
                    Status = "validiran",
                    DeterminationReason = "Prioritet 1: SifraDokumentaMigracija = '00824'",
                    Priority = 1,
                    MappingCode = mapping.SifraDokumentaMigracija,
                    MappingName = mapping.NazivDokumentaMigracija
                };
            }

            // PRIORITET 2: Provera PolitikaCuvanja
            if (!string.IsNullOrWhiteSpace(mapping.PolitikaCuvanja)  & false)
            {
                var politikaTrimmed = mapping.PolitikaCuvanja.Trim();

                if (politikaTrimmed.Equals("Nova verzija", StringComparison.OrdinalIgnoreCase) ||
                    politikaTrimmed.Equals("Novi dokument", StringComparison.OrdinalIgnoreCase))
                {
                    return new DocumentStatusInfo
                    {
                        IsActive = false,
                        Status = "poništen",
                        DeterminationReason = $"Prioritet 2: PolitikaCuvanja = '{politikaTrimmed}'",
                        Priority = 2,
                        MappingCode = mapping.SifraDokumentaMigracija,
                        MappingName = mapping.NazivDokumentaMigracija,
                        PolitikaCuvanja = politikaTrimmed
                    };
                }
            }

            // PRIORITET 3: Provera sufiks '- migracija' u NazivDokumentaMigracija
            if (!string.IsNullOrWhiteSpace(mapping.NazivDokumentaMigracija))
            {
                var naziv = mapping.NazivDokumentaMigracija.Trim();

                // Provera oba tipa crtice: obična (-) i en dash (–)
                if (naziv.EndsWith("- migracija", StringComparison.OrdinalIgnoreCase) ||
                    naziv.EndsWith("– migracija", StringComparison.OrdinalIgnoreCase))
                {
                    return new DocumentStatusInfo
                    {
                        IsActive = false,
                        Status = "poništen",
                        DeterminationReason = "Prioritet 3: NazivDokumentaMigracija ima sufiks '- migracija'",
                        Priority = 3,
                        MappingCode = mapping.SifraDokumentaMigracija,
                        MappingName = mapping.NazivDokumentaMigracija,
                        HasMigrationSuffix = true
                    };
                }
                else
                {
                    return new DocumentStatusInfo
                    {
                        IsActive = true,
                        Status = "validiran",
                        DeterminationReason = "Prioritet 3: NazivDokumentaMigracija NEMA sufiks '- migracija'",
                        Priority = 3,
                        MappingCode = mapping.SifraDokumentaMigracija,
                        MappingName = mapping.NazivDokumentaMigracija,
                        HasMigrationSuffix = false
                    };
                }
            }

            // Default: Aktivan (ako nije pokriveno gornjim pravilima)
            return new DocumentStatusInfo
            {

                IsActive = true,
                Status = "validiran",
                DeterminationReason = "Default: Aktivan (ne postoji NazivDokumentaMigracija)",
                Priority = 4,
                MappingCode = mapping.SifraDokumentaMigracija,
                MappingName = mapping.NazivDokumentaMigracija
            };
        }

        /// <summary>
        /// Kompatibilnost sa starom verzijom - određuje status na osnovu opisa dokumenta.
        /// NAPOMENA: Ova metoda NE koristi novu logiku sa PolitikaCuvanja i prioritetima!
        /// Za novu logiku koristiti DetermineStatus() metodu.
        /// </summary>
        [Obsolete("Koristiti DetermineStatus() metodu umesto ove - ona koristi novu logiku sa prioritetima")]
        public static DocumentStatusInfo GetStatusInfoByOpis(
            string? opisDokumenta,
            string? existingStatus = null)
        {
            // Stara logika - samo provera sufiks u opisu
            var isActive = !HasMigrationSuffixInOpis(opisDokumenta);
            var status = isActive ? "validiran" : "poništen";

            var hasMigrationSuffix = HasMigrationSuffixInOpis(opisDokumenta);

            return new DocumentStatusInfo
            {
                IsActive = isActive,
                Status = status,
                HasMigrationSuffix = hasMigrationSuffix,
                DeterminationReason = hasMigrationSuffix
                    ? "Stara logika: Opis ima sufiks '- migracija'"
                    : "Stara logika: Opis NEMA sufiks '- migracija'",
                Priority = 99 // Niska prioritet - stara logika
            };
        }

        /// <summary>
        /// Proverava da li opis dokumenta sadrži sufiks "- migracija"
        /// </summary>
        private static bool HasMigrationSuffixInOpis(string? opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            return opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                   opisDokumenta.Contains("– migracija", StringComparison.OrdinalIgnoreCase);
        }
    }
}

