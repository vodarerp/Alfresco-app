USE [AlfrescoMigration]
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_nodeid' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_nodeid ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_status' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_status ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_parentid' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_parentid ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_coreid' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_coreid ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_documenttype' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_documenttype ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_targetdossiertype' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_targetdossiertype ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_docstaging_status_created' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX idx_docstaging_status_created ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocStaging_DestinationFolderId' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX IX_DocStaging_DestinationFolderId ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocStaging_ContractNumber' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX IX_DocStaging_ContractNumber ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocStaging_DutOfferId' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX IX_DocStaging_DutOfferId ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocStaging_DossierDestFolderId' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX IX_DocStaging_DossierDestFolderId ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocStaging_ProductType' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX IX_DocStaging_ProductType ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocStaging_ClientSegment' AND object_id = OBJECT_ID('dbo.DocStaging'))
    DROP INDEX IX_DocStaging_ClientSegment ON dbo.DocStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_folderstaging_nodeid' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX idx_folderstaging_nodeid ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_folderstaging_status' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX idx_folderstaging_status ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_folderstaging_coreid' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX idx_folderstaging_coreid ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_folderstaging_parentid' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX idx_folderstaging_parentid ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_folderstaging_targetdossiertype' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX idx_folderstaging_targetdossiertype ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_folderstaging_status_created' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX idx_folderstaging_status_created ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FolderStaging_DestFolderId' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX IX_FolderStaging_DestFolderId ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FolderStaging_DossierDestFolderId' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX IX_FolderStaging_DossierDestFolderId ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FolderStaging_UniqueIdentifier' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX IX_FolderStaging_UniqueIdentifier ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FolderStaging_ContractNumber' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX IX_FolderStaging_ContractNumber ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FolderStaging_ProductType' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX IX_FolderStaging_ProductType ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FolderStaging_ClientSegment' AND object_id = OBJECT_ID('dbo.FolderStaging'))
    DROP INDEX IX_FolderStaging_ClientSegment ON dbo.FolderStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_checkpoint_servicename' AND object_id = OBJECT_ID('dbo.MigrationCheckpoint'))
    DROP INDEX idx_checkpoint_servicename ON dbo.MigrationCheckpoint;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_logger_date' AND object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger'))
    DROP INDEX idx_logger_date ON dbo.AlfrescoMigration_Logger;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_logger_level' AND object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger'))
    DROP INDEX idx_logger_level ON dbo.AlfrescoMigration_Logger;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_logger_documentid' AND object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger'))
    DROP INDEX idx_logger_documentid ON dbo.AlfrescoMigration_Logger;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_logger_batchid' AND object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger'))
    DROP INDEX idx_logger_batchid ON dbo.AlfrescoMigration_Logger;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_logger_level_date' AND object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger'))
    DROP INDEX idx_logger_level_date ON dbo.AlfrescoMigration_Logger;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Logger_WorkerId' AND object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger'))
    DROP INDEX IX_Logger_WorkerId ON dbo.AlfrescoMigration_Logger;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_phasecheckpoints_phase' AND object_id = OBJECT_ID('dbo.PhaseCheckpoints'))
    DROP INDEX idx_phasecheckpoints_phase ON dbo.PhaseCheckpoints;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_phasecheckpoints_updated' AND object_id = OBJECT_ID('dbo.PhaseCheckpoints'))
    DROP INDEX idx_phasecheckpoints_updated ON dbo.PhaseCheckpoints;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_phasecheckpoints_status' AND object_id = OBJECT_ID('dbo.PhaseCheckpoints'))
    DROP INDEX idx_phasecheckpoints_status ON dbo.PhaseCheckpoints;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_phasecheckpoints_status_phase' AND object_id = OBJECT_ID('dbo.PhaseCheckpoints'))
    DROP INDEX idx_phasecheckpoints_status_phase ON dbo.PhaseCheckpoints;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpdocstaging_nodeid' AND object_id = OBJECT_ID('dbo.KdpDocumentStaging'))
    DROP INDEX idx_kdpdocstaging_nodeid ON dbo.KdpDocumentStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpdocstaging_accfolder' AND object_id = OBJECT_ID('dbo.KdpDocumentStaging'))
    DROP INDEX idx_kdpdocstaging_accfolder ON dbo.KdpDocumentStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpdocstaging_status' AND object_id = OBJECT_ID('dbo.KdpDocumentStaging'))
    DROP INDEX idx_kdpdocstaging_status ON dbo.KdpDocumentStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpdocstaging_doctype' AND object_id = OBJECT_ID('dbo.KdpDocumentStaging'))
    DROP INDEX idx_kdpdocstaging_doctype ON dbo.KdpDocumentStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_KdpDocStaging_CoreId' AND object_id = OBJECT_ID('dbo.KdpDocumentStaging'))
    DROP INDEX IX_KdpDocStaging_CoreId ON dbo.KdpDocumentStaging;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpexport_nodeid' AND object_id = OBJECT_ID('dbo.KdpExportResult'))
    DROP INDEX idx_kdpexport_nodeid ON dbo.KdpExportResult;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpexport_coreid' AND object_id = OBJECT_ID('dbo.KdpExportResult'))
    DROP INDEX idx_kdpexport_coreid ON dbo.KdpExportResult;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_kdpexport_accfolder' AND object_id = OBJECT_ID('dbo.KdpExportResult'))
    DROP INDEX idx_kdpexport_accfolder ON dbo.KdpExportResult;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_KdpExport_TipDokumenta' AND object_id = OBJECT_ID('dbo.KdpExportResult'))
    DROP INDEX IX_KdpExport_TipDokumenta ON dbo.KdpExportResult;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NAZIV' AND object_id = OBJECT_ID('dbo.DocumentMappings'))
    DROP INDEX IX_DocumentMappings_NAZIV ON dbo.DocumentMappings;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_sifraDokumenta' AND object_id = OBJECT_ID('dbo.DocumentMappings'))
    DROP INDEX IX_DocumentMappings_sifraDokumenta ON dbo.DocumentMappings;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NazivDokumenta' AND object_id = OBJECT_ID('dbo.DocumentMappings'))
    DROP INDEX IX_DocumentMappings_NazivDokumenta ON dbo.DocumentMappings;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NazivDokumentaMigracija' AND object_id = OBJECT_ID('dbo.DocumentMappings'))
    DROP INDEX IX_DocumentMappings_NazivDokumentaMigracija ON dbo.DocumentMappings;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_TipDosijea' AND object_id = OBJECT_ID('dbo.DocumentMappings'))
    DROP INDEX IX_DocumentMappings_TipDosijea ON dbo.DocumentMappings;
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_Search_Composite' AND object_id = OBJECT_ID('dbo.DocumentMappings'))
    DROP INDEX IX_DocumentMappings_Search_Composite ON dbo.DocumentMappings;
GO

CREATE NONCLUSTERED INDEX idx_docstaging_nodeid ON dbo.DocStaging(NodeId);
GO

CREATE NONCLUSTERED INDEX idx_docstaging_status ON dbo.DocStaging(Status);
GO

CREATE NONCLUSTERED INDEX idx_docstaging_parentid ON dbo.DocStaging(ParentId);
GO

CREATE NONCLUSTERED INDEX idx_docstaging_coreid ON dbo.DocStaging(CoreId) WHERE CoreId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_docstaging_documenttype ON dbo.DocStaging(DocumentType) WHERE DocumentType IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_docstaging_targetdossiertype ON dbo.DocStaging(TargetDossierType) WHERE TargetDossierType IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_docstaging_status_created ON dbo.DocStaging(Status, CreatedAt);
GO

CREATE NONCLUSTERED INDEX IX_DocStaging_DestinationFolderId ON dbo.DocStaging(DestinationFolderId) INCLUDE (Id, NodeId, Status) WHERE DestinationFolderId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_DocStaging_ContractNumber ON dbo.DocStaging(ContractNumber) WHERE ContractNumber IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_DocStaging_DutOfferId ON dbo.DocStaging(DutOfferId) WHERE DutOfferId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_DocStaging_DossierDestFolderId ON dbo.DocStaging(DossierDestFolderId) WHERE DossierDestFolderId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_DocStaging_ProductType ON dbo.DocStaging(ProductType) WHERE ProductType IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_DocStaging_ClientSegment ON dbo.DocStaging(ClientSegment) WHERE ClientSegment IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_folderstaging_nodeid ON dbo.FolderStaging(NodeId);
GO

CREATE NONCLUSTERED INDEX idx_folderstaging_status ON dbo.FolderStaging(Status);
GO

CREATE NONCLUSTERED INDEX idx_folderstaging_coreid ON dbo.FolderStaging(CoreId) WHERE CoreId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_folderstaging_parentid ON dbo.FolderStaging(ParentId) WHERE ParentId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_folderstaging_targetdossiertype ON dbo.FolderStaging(TargetDossierType) WHERE TargetDossierType IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_folderstaging_status_created ON dbo.FolderStaging(Status, CreatedAt);
GO

CREATE NONCLUSTERED INDEX IX_FolderStaging_DestFolderId ON dbo.FolderStaging(DestFolderId) WHERE DestFolderId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_FolderStaging_DossierDestFolderId ON dbo.FolderStaging(DossierDestFolderId) WHERE DossierDestFolderId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_FolderStaging_UniqueIdentifier ON dbo.FolderStaging(UniqueIdentifier) WHERE UniqueIdentifier IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_FolderStaging_ContractNumber ON dbo.FolderStaging(ContractNumber) WHERE ContractNumber IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_FolderStaging_ProductType ON dbo.FolderStaging(ProductType) WHERE ProductType IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_FolderStaging_ClientSegment ON dbo.FolderStaging(ClientSegment) WHERE ClientSegment IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_checkpoint_servicename ON dbo.MigrationCheckpoint(ServiceName);
GO

CREATE NONCLUSTERED INDEX idx_logger_date ON dbo.AlfrescoMigration_Logger(LOG_DATE DESC);
GO

CREATE NONCLUSTERED INDEX idx_logger_level ON dbo.AlfrescoMigration_Logger(LOG_LEVEL);
GO

CREATE NONCLUSTERED INDEX idx_logger_documentid ON dbo.AlfrescoMigration_Logger(DOCUMENTID) WHERE DOCUMENTID IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_logger_batchid ON dbo.AlfrescoMigration_Logger(BATCHID) WHERE BATCHID IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_logger_level_date ON dbo.AlfrescoMigration_Logger(LOG_LEVEL, LOG_DATE DESC);
GO

CREATE NONCLUSTERED INDEX IX_Logger_WorkerId ON dbo.AlfrescoMigration_Logger(WORKERID) WHERE WORKERID IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX idx_phasecheckpoints_phase ON dbo.PhaseCheckpoints(Phase);
GO

CREATE NONCLUSTERED INDEX idx_phasecheckpoints_updated ON dbo.PhaseCheckpoints(UpdatedAt DESC);
GO

CREATE NONCLUSTERED INDEX idx_phasecheckpoints_status ON dbo.PhaseCheckpoints(Status);
GO

CREATE NONCLUSTERED INDEX idx_phasecheckpoints_status_phase ON dbo.PhaseCheckpoints(Status, Phase);
GO

CREATE UNIQUE NONCLUSTERED INDEX idx_kdpdocstaging_nodeid ON dbo.KdpDocumentStaging(NodeId);
GO

CREATE NONCLUSTERED INDEX idx_kdpdocstaging_accfolder ON dbo.KdpDocumentStaging(AccFolderName);
GO

CREATE NONCLUSTERED INDEX idx_kdpdocstaging_status ON dbo.KdpDocumentStaging(DocumentStatus);
GO

CREATE NONCLUSTERED INDEX idx_kdpdocstaging_doctype ON dbo.KdpDocumentStaging(DocumentType);
GO

CREATE NONCLUSTERED INDEX IX_KdpDocStaging_CoreId ON dbo.KdpDocumentStaging(CoreId) WHERE CoreId IS NOT NULL;
GO

CREATE UNIQUE NONCLUSTERED INDEX idx_kdpexport_nodeid ON dbo.KdpExportResult(ReferencaDokumenta);
GO

CREATE NONCLUSTERED INDEX idx_kdpexport_coreid ON dbo.KdpExportResult(KlijentskiBroj);
GO

CREATE NONCLUSTERED INDEX idx_kdpexport_accfolder ON dbo.KdpExportResult(AccFolderName);
GO

CREATE NONCLUSTERED INDEX IX_KdpExport_TipDokumenta ON dbo.KdpExportResult(TipDokumenta) WHERE TipDokumenta IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_DocumentMappings_NAZIV ON dbo.DocumentMappings(NAZIV) INCLUDE (ID, BROJ_DOKUMENATA, sifraDokumenta, NazivDokumenta, TipDosijea, TipProizvoda, SifraDokumentaMigracija, NazivDokumentaMigracija, ExcelFileName, ExcelFileSheet) WITH (FILLFACTOR = 90);
GO

CREATE NONCLUSTERED INDEX IX_DocumentMappings_sifraDokumenta ON dbo.DocumentMappings(sifraDokumenta) INCLUDE (ID, NAZIV, BROJ_DOKUMENATA, NazivDokumenta, TipDosijea, TipProizvoda, SifraDokumentaMigracija, NazivDokumentaMigracija, ExcelFileName, ExcelFileSheet) WITH (FILLFACTOR = 90);
GO

CREATE NONCLUSTERED INDEX IX_DocumentMappings_NazivDokumenta ON dbo.DocumentMappings(NazivDokumenta) INCLUDE (ID, NAZIV, BROJ_DOKUMENATA, sifraDokumenta, TipDosijea, TipProizvoda, SifraDokumentaMigracija, NazivDokumentaMigracija, ExcelFileName, ExcelFileSheet) WITH (FILLFACTOR = 90);
GO

CREATE NONCLUSTERED INDEX IX_DocumentMappings_NazivDokumentaMigracija ON dbo.DocumentMappings(NazivDokumentaMigracija) INCLUDE (ID, NAZIV, BROJ_DOKUMENATA, sifraDokumenta, NazivDokumenta, TipDosijea, TipProizvoda, SifraDokumentaMigracija, ExcelFileName, ExcelFileSheet) WITH (FILLFACTOR = 90);
GO

CREATE NONCLUSTERED INDEX IX_DocumentMappings_TipDosijea ON dbo.DocumentMappings(TipDosijea) INCLUDE (ID, NAZIV, BROJ_DOKUMENATA, sifraDokumenta, NazivDokumenta) WITH (FILLFACTOR = 90);
GO

CREATE NONCLUSTERED INDEX IX_DocumentMappings_Search_Composite ON dbo.DocumentMappings(NAZIV, NazivDokumenta, sifraDokumenta, TipDosijea) INCLUDE (ID, BROJ_DOKUMENATA, SifraDokumentaMigracija, TipProizvoda, NazivDokumentaMigracija) WITH (FILLFACTOR = 90);
GO

UPDATE STATISTICS dbo.DocStaging WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.FolderStaging WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.MigrationCheckpoint WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.AlfrescoMigration_Logger WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.PhaseCheckpoints WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.KdpDocumentStaging WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.KdpExportResult WITH FULLSCAN;
GO

UPDATE STATISTICS dbo.DocumentMappings WITH FULLSCAN;
GO
