-- ============================================================================
-- CLEAR ALL TEST DATA - Global Rubber MMM
--
-- DELETES (no backup):
--   * every masters.*, transactions.* and audit.* table (molds, machines, products, departments, employees,
--     vendors, spare parts, checklists, maintenance / breakdown types, production, PM, breakdowns, spare-part
--     usage and stock, notifications, audit and application logs)
--
-- KEEPS (untouched):
--   * security.*      - ALL users, roles, permissions and modules (and login sessions)
--   * configuration.* - system settings, user settings and document sequences
--                       (sequence counters RESET so codes start at 0001 again - except USER / ROLE: those are kept)
--
-- Users keep their login and role; only their link to an employee / department is cleared (those masters are deleted).
-- All in ONE transaction: any error rolls everything back. Identity counters of the emptied tables are reset.
-- Run in SSMS against the correct database.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];   -- check the database name before running
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- required by the computed / filtered-index columns (SSMS sets it; sqlcmd does not)
SET ANSI_NULLS ON;

DECLARE @sql nvarchar(max) = N'';

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Switch off every foreign key so the delete order does not matter.
    SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ALL;' + CHAR(10)
    FROM sys.tables t
    WHERE t.is_ms_shipped = 0;
    EXEC sp_executesql @sql;

    -- 2. Users stay, but the employee / department they point at is deleted below.
    UPDATE [security].[user_master] SET employee_id = NULL, department_id = NULL
    WHERE employee_id IS NOT NULL OR department_id IS NOT NULL;
    PRINT 'security.user_master: employee / department link cleared on ' + CAST(@@ROWCOUNT AS varchar(20)) + ' user(s)';

    -- 3. Empty every table in masters, transactions and audit.
    SET @sql = N'';
    SELECT @sql += N'DELETE FROM ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10)
                 + N'PRINT ''' + SCHEMA_NAME(t.schema_id) + N'.' + t.name + N': '' + CAST(@@ROWCOUNT AS varchar(20)) + '' row(s) deleted'';' + CHAR(10)
    FROM sys.tables t
    WHERE t.is_ms_shipped = 0
      AND SCHEMA_NAME(t.schema_id) IN (N'masters', N'transactions', N'audit');
    EXEC sp_executesql @sql;

    -- 4. Document numbers start again (MLD-0001, MAC-0001, ...). USER / ROLE keep counting: those records are kept.
    UPDATE [configuration].[document_sequence_configuration] SET last_number = 0, period_key = NULL
    WHERE document_type NOT IN ('USER', 'ROLE');
    PRINT 'configuration.document_sequence_configuration: ' + CAST(@@ROWCOUNT AS varchar(20)) + ' counter(s) reset to 0';

    -- 5. Switch every foreign key back on AND re-validate it (any problem -> everything rolls back).
    SET @sql = N'';
    SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' WITH CHECK CHECK CONSTRAINT ALL;' + CHAR(10)
    FROM sys.tables t
    WHERE t.is_ms_shipped = 0;
    EXEC sp_executesql @sql;

    COMMIT TRANSACTION;
    PRINT '--- Test data cleared. Users, roles, permissions and modules are kept. ---';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @msg nvarchar(4000) = ERROR_MESSAGE();
    RAISERROR('Nothing was deleted (rolled back): %s', 16, 1, @msg);
    RETURN;
END CATCH;

-- 6. Reset identity counters of the emptied tables so new ids start at 1 (outside the transaction: DBCC).
--    Only tables whose identity was used; a never-used table already starts at its seed.
DECLARE @reseed nvarchar(max) = N'';
SELECT @reseed += N'DBCC CHECKIDENT (''' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N''', RESEED, 0) WITH NO_INFOMSGS;' + CHAR(10)
FROM sys.tables t
JOIN sys.identity_columns ic ON ic.object_id = t.object_id
WHERE t.is_ms_shipped = 0
  AND ic.last_value IS NOT NULL
  AND SCHEMA_NAME(t.schema_id) IN (N'masters', N'transactions', N'audit');
EXEC sp_executesql @reseed;
PRINT '--- Identity counters reset. ---';
GO
