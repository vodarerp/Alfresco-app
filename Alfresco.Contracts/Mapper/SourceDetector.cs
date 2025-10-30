using Alfresco.Contracts.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Mapper
{
    public static class SourceDetector
    {
        /// <summary>
        /// Određuje source atribut na osnovu tipa dosijea
        /// TC 6: Heimdall za dosijee 300, 400, 500
        /// TC 7: DUT za dosije 700
        /// </summary>
        public static string GetSource(DossierType dossierType)
        {
            if (dossierType == DossierType.Deposit)
                return "DUT";

            return "Heimdall";
        }
    }
}
