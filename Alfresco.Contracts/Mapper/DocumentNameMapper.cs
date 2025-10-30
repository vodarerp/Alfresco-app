using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    public static class DocumentNameMapper
    {
        /// <summary>
        /// Mapiranje: originalni naziv → naziv sa sufiksom "-migracija"
        /// Dokumenti u ovom dictionary-u će biti migrirani kao NEAKTIVNI (status "poništen")
        /// Dokumenti koji NISU u ovom dictionary-u ostaju aktivni (status "validiran")
        /// </summary>
        private static readonly Dictionary<string, string> NameMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Srpski nazivi
        { "GDPR saglasnost", "GDPR saglasnost - migracija" },
        { "KYC upitnik", "KYC upitnik - migracija" },
        { "Izjava o kanalima komunikacije", "Izjava o kanalima komunikacije - migracija" },
        { "Izjava o pristupu", "Izjava o pristupu - migracija" },
        { "Izjava o sprečavanju pranja novca", "Izjava o sprečavanju pranja novca - migracija" },
        { "Pristanak za obradu ličnih podataka", "Pristanak za obradu ličnih podataka - migracija" },
        { "KDP za fizička lica", "KDP za fizička lica - migracija" },
        { "KDP za pravna lica_iz aplikacije", "KDP za pravna lica - migracija" },
        { "KDP za ovlašćena lica (za fizička lica)", "KDP za ovlašćena lica (za fizička lica) - migracija" },
        { "Zahtev za otvaranje/izmenu paket računa", "Zahtev za otvaranje/izmenu paket računa - migracija" },
        { "Obaveštenje o predugovornoj fazi", "Obaveštenje o predugovornoj fazi - migracija" },
        { "GL transakcije", "GL transakcije - migracija" },
        { "Zahtev za izmenu SMS info servisa", "Zahtev za izmenu SMS info servisa - migracija" },
        { "Zahtev za izmenu SMS CA servisa", "Zahtev za izmenu SMS CA servisa - migracija" },
        { "FX transakcije", "FX transakcije - migracija" },
        { "GDPR povlačenje saglasnosti", "GDPR povlačenje saglasnosti - migracija" },
        { "Zahtev za promenu email adrese putem mBankinga", "Zahtev za promenu email adrese putem mBankinga - migracija" },
        { "Zahtev za promenu broja telefona putem mBankinga", "Zahtev za promenu broja telefona putem mBankinga - migracija" },

        // Engleski nazivi (iz starog Alfresco-a)
        { "Personal Notice", "GDPR saglasnost - migracija" },
        { "KYC Questionnaire", "KYC upitnik - migracija" },
        { "KYC Questionnaire MDOC", "KYC upitnik - migracija" },
        { "Communication Consent", "Izjava o kanalima komunikacije - migracija" },
        { "Specimen card", "KDP za fizička lica - migracija" },
        { "Specimen card for LE", "KDP za pravna lica - migracija" },
        { "Specimen Card for Authorized Person", "KDP za ovlašćena lica (za fizička lica) - migracija" },
        { "Account Package", "Zahtev za otvaranje/izmenu paket računa - migracija" },
        { "Pre-Contract Info", "Obaveštenje o predugovornoj fazi - migracija" },
        { "GL Transaction", "GL transakcije - migracija" },
        { "SMS info modify request", "Zahtev za izmenu SMS info servisa - migracija" },
        { "SMS card alarm change", "Zahtev za izmenu SMS CA servisa - migracija" },
        { "FX Transaction", "FX transakcije - migracija" },
        { "GDPR Revoke", "GDPR povlačenje saglasnosti - migracija" },
        { "Contact Data Change Email", "Zahtev za promenu email adrese putem mBankinga - migracija" },
        { "Contact Data Change Phone", "Zahtev za promenu broja telefona putem mBankinga - migracija" }
    };

        public static string GetMigratedName(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
                return originalName;

            return NameMappings.TryGetValue(originalName.Trim(), out var migratedName)
                ? migratedName
                : originalName;
        }

        public static bool WillReceiveMigrationSuffix(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
                return false;

            return NameMappings.ContainsKey(originalName.Trim());
        }
    }
}
