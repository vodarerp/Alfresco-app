-- ============================================================================
-- Debug Query - Folder Status Analysis
-- ============================================================================
-- Purpose: Analyze missing documents issue
-- ============================================================================

-- 1. Overall folder status summary
SELECT Status, COUNT(*) AS FolderCount
FROM FolderStaging
GROUP BY Status
ORDER BY Status;

-- 2. Folders in PROCESSED status (these should have documents)
SELECT COUNT(*) AS ProcessedFolders
FROM FolderStaging
WHERE Status = 'PROCESSED';

-- 3. Total documents in DocStaging
SELECT COUNT(*) AS TotalDocuments
FROM DocStaging;

-- 4. Documents grouped by status
SELECT Status, COUNT(*) AS DocCount
FROM DocStaging
GROUP BY Status
ORDER BY Status;

-- 5. Find folders that are PROCESSED but have NO documents in DocStaging
-- This is the SMOKING GUN query!
SELECT
    fs.Id,
    fs.NodeId,
    fs.Name,
    fs.Status,
    fs.UpdatedAt AS ProcessedAt,
    (SELECT COUNT(*)
     FROM DocStaging ds
     WHERE ds.ParentId = fs.NodeId) AS DocumentCount
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1
      FROM DocStaging ds
      WHERE ds.ParentId = fs.NodeId
  )
ORDER BY fs.UpdatedAt DESC;

-- 6. Count folders with PROCESSED status but no documents
SELECT COUNT(*) AS FoldersWithNoDocuments
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1
      FROM DocStaging ds
      WHERE ds.ParentId = fs.NodeId
  );

-- 7. Sample of folders WITH documents (to compare)
SELECT
    fs.Id,
    fs.NodeId,
    fs.Name,
    fs.Status,
    COUNT(ds.Id) AS DocumentCount,
    MIN(ds.CreatedAt) AS FirstDoc,
    MAX(ds.CreatedAt) AS LastDoc
FROM FolderStaging fs
LEFT JOIN DocStaging ds ON ds.ParentId = fs.NodeId
WHERE fs.Status = 'PROCESSED'
GROUP BY fs.Id, fs.NodeId, fs.Name, fs.Status
HAVING COUNT(ds.Id) > 0
ORDER BY COUNT(ds.Id) DESC
FETCH FIRST 20 ROWS ONLY;

-- 8. Check if there are folders stuck in IN PROGRESS
SELECT Status, COUNT(*) AS Count,
       MIN(UpdatedAt) AS OldestUpdate,
       MAX(UpdatedAt) AS NewestUpdate
FROM FolderStaging
WHERE Status = 'IN PROGRESS'
GROUP BY Status;

-- 9. Check folders in ERROR status
SELECT Id, NodeId, Name, Error, UpdatedAt
FROM FolderStaging
WHERE Status = 'ERROR'
ORDER BY UpdatedAt DESC
FETCH FIRST 20 ROWS ONLY;

-- 10. Timeline analysis - when did folders get processed?
SELECT
    TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24') AS ProcessedHour,
    COUNT(*) AS FoldersProcessed,
    SUM(CASE WHEN EXISTS (
        SELECT 1 FROM DocStaging ds WHERE ds.ParentId = FolderStaging.NodeId
    ) THEN 1 ELSE 0 END) AS FoldersWithDocs,
    SUM(CASE WHEN NOT EXISTS (
        SELECT 1 FROM DocStaging ds WHERE ds.ParentId = FolderStaging.NodeId
    ) THEN 1 ELSE 0 END) AS FoldersWithoutDocs
FROM FolderStaging
WHERE Status = 'PROCESSED'
GROUP BY TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24')
ORDER BY ProcessedHour DESC;

-- 11. Check MigrationCheckpoint for DocumentDiscovery service
SELECT
    ServiceName,
    TotalProcessed,
    TotalFailed,
    BatchCounter,
    TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24:MI:SS') AS LastUpdate
FROM MigrationCheckpoint
WHERE ServiceName = 'DocumentDiscovery';

-- 12. Sample folders without documents - detailed inspection
SELECT
    fs.Id,
    fs.NodeId,
    fs.Name,
    fs.Status,
    fs.CreatedAt,
    fs.UpdatedAt,
    fs.RetryCount,
    fs.Error,
    ROUND((fs.UpdatedAt - fs.CreatedAt) * 24 * 60 * 60) AS ProcessingTimeSeconds
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1 FROM DocStaging ds WHERE ds.ParentId = fs.NodeId
  )
ORDER BY fs.UpdatedAt ASC
FETCH FIRST 50 ROWS ONLY;
