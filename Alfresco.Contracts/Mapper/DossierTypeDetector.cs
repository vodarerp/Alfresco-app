using Alfresco.Contracts.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    public static class DossierTypeDetector
    {
        /// <summary>
        /// Određuje tip dosijea na osnovu "Tip dosijea" property-ja iz Alfresco-a
        /// NE koristi folder path jer nije pouzdan indikator
        /// </summary>
        public static DossierType DetectFromTipDosijea(string? tipDosijea)
        {
            if (string.IsNullOrWhiteSpace(tipDosijea))
                return DossierType.Unknown;

            var normalized = tipDosijea.Trim().ToLowerInvariant();

            // TC 3: Dosije paket računa → 300
            if (normalized.Contains("dosije paket racuna") ||
                normalized.Contains("dosije paket računa"))
                return DossierType.AccountPackage;

            // TC 4: Dosije klijenta FL/PL → zavisi od segmenta
            if (normalized.Contains("dosije klijenta fl / pl") ||
                normalized.Contains("dosije klijenta fl/pl"))
            {
                return DossierType.ClientFLorPL; // Čeka ClientAPI segment
            }

            // TC 5: Dosije klijenta PL (samo PL, bez FL) → 400
            if (normalized.Contains("dosije klijenta pl") &&
                !normalized.Contains("fl"))
                return DossierType.ClientPL;

            // TC 17: Dosije depozita → 700
            if (normalized.Contains("dosije depozita"))
                return DossierType.Deposit;

            if (normalized.Contains("dosije ostalo"))
                return DossierType.Other;

            return DossierType.Unknown;
        }

        /// <summary>
        /// NOVA LOGIKA: Određuje tip dosijea na osnovu ecm:docDesc i mapiranja iz HeimdallDocumentMapper
        /// ecm:docDesc sadrži vrednost iz polja Naziv ili NazivDoc iz DocumentMappings liste
        /// Na osnovu tog mapiranja dobijamo TipDosiea i određujemo destination folder
        /// </summary>
        /// <param name="docDesc">ecm:docDesc - Opis dokumenta (Naziv ili NazivDoc iz liste)</param>
        /// <returns>DossierType baziran na TipDosiea iz mapiranja</returns>
        public static DossierType DetectFromDocDesc(string docDesc)
        {
            if (string.IsNullOrWhiteSpace(docDesc))
                return DossierType.Unknown;

            // Pronađi mapping na osnovu ecm:docDesc
            var mapping = HeimdallDocumentMapper.FindByOriginalName(docDesc);

            // Ako nije pronađen po engleskom nazivu, probaj po srpskom nazivu
            if (mapping == null)
            {
                mapping = HeimdallDocumentMapper.DocumentMappings
                    .FirstOrDefault(m => m.NazivDoc.Equals(docDesc?.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (mapping == null)
                return DossierType.Unknown;

            // Sada koristimo TipDosiea iz mapiranja
            return DetectFromTipDosijea(mapping.Value.TipDosiea);
        }

        /// <summary>
        /// Vraća destination folder name (DOSSIER-ACC, DOSSIER-LE, DOSSIER-PI, DOSSIER-D)
        /// na osnovu DossierType
        /// </summary>
        /// <param name="dossierType">Tip dosijea</param>
        /// <returns>Naziv destination foldera</returns>
        public static string GetDossierFolderName(DossierType dossierType)
        {
            return dossierType switch
            {
                DossierType.AccountPackage => "DOSSIER-ACC",
                DossierType.ClientPL => "DOSSIER-LE",
                DossierType.ClientFL => "DOSSIER-PI",
                DossierType.Deposit => "DOSSIER-D",
                DossierType.ClientFLorPL => throw new InvalidOperationException(
                    "Cannot get folder name for unresolved ClientFLorPL type. Must resolve to ClientFL or ClientPL first."),
                _ => "DOSSIER-UNKNOWN"
            };
        }

        /// <summary>
        /// Razrešava FL/PL na osnovu segment/tip klijenta iz ClientAPI-a
        /// </summary>
        public static DossierType ResolveFLorPL(string clientSegment)
        {
            var normalized = clientSegment?.Trim().ToUpperInvariant();

            // PI = Personal Individual = Fizičko lice
            if (normalized == "PI" || normalized == "RETAIL" || normalized == "FL")
                return DossierType.ClientFL; // 500

            // LE = Legal Entity = Pravno lice
            if (normalized == "LE" || normalized == "SME" || normalized == "CORPORATE" || normalized == "PL")
                return DossierType.ClientPL; // 400

            return DossierType.Unknown;
        }

        /// <summary>
        /// Vraća naziv destination foldera za dati DossierType
        /// Koristi se za kreiranje parent foldera u novom Alfresco-u
        /// </summary>
        public static string GetDestinationFolderName(DossierType dossierType)
        {
            return dossierType switch
            {
                DossierType.AccountPackage => "300 Dosije paket računa",
                DossierType.ClientPL => "400 Dosije pravnog lica",
                DossierType.ClientFL => "500 Dosije fizičkog lica",
                DossierType.Deposit => "700 Dosije depozita",
                DossierType.Unknown => "999 Dosije - Unknown",
                DossierType.Other => "999 Dosije - Unknown", // Fallback
                DossierType.ClientFLorPL => throw new InvalidOperationException(
                    "Cannot get folder name for unresolved ClientFLorPL type. Must resolve to ClientFL or ClientPL first."),
                _ => "999 Dosije - Unknown"
            };
        }

        /// <summary>
        /// Mapira DOSSIER folder type (FL/PL/ACC/D) iz starog Alfresco-a -> DossierType enum
        /// Ovo se koristi za određivanje koje parent foldere treba kreirati
        /// </summary>
        public static DossierType MapDossierFolderTypeToDossierType(string folderType)
        {
            return folderType?.ToUpperInvariant() switch
            {
                "FL" => DossierType.ClientFLorPL,  // Treba razrešiti kasnije pomoću ClientSegment
                "PL" => DossierType.ClientPL,
                "ACC" => DossierType.AccountPackage,
                "D" => DossierType.Deposit,
                _ => DossierType.Unknown
            };
        }

        /// <summary>
        /// Vraća sve moguće DossierType vrednosti koje mogu nastati iz datog folder type-a
        /// Za FL vraća i ClientFL i ClientPL jer mogu biti oba
        /// </summary>
        public static IEnumerable<DossierType> GetPossibleDossierTypes(string folderType)
        {
            var baseType = MapDossierFolderTypeToDossierType(folderType);

            if (baseType == DossierType.ClientFLorPL)
            {
                // FL folder može sadržati i fizička i pravna lica
                yield return DossierType.ClientFL;
                yield return DossierType.ClientPL;
            }
            else if (baseType != DossierType.Other && baseType != DossierType.ClientFLorPL)
            {
                yield return baseType;
            }
        }
    }
}