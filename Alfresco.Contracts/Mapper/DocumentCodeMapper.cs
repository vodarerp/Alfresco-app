using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    public static class DocumentCodeMapper
    {
        /// <summary>
        /// Mapiranje: originalna šifra → nova šifra
        /// Ako se šifra NE MENJA, mapiranje pokazuje na istu vrednost
        /// </summary>
        private static readonly Dictionary<string, string> CodeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Šifre koje SE MENJAJU
        { "00253", "00849" },  // GDPR saglasnost
        { "00130", "00841" },  // KYC upitnik
        { "00141", "00842" },  // Izjava o kanalima komunikacije
        { "00099", "00824" },  // KDP za fizička lica
        { "00100", "00827" },  // KDP za pravna lica
        { "00101", "00825" },  // KDP za ovlašćena lica
        { "00102", "00834" },  // Zahtev za otvaranje/izmenu paket računa
        { "00109", "00838" },  // Obaveštenje o predugovornoj fazi
        { "00143", "00844" },  // GL transakcije
        { "00103", "00835" },  // Zahtev za izmenu SMS info servisa
        { "00104", "00836" },  // Zahtev za izmenu SMS CA servisa
        { "00142", "00843" },  // FX transakcije
        { "00121", "00840" },  // GDPR povlačenje saglasnosti
        { "00156", "00847" },  // Zahtev za promenu email adrese
        { "00155", "00846" },  // Zahtev za promenu broja telefona

        // Šifre koje se NE MENJAJU
        { "00135", "00135" },
        { "00139", "00845" },
        { "00889", "00889" },
        { "00879", "00879" },
        { "00882", "00882" },
        { "00890", "00890" },
        { "00891", "00891" },
        { "00892", "00892" },
        { "00886", "00886" },
        { "00887", "00887" },
        { "00581", "00581" },
        { "00584", "00584" },
        { "00439", "00439" },
        { "00438", "00438" },
        { "00473", "00473" },
        { "00472", "00472" },
        { "00136", "00136" },
        { "00493", "00493" },
        { "00494", "00494" },
        { "00582", "00582" },
        { "00583", "00583" },
        { "00660", "00660" },
        { "00661", "00661" },
        { "00662", "00662" },
        { "00663", "00663" },
        { "00664", "00664" },
        { "00665", "00665" },
        { "00666", "00666" },
        { "00667", "00667" },
        { "00668", "00668" },
        { "00669", "00669" },
        { "02756", "02756" },
        { "02757", "02757" },
        { "02758", "02758" },
        { "00110", "00110" },
        { "00117", "00117" },
        { "00122", "00122" },
        { "00124", "00124" },
        { "00125", "00125" },
        { "00233", "00233" },
        { "00113", "00113" },
        { "00241", "00241" },
        { "00138", "00138" },
        { "00178", "00178" },
        { "00133", "00133" },
        { "00134", "00134" },
        { "00237", "00237" },
        { "00123", "00123" },
        { "00766", "00766" },
        { "00105", "00105" },
        { "00129", "00129" },
        { "00128", "00128" },
        { "00127", "00127" },
        { "00137", "00137" },
        { "00132", "00132" }
    };

        public static string GetMigratedCode(string originalCode)
        {
            if (string.IsNullOrWhiteSpace(originalCode))
                return originalCode;

            return CodeMappings.TryGetValue(originalCode.Trim(), out var migratedCode)
                ? migratedCode
                : originalCode;
        }

        public static bool CodeWillChange(string originalCode)
        {
            if (string.IsNullOrWhiteSpace(originalCode))
                return false;

            var trimmed = originalCode.Trim();
            return CodeMappings.TryGetValue(trimmed, out var newCode) && newCode != trimmed;
        }
    }
}
