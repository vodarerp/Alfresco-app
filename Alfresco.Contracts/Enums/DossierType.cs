using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Enums
{
    public enum DossierType
    {
        ClientFL = 500,           // Dosije fizičkog lica
        ClientPL = 400,           // Dosije pravnog lica
        AccountPackage = 300,     // Dosije paket računa
        Deposit = 700,            // Dosije depozita
        ClientFLorPL = -1,        // Privremeno - čeka segment iz ClientAPI
        Other = -2,               // Dosije ostalo - čeka dodatne informacije
        Unknown = 999             // Nepoznato
    }
}
