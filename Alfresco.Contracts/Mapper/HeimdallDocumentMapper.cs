using System;
using System.Collections.Generic;
using System.Linq;

namespace Alfresco.Contracts.Mapper
{
    public static class HeimdallDocumentMapper
    {
        /// <summary>
        /// Lista mapiranja sa CSV strukurom:
        /// (Naziv, SifraDoc, NazivDoc, TipDosiea, SifraDocMigracija, NazivDocMigracija)
        /// </summary>
        public static readonly List<(string Naziv, string SifraDoc, string NazivDoc, string TipDosiea, string SifraDocMigracija, string NazivDocMigracija)> DocumentMappings = new()
        {
            ("Personal Notice", "00253", "GDPR saglasnost", "Dosije klijenta FL / PL", "00849", "GDPR saglasnost - migracija"),
            ("Admission Card", "00135", "Potvrda o prijemu kartice", "Dosije paket racuna", "00135", "Potvrda o prijemu kartice"),
            ("KYC Questionnaire", "00130", "KYC upitnik", "Dosije klijenta FL / PL", "00841", "KYC upitnik - migracija"),
            ("Communication Consent", "00141", "Izjava o kanalima komunikacije", "Dosije klijenta FL / PL", "00842", "Izjava o kanalima komunikacije - migracija"),
            ("Application For Issuing Debit Card PI", "00139", "Zahtev za izdavanje kartice", "Dosije paket racuna", "00139", "Zahtev za izdavanje kartice"),
            ("Specimen card", "00099", "KDP za fizička lica", "Dosije paket racuna", "00824", "KDP za fizicka lica - migracija"),
            ("Specimen card for LE", "00100", "KDP za pravna lica_iz aplikacije", "Dosije paket racuna", "00827", "KDP za pravna lica - migracija"),
            ("Specimen Card for Authorized Person", "00101", "KDP za ovlašćena lica (za fizička lica)", "Dosije paket racuna", "00825", "KDP za ovlašćena lica (za fizička lica) – migracija"),
            ("Account Package", "00102", "Zahtev za otvaranje/izmenu paket računa", "Dosije paket racuna", "00834", "Zahtev za otvaranje/izmenu paket računa - migracija"),
            ("Pre-Contract Info", "00109", "Obaveštenje o predugovornoj fazi", "Dosije paket racuna", "00838", "Obaveštenje o predugovornoj fazi - migracija"),
            ("Current Accounts Contract", "00110", "Ugovor o tekućem računu", "Dosije paket racuna", "00110", "Ugovor o tekućem računu"),
            ("RSD Instruction for Resident", "00117", "Instrukcija za uplatu na RSD račun", "Dosije paket racuna", "00117", "Instrukcija za uplatu na RSD račun"),
            ("Travel Insurance", "00122", "Predugovorno obaveštenje o putnom osiguranju", "Dosije paket racuna", "00122", "Predugovorno obaveštenje o putnom osiguranju"),
            ("Request for Accounts Closure", "00124", "Zahtev za pojedinacno gašenje računa", "Dosije paket racuna", "00124", "Zahtev za pojedinacno gašenje računa"),
            ("Request for Package Accounts Closure", "00125", "Zahtev za gašenje računa i usluga-kompletno gašenje", "Dosije paket racuna", "00125", "Zahtev za gašenje računa i usluga-kompletno gašenje"),
            ("Offer With Saving Accounts", "00233", "Ponuda", "Dosije paket racuna", "00233", "Ponuda"),
            ("Saving Accounts Contract", "00113", "Ugovor o štednom racunu", "Dosije paket racuna", "00113", "Ugovor o štednom racunu"),
            ("Mandatory Elements with Saving Accounts", "00241", "Obavezni elementi", "Dosije paket racuna", "00241", "Obavezni elementi"),
            ("Card Return Request", "00138", "Zahtev za vraćanje kartice", "Dosije paket racuna", "00138", "Zahtev za vraćanje kartice"),
            ("GL Transaction", "00143", "GL transakcije", "Dosije paket racuna", "00844", "GL transakcije - migracija"),
            ("SMS info modify request", "00103", "Zahtev za izmenu SMS info servisa", "Dosije paket racuna", "00835", "Zahtev za izmenu SMS info servisa - migracija"),
            ("SMS ca edit client phone change", "00178", "Sms CA Phone Number Change", "Dosije paket racuna", "00178", "Sms CA Phone Number Change"),
            ("Card Accounts Change", "00133", "Zahtev za promenu racuna po kartici", "Dosije paket racuna", "00133", "Zahtev za promenu racuna po kartici"),
            ("Card Reissuing", "00134", "Zahtev za reizdavanje kartice", "Dosije paket racuna", "00134", "Zahtev za reizdavanje kartice"),
            ("GDPR Revoke", "00121", "GDPR povlačenje saglasnosti", "Dosije klijenta FL / PL", "00840", "GDPR povlačenje saglasnosti - migracija"),
            ("Card Blocking Request", "00136", "Zahtev za blokadu kartice", "Dosije paket racuna", "00136", "Zahtev za blokadu kartice"),
            ("SMS card alarm change", "00104", "Zahtev za izmenu SMS CA servisa", "Dosije paket racuna", "00836", "Zahtev za izmenu SMS CA servisa - migracija"),
            ("Card Limit Change", "00132", "Zahtev za promenu limita po kartici", "Dosije paket racuna", "00132", "Zahtev za promenu limita po kartici"),
            ("Request For Cancellation of Authorization", "00127", "Otkaz ovlašćenja", "Dosije paket racuna", "00127", "Otkaz ovlašćenja"),
            ("FX Transaction", "00142", "FX transakcije", "Dosije paket racuna", "00843", "FX transakcije - migracija"),
            ("Deblocking Card Request", "00137", "Zahtev za deblokadu kartice", "Dosije paket racuna", "00137", "Zahtev za deblokadu kartice"),
            ("Contact Data Change Email", "00156", "Zahtev za promenu email adrese putem mBankinga", "Dosije klijenta FL / PL", "00847", "Zahtev za promenu email adrese putem mBankinga - migracija"),
            ("Credit Bureau Reports Consent", "00237", "Saglasnost za povlačenje izveštaja KB-a", "Dosije klijenta FL / PL", "00237", "Saglasnost za povlačenje izveštaja KB-a"),
            ("Family insurance", "00123", "Predugovorno obaveštenje o porodičnom putnom osiguranju", "Dosije paket racuna", "00123", "Predugovorno obaveštenje o porodičnom putnom osiguranju"),
            ("Contact Data Change Phone", "00155", "Zahtev za promenu broja telefona putem mBankinga", "Dosije klijenta FL / PL", "00846", "Zahtev za promenu broja telefona putem mBankinga - migracija"),
            ("Travel Insurance Generali", "00766", "Putno osiguranje Generali", "Dosije paket racuna", "00766", "Putno osiguranje Generali"),
            ("Request For Opening Private Account", "00105", "Zahtev za otvaranje računa-pojedinačno otvaranje", "Dosije paket racuna", "00105", "Zahtev za otvaranje računa-pojedinačno otvaranje"),
            ("Contract Foreign Exchange Account For Receive Of Funds From The Sale Financial Instruments RSD", "00129", "Ugovor o otvaranju namenskog računa za prodaju finansijskih instrumenata", "Dosije paket racuna", "00129", "Ugovor o otvaranju namenskog računa za prodaju finansijskih instrumenata"),
            ("KYC Questionnaire MDOC", "00130", "KYC upitnik", "Dosije klijenta FL / PL", "00841", "KYC upitnik - migracija"),
            ("Contract Dedicated Account For Purchase Of Financial Instruments RSD", "00128", "Ugovor o otvaranju namenskog računa za kupovinu finansijskih instrumenata", "Dosije paket racuna", "00128", "Ugovor o otvaranju namenskog računa za kupovinu finansijskih instrumenata"),
            ("Prestige Package Tariff for LE","00160","Tarifnik za Prestige paket SB","Dosije paket racuna","00160","Tarifnik za Prestige paket SB"),
            ("SmePonuda","00765", "Ponuda", "Dosije depozita", "00765", "Ponuda"),
            ("SmeUgovorORacunu", "00764", "Ugovor o računu", "Dosije depozita", "00764", "Ugovor o računu"),
            ("SmeObavezniElementiUgovoraORacunu", "00763", "Obavezni elementi ugovora o računu", "Dosije depozita", "00763", "Obavezni elementi ugovora o računu"),
            // Dosije Depozita examples
            ("PiAnuitetniPlan", "00163", "Plan isplate depozita", "Dosije depozita", "00163", "Plan isplate depozita"),
            ("PiObavezniElementiUgovora", "00757", "Obavezni elementi ugovora", "Dosije depozita", "00757", "Obavezni elementi ugovora"),
            ("SmeUgovorOroceniDepozitPreduzetnici", "00166", "Ugovor o orocenom depozitu", "Dosije depozita", "00166", "Ugovor o orocenom depozitu"),
            ("KYC Questionnaire for LE","00130","KYC upitnik","Dosije klijenta PL","00841","KYC upitnik - migracija")
        };

        /// <summary>
        /// Pronalazi mapping po originalnom imenu dokumenta
        /// </summary>
        public static (string Naziv, string SifraDoc, string NazivDoc, string TipDosiea, string SifraDocMigracija, string NazivDocMigracija)? FindByOriginalName(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
                return null;

            var mapping = DocumentMappings.FirstOrDefault(m =>
                m.Naziv.Equals(originalName.Trim(), StringComparison.OrdinalIgnoreCase));

            return mapping.Naziv != null ? mapping : null;
        }

        /// <summary>
        /// Pronalazi mapping po originalnoj šifri dokumenta
        /// </summary>
        public static (string Naziv, string SifraDoc, string NazivDoc, string TipDosiea, string SifraDocMigracija, string NazivDocMigracija)? FindByOriginalCode(string originalCode)
        {
            if (string.IsNullOrWhiteSpace(originalCode))
                return null;

            var mapping = DocumentMappings.FirstOrDefault(m =>
                m.SifraDoc.Equals(originalCode.Trim(), StringComparison.OrdinalIgnoreCase));

            return mapping.Naziv != null ? mapping : null;
        }

        /// <summary>
        /// Da li će dokument dobiti sufiks migracija
        /// </summary>
        public static bool WillReceiveMigrationSuffix(string originalName)
        {
            var mapping = FindByOriginalName(originalName);
            if (mapping == null)
                return false;

            return mapping.Value.NazivDocMigracija.EndsWith("- migracija", StringComparison.OrdinalIgnoreCase) ||
                   mapping.Value.NazivDocMigracija.EndsWith("– migracija", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Da li će se šifra dokumenta promeniti
        /// </summary>
        public static bool CodeWillChange(string originalCode)
        {
            var mapping = FindByOriginalCode(originalCode);
            if (mapping == null)
                return false;

            return !mapping.Value.SifraDoc.Equals(mapping.Value.SifraDocMigracija, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Vraća migriranu šifru dokumenta
        /// </summary>
        public static string GetMigratedCode(string originalCode)
        {
            var mapping = FindByOriginalCode(originalCode);
            return mapping?.SifraDocMigracija ?? originalCode;
        }

        /// <summary>
        /// Vraća migrirani naziv dokumenta
        /// </summary>
        public static string GetMigratedName(string originalName)
        {
            var mapping = FindByOriginalName(originalName);
            return mapping?.NazivDocMigracija ?? originalName;
        }

        /// <summary>
        /// Vraća tip dosijea za dokument
        /// </summary>
        public static string GetDossierType(string originalName)
        {
            var mapping = FindByOriginalName(originalName);
            return mapping?.TipDosiea ?? string.Empty;
        }

        /// <summary>
        /// Vraća srpski naziv dokumenta
        /// </summary>
        public static string GetSerbianName(string originalName)
        {
            var mapping = FindByOriginalName(originalName);
            return mapping?.NazivDoc ?? originalName;
        }
    }
}
