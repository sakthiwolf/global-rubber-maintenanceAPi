-- ============================================================================
-- STEP 00: PRE-CHECK on the ERP database (read-only, changes nothing)
-- Replace <ERP_DB_NAME> and run. Review every result set before running 01.
-- ============================================================================
USE [<ERP_DB_NAME>];
GO
SET NOCOUNT ON;

-- 1. Server version + DB collation. MMM source is SQL Server 2022, Latin1_General_CI_AS.
--    A different ERP collation is OK, but joins between ERP varchar columns and MMM
--    columns (e.g. mold_code) will need COLLATE DATABASE_DEFAULT in the integration step.
SELECT @@VERSION AS server_version,
       DB_NAME() AS database_name,
       DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS erp_collation,
       'Latin1_General_CI_AS' AS mmm_collation,
       compatibility_level
FROM sys.databases WHERE name = DB_NAME();

-- 2. Do the MMM schemas already exist in the ERP DB? (any rows = review ownership/conflict)
SELECT s.name AS existing_schema, COUNT(o.object_id) AS objects_inside
FROM sys.schemas s
LEFT JOIN sys.objects o ON o.schema_id = s.schema_id AND o.is_ms_shipped = 0
WHERE s.name IN ('security','configuration','masters','transactions','audit')
GROUP BY s.name;

-- 3. Table name clashes: same schema + table name already present (MUST be empty)
SELECT s.name AS schema_name, t.name AS table_name, 'ALREADY EXISTS - 01 will SKIP it' AS note
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN ('security','configuration','masters','transactions','audit')
  AND t.name IN (
   'module_master','permission_master','role_master','user_master','user_refresh_token',
   'document_sequence_configuration','system_settings_configuration','user_settings_configuration',
   'breakdown_type_master','department_master','employee_master','machine_master',
   'maintenance_checklist_item_master','maintenance_checklist_master','maintenance_type_master',
   'mold_master','product_master','spare_part_master','vendor_master',
   'breakdown_stage_transaction','machine_breakdown_transaction','machine_pm_checklist_transaction',
   'machine_pm_transaction','maintenance_work_order_transaction','mold_pm_checklist_transaction',
   'mold_pm_transaction','notification_read_transaction','notification_transaction',
   'production_entry_transaction','spare_part_stock_transaction','spare_part_usage_transaction',
   'application_log','audit_log','audit_log_detail');

-- 4. Constraint/index name clashes (constraint names are unique per schema)
SELECT SCHEMA_NAME(schema_id) AS schema_name, name AS object_name, type_desc
FROM sys.objects
WHERE type IN ('PK','UQ','F','C','D')
  AND SCHEMA_NAME(schema_id) IN ('security','configuration','masters','transactions','audit');

-- 5. Current user permissions needed for 01-03
SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE SCHEMA') AS can_create_schema,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE')  AS can_create_table,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER')         AS can_alter_db;

-- 6. Find the ERP production / mold / item tables (needed for the integration step)
SELECT s.name AS schema_name, t.name AS table_name, p.rows
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name LIKE '%prod%' OR t.name LIKE '%mold%' OR t.name LIKE '%mould%'
   OR t.name LIKE '%die%'  OR t.name LIKE '%shot%' OR t.name LIKE '%item%'
   OR t.name LIKE '%machine%' OR t.name LIKE '%shift%'
ORDER BY s.name, t.name;
