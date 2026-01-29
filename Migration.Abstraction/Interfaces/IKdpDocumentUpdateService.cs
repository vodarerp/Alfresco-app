using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    /// <summary>
    /// Servis za ažuriranje KDP dokumenata u Alfrescu
    /// Procesira dokumente iz KdpExportResult tabele i ažurira njihove property-je u Alfrescu
    /// </summary>
    public interface IKdpDocumentUpdateService
    {
        /// <summary>
        /// Pokreće proces ažuriranja dokumenata u batch-evima
        /// </summary>
        /// <param name="batchSize">Veličina batch-a (preporučeno 500-1000)</param>
        /// <param name="maxDegreeOfParallelism">Maksimalan broj paralelnih API poziva (preporučeno 5)</param>
        /// <param name="progressCallback">Callback za prijavu progresa</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Rezultat ažuriranja</returns>
        Task<KdpUpdateResult> UpdateDocumentsAsync(
            int batchSize = 500,
            int maxDegreeOfParallelism = 5,
            Action<KdpUpdateProgress>? progressCallback = null,
            CancellationToken ct = default);

        /// <summary>
        /// Vraća trenutni status ažuriranja
        /// </summary>
        Task<KdpUpdateProgress> GetProgressAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Rezultat ažuriranja KDP dokumenata
    /// </summary>
    public class KdpUpdateResult
    {
        /// <summary>
        /// Ukupan broj uspešno ažuriranih dokumenata
        /// </summary>
        public int TotalSuccessful { get; set; }

        /// <summary>
        /// Ukupan broj neuspešnih ažuriranja
        /// </summary>
        public int TotalFailed { get; set; }

        /// <summary>
        /// Ukupan broj obrađenih dokumenata
        /// </summary>
        public int TotalProcessed => TotalSuccessful + TotalFailed;

        /// <summary>
        /// Vreme početka
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Vreme završetka
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Trajanje obrade
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Da li je proces otkazan
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>
        /// Poruka o grešci (ako je bilo kritične greške)
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Progress ažuriranja KDP dokumenata
    /// </summary>
    public class KdpUpdateProgress
    {
        /// <summary>
        /// Ukupan broj dokumenata za ažuriranje
        /// </summary>
        public long TotalDocuments { get; set; }

        /// <summary>
        /// Broj obrađenih dokumenata
        /// </summary>
        public long ProcessedDocuments { get; set; }

        /// <summary>
        /// Broj uspešno ažuriranih dokumenata
        /// </summary>
        public long SuccessfulUpdates { get; set; }

        /// <summary>
        /// Broj neuspelih ažuriranja
        /// </summary>
        public long FailedUpdates { get; set; }

        /// <summary>
        /// Procenat završenosti (0-100)
        /// </summary>
        public double PercentComplete => TotalDocuments > 0
            ? Math.Round((double)ProcessedDocuments / TotalDocuments * 100, 2)
            : 0;

        /// <summary>
        /// Trenutni batch broj
        /// </summary>
        public int CurrentBatch { get; set; }

        /// <summary>
        /// Procenjeno preostalo vreme
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Proteklo vreme
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// Prosečna brzina (dokumenata po sekundi)
        /// </summary>
        public double DocumentsPerSecond { get; set; }

        /// <summary>
        /// Poslednja poruka o statusu
        /// </summary>
        public string? StatusMessage { get; set; }
    }
}
