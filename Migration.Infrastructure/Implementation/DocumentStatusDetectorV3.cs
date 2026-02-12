using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Models;

using System;

namespace Migration.Infrastructure.Implementation
{
    
    public static class DocumentStatusDetectorV3
    {
        
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
                    Status = "2",
                    DeterminationReason = "Nema mapiranja - default aktivan",
                    Priority = 0
                };
            }

           
            if (!string.IsNullOrWhiteSpace(mapping.PolitikaCuvanja)  & false)
            {
                var politikaTrimmed = mapping.PolitikaCuvanja.Trim();

                if (politikaTrimmed.Equals("Nova verzija", StringComparison.OrdinalIgnoreCase) ||
                    politikaTrimmed.Equals("Novi dokument", StringComparison.OrdinalIgnoreCase))
                {
                    return new DocumentStatusInfo
                    {
                        IsActive = false,
                        Status = "2",
                        DeterminationReason = $"Prioritet 2: PolitikaCuvanja = '{politikaTrimmed}'",
                        Priority = 2,
                        MappingCode = mapping.SifraDokumentaMigracija,
                        MappingName = mapping.NazivDokumentaMigracija,
                        PolitikaCuvanja = politikaTrimmed,
                        OriginalCode = mapping.SifraDokumenta
                    };
                }
            }

            
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
                        Status = "2",
                        DeterminationReason = "Prioritet 3: NazivDokumentaMigracija ima sufiks '- migracija'",
                        Priority = 3,
                        MappingCode = mapping.SifraDokumentaMigracija,
                        MappingName = mapping.NazivDokumentaMigracija,
                        HasMigrationSuffix = true,
                        OriginalCode = mapping.SifraDokumenta
                    };
                }
                else
                {
                    return new DocumentStatusInfo
                    {
                        IsActive = true,
                        Status = "1",
                        DeterminationReason = "Prioritet 3: NazivDokumentaMigracija NEMA sufiks '- migracija'",
                        Priority = 3,
                        MappingCode = mapping.SifraDokumentaMigracija,
                        MappingName = mapping.NazivDokumentaMigracija,
                        HasMigrationSuffix = false,
                        OriginalCode = mapping.SifraDokumenta
                    };
                }
            }

            // Default: Aktivan (ako nije pokriveno gornjim pravilima)
            return new DocumentStatusInfo
            {

                IsActive = true,
                Status = "2",
                DeterminationReason = "Default: Aktivan (ne postoji NazivDokumentaMigracija)",
                Priority = 4,
                MappingCode = mapping.SifraDokumentaMigracija,
                MappingName = mapping.NazivDokumentaMigracija,
                OriginalCode = mapping.SifraDokumenta
            };
        }

        
        [Obsolete("Koristiti DetermineStatus() metodu umesto ove - ona koristi novu logiku sa prioritetima")]
        public static DocumentStatusInfo GetStatusInfoByOpis(
            string? opisDokumenta,
            string? existingStatus = null)
        {
            // Stara logika - samo provera sufiks u opisu
            var isActive = !HasMigrationSuffixInOpis(opisDokumenta);
            var status = isActive ? "1" : "2";

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

        
        private static bool HasMigrationSuffixInOpis(string? opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            return opisDokumenta.Contains("- migracija", StringComparison.OrdinalIgnoreCase) ||
                   opisDokumenta.Contains("– migracija", StringComparison.OrdinalIgnoreCase);
        }
    }
}

