-- =====================================================
-- Dotmim.Sync Pre-Provisioned Object Validation Script
-- =====================================================
-- Purpose: Validates that all required Dotmim.Sync objects exist
-- Usage: Run this script on your client database after applying migrations
-- =====================================================

SET NOCOUNT ON;

DECLARE @SchemaName NVARCHAR(128) = 'dbo';  -- Change to your schema (e.g., 'Report')
DECLARE @ScopeName NVARCHAR(128) = 'DefaultScope'; -- Change to your scope name if different

-- Table names to sync (CUSTOMIZE THIS LIST)
DECLARE @Tables TABLE (TableName NVARCHAR(128))
INSERT INTO @Tables VALUES
    ('ProductCategory'),
    ('Product')
    -- Add all your synced tables here

-- Prefixes/Suffixes (CUSTOMIZE if you used custom values in SyncSetup)
DECLARE @TrackingTableSuffix NVARCHAR(50) = '_tracking';
DECLARE @StoredProcedurePrefix NVARCHAR(50) = '';
DECLARE @StoredProcedureSuffix NVARCHAR(50) = '';
DECLARE @TriggerPrefix NVARCHAR(50) = '';
DECLARE @TriggerSuffix NVARCHAR(50) = '';

-- Results table
DECLARE @MissingObjects TABLE (
    ObjectType VARCHAR(50),
    ObjectName NVARCHAR(255),
    Severity VARCHAR(20),
    Impact NVARCHAR(500)
)

PRINT '========================================='
PRINT 'Dotmim.Sync Object Validation'
PRINT '========================================='
PRINT 'Schema: ' + @SchemaName
PRINT 'Scope: ' + @ScopeName
PRINT ''

-- =====================================================
-- 1. Check Global Scope Tables
-- =====================================================
PRINT 'Checking global scope tables...'

IF NOT EXISTS (
    SELECT * FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE t.name = 'scope_info' AND s.name = @SchemaName
)
BEGIN
    INSERT INTO @MissingObjects VALUES (
        'Table',
        @SchemaName + '.scope_info',
        'CRITICAL',
        'Sync initialization will fail immediately'
    )
END

IF NOT EXISTS (
    SELECT * FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE t.name = 'scope_info_client' AND s.name = @SchemaName
)
BEGIN
    INSERT INTO @MissingObjects VALUES (
        'Table',
        @SchemaName + '.scope_info_client',
        'CRITICAL',
        'Client scope tracking will fail'
    )
END

-- =====================================================
-- 2. Check Per-Table Objects
-- =====================================================
PRINT 'Checking per-table objects...'

DECLARE @CurrentTable NVARCHAR(128)
DECLARE @FullTableName NVARCHAR(255)
DECLARE @TrackingTableName NVARCHAR(255)
DECLARE @ObjectName NVARCHAR(255)

DECLARE table_cursor CURSOR FOR
SELECT TableName FROM @Tables

OPEN table_cursor
FETCH NEXT FROM table_cursor INTO @CurrentTable

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @FullTableName = QUOTENAME(@SchemaName) + '.' + QUOTENAME(@CurrentTable)
    SET @TrackingTableName = @CurrentTable + @TrackingTableSuffix

    PRINT '  Table: ' + @FullTableName

    -- Check Base Table (optional - may already exist)
    IF NOT EXISTS (
        SELECT * FROM sys.tables t
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        WHERE t.name = @CurrentTable AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'Table',
            @FullTableName,
            'WARNING',
            'Base table does not exist (may be expected if client is download-only)'
        )
    END

    -- Check Tracking Table
    IF NOT EXISTS (
        SELECT * FROM sys.tables t
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        WHERE t.name = @TrackingTableName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'Table',
            @SchemaName + '.' + @TrackingTableName,
            'CRITICAL',
            'Change tracking will fail - sync will crash'
        )
    END

    -- Check Triggers
    SET @ObjectName = @TriggerPrefix + @CurrentTable + '_insert_trigger' + @TriggerSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.triggers t
        INNER JOIN sys.tables tb ON t.parent_id = tb.object_id
        INNER JOIN sys.schemas s ON tb.schema_id = s.schema_id
        WHERE t.name = @ObjectName AND tb.name = @CurrentTable AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'Trigger',
            @SchemaName + '.' + @ObjectName,
            'CRITICAL',
            'INSERT changes will NOT be tracked - SILENT DATA LOSS!'
        )
    END

    SET @ObjectName = @TriggerPrefix + @CurrentTable + '_update_trigger' + @TriggerSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.triggers t
        INNER JOIN sys.tables tb ON t.parent_id = tb.object_id
        INNER JOIN sys.schemas s ON tb.schema_id = s.schema_id
        WHERE t.name = @ObjectName AND tb.name = @CurrentTable AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'Trigger',
            @SchemaName + '.' + @ObjectName,
            'CRITICAL',
            'UPDATE changes will NOT be tracked - SILENT DATA LOSS!'
        )
    END

    SET @ObjectName = @TriggerPrefix + @CurrentTable + '_delete_trigger' + @TriggerSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.triggers t
        INNER JOIN sys.tables tb ON t.parent_id = tb.object_id
        INNER JOIN sys.schemas s ON tb.schema_id = s.schema_id
        WHERE t.name = @ObjectName AND tb.name = @CurrentTable AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'Trigger',
            @SchemaName + '.' + @ObjectName,
            'CRITICAL',
            'DELETE changes will NOT be tracked - SILENT DATA LOSS!'
        )
    END

    -- Check Stored Procedures
    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_selectchanges' + @StoredProcedureSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'CRITICAL',
            'SelectChanges will fail - sync will crash'
        )
    END

    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_selectchanges_filtered' + @StoredProcedureSuffix
    -- Only check if filters are used - this may not exist for non-filtered tables
    -- Commenting out by default - uncomment if you use filters
    /*
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'WARNING',
            'Filtered SelectChanges may fail if filters are used'
        )
    END
    */

    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_initialize' + @StoredProcedureSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'CRITICAL',
            'Initial sync will fail'
        )
    END

    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_update' + @StoredProcedureSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'WARNING',
            'Update/Insert operations may fail (OK for download-only)'
        )
    END

    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_delete' + @StoredProcedureSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'WARNING',
            'Delete operations may fail (OK for download-only)'
        )
    END

    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_deletemetadata' + @StoredProcedureSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'LOW',
            'Metadata cleanup may fail (not critical for normal sync)'
        )
    END

    SET @ObjectName = @StoredProcedurePrefix + @CurrentTable + '_reset' + @StoredProcedureSuffix
    IF NOT EXISTS (
        SELECT * FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        WHERE p.name = @ObjectName AND s.name = @SchemaName
    )
    BEGIN
        INSERT INTO @MissingObjects VALUES (
            'StoredProcedure',
            @SchemaName + '.' + @ObjectName,
            'LOW',
            'Reset operations will fail (rarely used)'
        )
    END

    FETCH NEXT FROM table_cursor INTO @CurrentTable
END

CLOSE table_cursor
DEALLOCATE table_cursor

-- =====================================================
-- 3. Display Results
-- =====================================================
PRINT ''
PRINT '========================================='
PRINT 'VALIDATION RESULTS'
PRINT '========================================='

IF NOT EXISTS (SELECT * FROM @MissingObjects)
BEGIN
    PRINT ''
    PRINT '✓ SUCCESS: All required Dotmim.Sync objects exist!'
    PRINT ''
    PRINT 'Your database is ready for synchronization with DisableProvisioning = true'
END
ELSE
BEGIN
    PRINT ''
    PRINT '✗ VALIDATION FAILED: Missing objects detected'
    PRINT ''

    -- Critical issues
    IF EXISTS (SELECT * FROM @MissingObjects WHERE Severity = 'CRITICAL')
    BEGIN
        PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
        PRINT '⚠️  CRITICAL ISSUES (Sync will fail)'
        PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
        SELECT
            ObjectType,
            ObjectName,
            Impact
        FROM @MissingObjects
        WHERE Severity = 'CRITICAL'
        ORDER BY ObjectType, ObjectName
        PRINT ''
    END

    -- Warnings
    IF EXISTS (SELECT * FROM @MissingObjects WHERE Severity = 'WARNING')
    BEGIN
        PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
        PRINT '⚠️  WARNINGS (May cause issues)'
        PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
        SELECT
            ObjectType,
            ObjectName,
            Impact
        FROM @MissingObjects
        WHERE Severity = 'WARNING'
        ORDER BY ObjectType, ObjectName
        PRINT ''
    END

    -- Low severity
    IF EXISTS (SELECT * FROM @MissingObjects WHERE Severity = 'LOW')
    BEGIN
        PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
        PRINT 'ℹ️  INFORMATIONAL (Low impact)'
        PRINT '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
        SELECT
            ObjectType,
            ObjectName,
            Impact
        FROM @MissingObjects
        WHERE Severity = 'LOW'
        ORDER BY ObjectType, ObjectName
        PRINT ''
    END

    DECLARE @CriticalCount INT = (SELECT COUNT(*) FROM @MissingObjects WHERE Severity = 'CRITICAL')
    DECLARE @WarningCount INT = (SELECT COUNT(*) FROM @MissingObjects WHERE Severity = 'WARNING')
    DECLARE @LowCount INT = (SELECT COUNT(*) FROM @MissingObjects WHERE Severity = 'LOW')

    PRINT ''
    PRINT 'Summary: ' + CAST(@CriticalCount AS VARCHAR) + ' critical, ' +
          CAST(@WarningCount AS VARCHAR) + ' warnings, ' +
          CAST(@LowCount AS VARCHAR) + ' informational'
    PRINT ''
    PRINT 'ACTION REQUIRED:'
    PRINT '1. Review the missing objects above'
    PRINT '2. Apply the required migration scripts'
    PRINT '3. Re-run this validation script'
    PRINT '4. Only deploy client app when validation passes'
    PRINT ''

    -- Raise error to fail CI/CD pipelines
    IF @CriticalCount > 0
    BEGIN
        RAISERROR('Validation failed: Missing critical Dotmim.Sync objects', 16, 1)
    END
END

PRINT '========================================='
