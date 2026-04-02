-- =====================================================================
-- Korak 1: PreviewTypeMigration - kreiranje tabela
-- =====================================================================

-- -------------------------------------------------------
-- PreviewDocStaging
-- -------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PreviewDocStaging')
BEGIN
    CREATE TABLE PreviewDocStaging (
        Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
        NodeId                          NVARCHAR(100)   NULL,
        Name                            NVARCHAR(500)   NULL,
        NodeType                        NVARCHAR(200)   NULL,
        ParentId                        NVARCHAR(100)   NULL,
        DocDescription                  NVARCHAR(500)   NULL,
        OriginalDocumentCode            NVARCHAR(100)   NULL,
        NewDocumentCode                 NVARCHAR(100)   NULL,
        OldAlfrescoStatus               NVARCHAR(100)   NULL,
        NewAlfrescoStatus               NVARCHAR(100)   NULL,
        IsActive                        INT             NOT NULL DEFAULT 0,
        DocumentType                    NVARCHAR(100)   NULL,
        DocumentTypeMigration           NVARCHAR(100)   NULL,
        DossierType                     NVARCHAR(100)   NULL,
        TargetDossierType               NVARCHAR(100)   NULL,
        DossierDestinationFolderId      NVARCHAR(200)   NULL,
        DossierDestinationFolderName    NVARCHAR(200)   NULL,
        DossierDestinationFolderIsCreated INT           NOT NULL DEFAULT 0,
        Status                          NVARCHAR(50)    NULL,
        CoreId                          NVARCHAR(100)   NULL,
        ClientSegment                   NVARCHAR(100)   NULL,
        Source                          NVARCHAR(200)   NULL,
        CategoryCode                    NVARCHAR(100)   NULL,
        CategoryName                    NVARCHAR(200)   NULL,
        ContractNumber                  NVARCHAR(200)   NULL,
        ProductType                     NVARCHAR(100)   NULL,
        AccountNumbers                  NVARCHAR(MAX)   NULL,
        OriginalCreatedAt               DATETIME2       NULL,
        NewDocumentName                 NVARCHAR(500)   NULL,
        OriginalDocumentName            NVARCHAR(500)   NULL,
        FinalDocumentType               NVARCHAR(100)   NULL,
        RecordInserted                  DATETIME2       NOT NULL DEFAULT SYSDATETIME(),
        RecordExportedMigration         DATETIME2       NULL,
        -- Client API podaci
        ClientApiMbrJmbg                NVARCHAR(50)    NULL,
        ClientApiClientName             NVARCHAR(300)   NULL,
        ClientApiClientType             NVARCHAR(100)   NULL,
        ClientApiClientSubtype          NVARCHAR(100)   NULL,
        ClientApiResidency              NVARCHAR(50)    NULL,
        ClientApiSegment                NVARCHAR(100)   NULL,
        ClientApiStaff                  NVARCHAR(10)    NULL,
        ClientApiOpuUser                NVARCHAR(200)   NULL,
        ClientApiOpuRealization         NVARCHAR(200)   NULL,
        ClientApiBarclex                NVARCHAR(200)   NULL,
        ClientApiCollaborator           NVARCHAR(200)   NULL,
        ClientApiBarCLEXName            NVARCHAR(300)   NULL,
        ClientApiBarCLEXOpu             NVARCHAR(200)   NULL,
        ClientApiBarCLEXGroupName       NVARCHAR(300)   NULL,
        ClientApiBarCLEXGroupCode       NVARCHAR(100)   NULL,
        ClientApiBarCLEXCode            NVARCHAR(100)   NULL,
        Properties                      NVARCHAR(MAX)   NULL
    );

    -- Indeksi za performanse
    CREATE UNIQUE INDEX UX_PreviewDocStaging_NodeId
        ON PreviewDocStaging (NodeId)
        WHERE NodeId IS NOT NULL;

    CREATE INDEX IX_PreviewDocStaging_Status
        ON PreviewDocStaging (Status);

    CREATE INDEX IX_PreviewDocStaging_DossierType
        ON PreviewDocStaging (DossierType);

    CREATE INDEX IX_PreviewDocStaging_FolderName
        ON PreviewDocStaging (DossierDestinationFolderName)
        WHERE DossierDestinationFolderName IS NOT NULL;

    PRINT 'PreviewDocStaging tabela kreirana.';
END
ELSE
    PRINT 'PreviewDocStaging tabela vec postoji, preskacemo.';
GO

-- -------------------------------------------------------
-- PreviewLoadCheckpoint
-- -------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PreviewLoadCheckpoint')
BEGIN
    CREATE TABLE PreviewLoadCheckpoint (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        FolderType      NVARCHAR(20)    NOT NULL,   -- 'PI' ili 'LE'
        TotalFetched    BIGINT          NOT NULL DEFAULT 0,
        UpdatedAt       DATETIME2       NOT NULL DEFAULT SYSDATETIME()
    );

    CREATE UNIQUE INDEX UX_PreviewLoadCheckpoint_FolderType
        ON PreviewLoadCheckpoint (FolderType);

    PRINT 'PreviewLoadCheckpoint tabela kreirana.';
END
ELSE
    PRINT 'PreviewLoadCheckpoint tabela vec postoji, preskacemo.';
GO
