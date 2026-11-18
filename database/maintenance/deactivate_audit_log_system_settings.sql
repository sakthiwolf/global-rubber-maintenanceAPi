-- ============================================================================
-- Audit Log and System Settings are not used in this project (removed from the menu on 2026-11-18).
-- Marks their two modules inactive and hidden, so they disappear from Roles & Permissions (the matrix lists only
-- active modules). Nothing is deleted: their permission rows stay, and the change can be undone by setting
-- is_active / is_menu_visible back to 1.
-- The audit trail itself is NOT affected - the system still records changes in audit.audit_log.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];   -- check the database name before running
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

UPDATE [security].[module_master]
SET is_active = 0, is_menu_visible = 0, updated_at = SYSUTCDATETIME()
WHERE module_code IN ('ADMIN_AUDIT_LOG', 'ADMIN_SYSTEM_SETTINGS');

PRINT CAST(@@ROWCOUNT AS varchar(10)) + ' module(s) deactivated.';

SELECT module_code, module_name, is_active, is_menu_visible
FROM [security].[module_master]
WHERE module_code IN ('ADMIN_AUDIT_LOG', 'ADMIN_SYSTEM_SETTINGS');
GO
