using System;
using System.Collections.Generic;

namespace Migration.Infrastructure.Implementation.Helpers
{
    /// <summary>
    /// Maps ecm:opisDokumenta (document description) to ecm:tipDokumenta (document type code)
    /// Supports both Serbian and English descriptions from old Alfresco system
    /// </summary>
    public static class OpisToTipMapper
    {
        /// <summary>
        /// Mapping dictionary: ecm:opisDokumenta → ecm:tipDokumenta
        /// Case-insensitive matching
        /// </summary>
        private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
        {
            // ========================================
            // SERBIAN DESCRIPTIONS
            // ========================================
            { "GDPR saglasnost", "00849" },
            { "KYC upitnik", "00841" },
            { "Izjava o kanalima komunikacije", "00842" },
            { "KDP za fizička lica", "00824" },
            { "KDP za pravna lica", "00827" },
            { "KDP za ovlašćena lica (za fizička lica)", "00825" },
            { "Zahtev za otvaranje/izmenu paket računa", "00834" },
            { "Obaveštenje o predugovornoj fazi", "00838" },
            { "GL transakcije", "00844" },
            { "Zahtev za izmenu SMS info servisa", "00835" },
            { "Zahtev za izmenu SMS CA servisa", "00836" },
            { "FX transakcije", "00843" },
            { "GDPR povlačenje saglasnosti", "00840" },
            { "Zahtev za promenu email adrese putem mBankinga", "00847" },
            { "Zahtev za promenu broja telefona putem mBankinga", "00846" },
            { "Ugovor o tekućem računu", "00110" },
            { "Ugovor o tekućem deviznom računu", "00117" },
            { "Izjava o pristupu", "00845" },
            { "Pristanak za obradu ličnih podataka", "00849" },

            // ========================================
            // ENGLISH DESCRIPTIONS (from old Alfresco)
            // ========================================
            { "Personal Notice", "00849" },
            { "KYC Questionnaire", "00841" },
            { "KYC Questionnaire MDOC", "00841" },
            { "KYC Questionnaire for LE", "00841" },
            { "Communication Consent", "00842" },
            { "Specimen card", "00824" },
            { "Specimen card for LE", "00827" },
            { "Specimen Card for Authorized Person", "00825" },
            { "Account Package", "00834" },
            { "Account Package RSD Instruction for Resident", "00834" },
            { "Pre-Contract Info", "00838" },
            { "GL Transaction", "00844" },
            { "SMS info modify request", "00835" },
            { "SMS card alarm change", "00836" },
            { "FX Transaction", "00843" },
            { "GDPR Revoke", "00840" },
            { "Contact Data Change Email", "00847" },
            { "Contact Data Change Phone", "00846" },
            { "Current Accounts Contract", "00110" },
            { "Current Account Contract for LE", "00110" },

            // ========================================
            // DEPOSIT DOCUMENTS
            // ========================================
            { "Ugovor o oročenom depozitu", "00008" },
            { "Ponuda", "00889" },
            { "Plan isplate depozita", "00879" },
            { "Obavezni elementi Ugovora", "00882" },
            { "PiVazeciUgovorOroceniDepozitDvojezicniRSD", "00008" },
            { "PiVazeciUgovorOroceniDepozitOstaleValute", "00008" },
            { "PiVazeciUgovorOroceniDepozitDinarskiTekuci", "00008" },
            { "PiVazeciUgovorOroceniDepozitNa36Meseci", "00008" },
            { "PiVazeciUgovorOroceniDepozitNa24MesecaRSD", "00008" },
            { "PiVazeciUgovorOroceniDepozitNa25Meseci", "00008" },
            { "PiPonuda", "00889" },
            { "PiAnuitetniPlan", "00879" },
            { "PiObavezniElementiUgovora", "00882" },
            { "ZahtevZaOtvaranjeRacunaOrocenogDepozita", "00890" }
        };

        /// <summary>
        /// Gets the document type code (ecm:tipDokumenta) from document description (ecm:opisDokumenta)
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <returns>Document type code or "UNKNOWN" if not found</returns>
        public static string GetTipDokumenta(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return "UNKNOWN";

            return Mappings.TryGetValue(opisDokumenta.Trim(), out var tipDokumenta)
                ? tipDokumenta
                : "UNKNOWN";
        }

        /// <summary>
        /// Checks if the given document description has a known mapping
        /// </summary>
        /// <param name="opisDokumenta">Document description from Alfresco</param>
        /// <returns>True if mapping exists, false otherwise</returns>
        public static bool IsKnownOpis(string opisDokumenta)
        {
            if (string.IsNullOrWhiteSpace(opisDokumenta))
                return false;

            return Mappings.ContainsKey(opisDokumenta.Trim());
        }

        /// <summary>
        /// Gets all registered mappings (for debugging/testing purposes)
        /// </summary>
        /// <returns>Read-only dictionary of all mappings</returns>
        public static IReadOnlyDictionary<string, string> GetAllMappings()
        {
            return Mappings;
        }
    }
}
