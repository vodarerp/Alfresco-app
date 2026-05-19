-- Adds skip-set tracking columns to PreviewLoadCheckpoint.
-- All new columns are nullable — fully backward compatible.
-- TotalFetched remains as high-water mark for UI display and fallback on rows
-- where the new columns are still NULL.

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'PreviewLoadCheckpoint' AND COLUMN_NAME = 'ProcessedSkipsJson')
BEGIN
    ALTER TABLE PreviewLoadCheckpoint
        ADD ProcessedSkipsJson NVARCHAR(MAX) NULL,
            FailedSkipsJson    NVARCHAR(MAX) NULL,
            LastUpdatedAt      DATETIME2     NULL;
    PRINT 'PreviewLoadCheckpoint: ProcessedSkipsJson, FailedSkipsJson, LastUpdatedAt added.';
END
ELSE
    PRINT 'PreviewLoadCheckpoint: skip-tracking columns already exist, skipping.';
GO
