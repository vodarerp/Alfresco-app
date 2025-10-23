using log4net;
using log4net.Config;
using System;
using System.IO;
using System.Reflection;

namespace SqlServer.Infrastructure.Examples
{
    /// <summary>
    /// Primer korišćenja log4net sa SQL Server backend-om
    /// </summary>
    public class LoggerExample
    {
        // Static logger instance za ovu klasu
        private static readonly ILog log = LogManager.GetLogger(typeof(LoggerExample));

        // Named loggers za specifične potrebe
        private static readonly ILog dbLog = LogManager.GetLogger("DbLogger");
        private static readonly ILog fileLog = LogManager.GetLogger("FileLogger");
        private static readonly ILog hybridLog = LogManager.GetLogger("HybridLogger");

        /// <summary>
        /// Inicijalizacija log4net konfiguracije
        /// Poziva se jednom na startu aplikacije (Program.cs ili Startup.cs)
        /// </summary>
        public static void InitializeLogging(string appInstanceName = "MigrationService-1")
        {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            var configFile = new FileInfo("log4net.sqlserver.config");

            if (!configFile.Exists)
            {
                throw new FileNotFoundException($"Log4net config file not found: {configFile.FullName}");
            }

            XmlConfigurator.Configure(logRepository, configFile);

            // Setuj globalni AppInstance (vidljivo u svim log entry-jima)
            log4net.GlobalContext.Properties["AppInstance"] = appInstanceName;

            log.Info("=== Logging system initialized ===");
        }

        /// <summary>
        /// Primer 1: Jednostavno logovanje
        /// </summary>
        public void SimpleLoggingExample()
        {
            log.Info("Application started");
            log.Debug("Debug information");
            log.Warn("Warning message");
            log.Error("Error occurred");
        }

        /// <summary>
        /// Primer 2: Logovanje sa exception-om
        /// </summary>
        public void ExceptionLoggingExample()
        {
            try
            {
                // Simulacija greške
                throw new InvalidOperationException("Something went wrong!");
            }
            catch (Exception ex)
            {
                log.Error("Failed to process operation", ex);
                // Exception će biti upisana u EXCEPTION kolonu
            }
        }

        /// <summary>
        /// Primer 3: Logovanje sa custom context properties
        /// Ovo je NAJVAŽNIJI deo za migration tracking!
        /// </summary>
        public void ContextPropertiesExample(string workerId, string batchId, long documentId)
        {
            // Setuj context properties pre logovanja
            log4net.LogicalThreadContext.Properties["WorkerId"] = workerId;
            log4net.LogicalThreadContext.Properties["BatchId"] = batchId;
            log4net.LogicalThreadContext.Properties["DocumentId"] = documentId.ToString();
            log4net.LogicalThreadContext.Properties["UserId"] = "admin";

            try
            {
                log.Info($"Processing document {documentId}");

                // Simuliraj obradu
                ProcessDocument(documentId);

                log.Info($"Document {documentId} processed successfully");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to process document {documentId}", ex);
            }
            finally
            {
                // VAŽNO: Očisti context properties nakon obrade
                log4net.LogicalThreadContext.Properties.Remove("DocumentId");
            }
        }

        /// <summary>
        /// Primer 4: Batch processing sa context tracking
        /// </summary>
        public void BatchProcessingExample()
        {
            string batchId = $"Batch-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            string workerId = "Worker-1";

            // Setuj batch-level context (ostaje tokom cele obrade)
            log4net.LogicalThreadContext.Properties["WorkerId"] = workerId;
            log4net.LogicalThreadContext.Properties["BatchId"] = batchId;

            dbLog.Info($"Starting batch processing: {batchId}");

            var documentIds = new[] { 1001L, 1002L, 1003L, 1004L, 1005L };

            foreach (var docId in documentIds)
            {
                // Setuj document-level context (menja se za svaki dokument)
                log4net.LogicalThreadContext.Properties["DocumentId"] = docId.ToString();

                try
                {
                    fileLog.Debug($"Processing document {docId}");
                    ProcessDocument(docId);
                    dbLog.Info($"Document {docId} completed");
                }
                catch (Exception ex)
                {
                    hybridLog.Error($"Document {docId} failed", ex);
                }
                finally
                {
                    log4net.LogicalThreadContext.Properties.Remove("DocumentId");
                }
            }

            dbLog.Info($"Batch processing completed: {batchId}");

            // Cleanup batch-level context
            log4net.LogicalThreadContext.Properties.Remove("WorkerId");
            log4net.LogicalThreadContext.Properties.Remove("BatchId");
        }

        /// <summary>
        /// Primer 5: Conditional logging (performance optimization)
        /// </summary>
        public void ConditionalLoggingExample(long documentId)
        {
            // Proveri da li je Debug enabled pre skupog poziva
            if (log.IsDebugEnabled)
            {
                // Ova linija se izvršava SAMO ako je Debug level enabled
                log.Debug($"Processing details: {GetExpensiveDebugInfo(documentId)}");
            }

            // Info level - uvek loguj važne stvari
            log.Info($"Document {documentId} processed");
        }

        /// <summary>
        /// Primer 6: Različiti named loggers
        /// </summary>
        public void NamedLoggersExample()
        {
            // DbLogger - samo u bazu (za kritične stvari)
            dbLog.Info("Critical business operation completed");

            // FileLogger - samo u fajl (za debug i verbose logging)
            fileLog.Debug("Detailed debug information that we don't want in DB");

            // HybridLogger - i u bazu i u fajl (za jako važne događaje)
            hybridLog.Error("Critical error that needs to be in both places");

            // Default logger (iz klase) - konfigurisano u root section
            log.Warn("Generic warning");
        }

        /// <summary>
        /// Primer 7: UI Progress logging (za WPF aplikaciju)
        /// </summary>
        public void UiProgressLoggingExample()
        {
            var uiLog = LogManager.GetLogger("UiLogger");

            for (int i = 0; i <= 100; i += 10)
            {
                uiLog.Info($"Progress: {i}%");
                System.Threading.Thread.Sleep(500);
            }

            uiLog.Info("Operation completed!");
        }

        /// <summary>
        /// Primer 8: Using statement pattern za auto-cleanup
        /// </summary>
        public void AutoCleanupExample(long documentId)
        {
            using (new LogContext("DocumentId", documentId.ToString()))
            {
                log.Info("Processing started");
                ProcessDocument(documentId);
                log.Info("Processing completed");
                // Context property se automatski čisti na kraju using bloka
            }
        }

        // Helper metode
        private void ProcessDocument(long documentId)
        {
            // Simulacija obrade dokumenta
            System.Threading.Thread.Sleep(100);

            if (documentId % 10 == 0)
            {
                throw new Exception($"Failed to process document {documentId}");
            }
        }

        private string GetExpensiveDebugInfo(long documentId)
        {
            // Simulacija skupe operacije
            return $"Detailed info for doc {documentId}: {string.Join(",", Enumerable.Range(1, 1000))}";
        }
    }

    /// <summary>
    /// Helper klasa za automatsko čišćenje log context properties
    /// </summary>
    public class LogContext : IDisposable
    {
        private readonly string _propertyName;

        public LogContext(string propertyName, string propertyValue)
        {
            _propertyName = propertyName;
            log4net.LogicalThreadContext.Properties[propertyName] = propertyValue;
        }

        public void Dispose()
        {
            log4net.LogicalThreadContext.Properties.Remove(_propertyName);
        }
    }

    /// <summary>
    /// Program.cs primer - kako inicijalizovati logging
    /// </summary>
    public class ProgramExample
    {
        public static void Main(string[] args)
        {
            // 1. Inicijalizuj logging na startu aplikacije
            LoggerExample.InitializeLogging("MigrationService-Worker1");

            // 2. Koristi logger kroz aplikaciju
            var example = new LoggerExample();

            // Različiti primeri korišćenja
            example.SimpleLoggingExample();
            example.ContextPropertiesExample("Worker-1", "Batch-001", 12345);
            example.BatchProcessingExample();

            Console.WriteLine("Check database table 'AlfrescoMigration_Logger' for logged entries!");
        }
    }
}
