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
    }
}