using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Servis za obradu KDP dokumenata (tipovi 00824 i 00099)
    /// Procesira KDP dokumente, pronalazi foldere sa samo neaktivnim dokumentima,
    /// i priprema ih za eksport u Excel
    /// </summary>
    public interface IKdpDocumentProcessingService
    {
        /// <summary>
        /// Učitava sve KDP dokumente (00824 i 00099) iz Alfresca i puni staging tabelu
        /// </summary>
        /// <returns>Broj učitanih dokumenata</returns>
        Task<int> LoadKdpDocumentsToStagingAsync(CancellationToken ct = default);

        /// <summary>
        /// Procesuira staging podatke pozivom sp_ProcessKdpDocuments
        /// Pronalazi foldere sa samo neaktivnim KDP dokumentima i kreira export rezultate
        /// </summary>
        /// <returns>Tuple sa (totalCandidates, totalDocuments)</returns>
        Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default);

        /// <summary>
        /// Eksportuje rezultate u Excel fajl (za buduću implementaciju)
        /// </summary>
        Task ExportToExcelAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// Briše staging tabelu (za novo pokretanje)
        /// </summary>
        Task ClearStagingAsync(CancellationToken ct = default);

        /// <summary>
        /// Vraća statistiku obrade
        /// </summary>
        Task<KdpProcessingStatistics> GetStatisticsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Statistika obrade KDP dokumenata
    /// </summary>
    public class KdpProcessingStatistics
    {
        /// <summary>
        /// Ukupan broj dokumenata u staging tabeli
        /// </summary>
        public long TotalDocumentsInStaging { get; set; }

        /// <summary>
        /// Broj kandidat foldera (folderi sa samo neaktivnim KDP dokumentima)
        /// </summary>
        public long TotalCandidateFolders { get; set; }

        /// <summary>
        /// Ukupan broj KDP dokumenata u kandidat folderima
        /// </summary>
        public long TotalDocumentsInCandidateFolders { get; set; }

        /// <summary>
        /// Datum najstarijeg dokumenta
        /// </summary>
        public DateTime? OldestDocumentDate { get; set; }

        /// <summary>
        /// Datum najnovijeg dokumenta
        /// </summary>
        public DateTime? NewestDocumentDate { get; set; }

        /// <summary>
        /// Broj neaktivnih dokumenata (status = '2')
        /// </summary>
        public long InactiveDocumentsCount { get; set; }

        /// <summary>
        /// Broj aktivnih dokumenata (status = '1')
        /// </summary>
        public long ActiveDocumentsCount { get; set; }

        /// <summary>
        /// Broj dokumenata tipa 00824
        /// </summary>
        public long Type00824Count { get; set; }

        /// <summary>
        /// Broj dokumenata tipa 00099
        /// </summary>
        public long Type00099Count { get; set; }
    }
}
