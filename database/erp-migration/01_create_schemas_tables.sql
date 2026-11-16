-- ============================================================================
-- Global Rubber MMM  ->  ERP database migration
-- Generated 2026-11-16 from live DB CHE_WA_Global_Rubber_MMM (SQL Server 2022)
-- Run order: 00 -> 01 -> 02 -> 03   (04 comes in the integration phase)
-- BEFORE RUNNING: replace <ERP_DB_NAME> below with the ERP database name.
-- ============================================================================
USE [<ERP_DB_NAME>];
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- STEP 01: schemas, 34 tables (with PK / UNIQUE / CHECK / DEFAULT / computed columns), 60 indexes
-- Foreign keys are added in 03 AFTER master data is loaded.

IF SCHEMA_ID(N'security') IS NULL EXEC(N'CREATE SCHEMA [security]');
IF SCHEMA_ID(N'configuration') IS NULL EXEC(N'CREATE SCHEMA [configuration]');
IF SCHEMA_ID(N'masters') IS NULL EXEC(N'CREATE SCHEMA [masters]');
IF SCHEMA_ID(N'transactions') IS NULL EXEC(N'CREATE SCHEMA [transactions]');
IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA [audit]');
GO
-- security.module_master
IF OBJECT_ID(N'[security].[module_master]', N'U') IS NULL
CREATE TABLE [security].[module_master] (
    [module_id] int IDENTITY(1,1) NOT NULL,
    [module_code] varchar(50) NOT NULL,
    [module_name] nvarchar(100) NOT NULL,
    [parent_module_id] int NULL,
    [menu_group] varchar(20) NOT NULL,
    [route] nvarchar(200) NULL,
    [icon] nvarchar(50) NULL,
    [sort_order] int NOT NULL CONSTRAINT [DF_module_master_sort_order] DEFAULT ((0)),
    [is_menu_visible] bit NOT NULL CONSTRAINT [DF_module_master_is_menu_visible] DEFAULT ((1)),
    [is_active] bit NOT NULL CONSTRAINT [DF_module_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_module_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_module_master] PRIMARY KEY CLUSTERED ([module_id]),
    CONSTRAINT [UQ_module_master_module_code] UNIQUE NONCLUSTERED ([module_code]),
    CONSTRAINT [CK_module_master_menu_group] CHECK ([menu_group]='Administration' OR [menu_group]='Reports' OR [menu_group]='Transactions' OR [menu_group]='Masters' OR [menu_group]='Dashboard')
);
GO
-- security.permission_master
IF OBJECT_ID(N'[security].[permission_master]', N'U') IS NULL
CREATE TABLE [security].[permission_master] (
    [permission_id] int IDENTITY(1,1) NOT NULL,
    [role_id] int NOT NULL,
    [module_id] int NOT NULL,
    [can_view] bit NOT NULL CONSTRAINT [DF_permission_master_can_view] DEFAULT ((0)),
    [can_add] bit NOT NULL CONSTRAINT [DF_permission_master_can_add] DEFAULT ((0)),
    [can_edit] bit NOT NULL CONSTRAINT [DF_permission_master_can_edit] DEFAULT ((0)),
    [can_delete] bit NOT NULL CONSTRAINT [DF_permission_master_can_delete] DEFAULT ((0)),
    [can_approve] bit NOT NULL CONSTRAINT [DF_permission_master_can_approve] DEFAULT ((0)),
    [can_export] bit NOT NULL CONSTRAINT [DF_permission_master_can_export] DEFAULT ((0)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_permission_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_permission_master] PRIMARY KEY CLUSTERED ([permission_id]),
    CONSTRAINT [UQ_permission_master_role_module] UNIQUE NONCLUSTERED ([role_id], [module_id])
);
GO
-- security.role_master
IF OBJECT_ID(N'[security].[role_master]', N'U') IS NULL
CREATE TABLE [security].[role_master] (
    [role_id] int IDENTITY(1,1) NOT NULL,
    [role_code] varchar(30) NOT NULL,
    [role_name] nvarchar(100) NOT NULL,
    [description] nvarchar(200) NULL,
    [is_system_role] bit NOT NULL CONSTRAINT [DF_role_master_is_system_role] DEFAULT ((0)),
    [is_active] bit NOT NULL CONSTRAINT [DF_role_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_role_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_role_master] PRIMARY KEY CLUSTERED ([role_id]),
    CONSTRAINT [UQ_role_master_role_code] UNIQUE NONCLUSTERED ([role_code])
);
GO
-- security.user_master
IF OBJECT_ID(N'[security].[user_master]', N'U') IS NULL
CREATE TABLE [security].[user_master] (
    [user_id] int IDENTITY(1,1) NOT NULL,
    [user_code] varchar(20) NOT NULL,
    [login_id] varchar(50) NOT NULL,
    [user_name] nvarchar(100) NOT NULL,
    [password_hash] varchar(255) NOT NULL,
    [role_id] int NOT NULL,
    [employee_id] int NULL,
    [department_id] int NULL,
    [email] varchar(150) NULL,
    [mobile] varchar(15) NULL,
    [must_change_password] bit NOT NULL CONSTRAINT [DF_user_master_must_change_password] DEFAULT ((1)),
    [failed_login_count] int NOT NULL CONSTRAINT [DF_user_master_failed_login_count] DEFAULT ((0)),
    [lockout_end_at] datetime2(0) NULL,
    [last_login_at] datetime2(0) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_user_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_user_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_user_master] PRIMARY KEY CLUSTERED ([user_id]),
    CONSTRAINT [UQ_user_master_user_code] UNIQUE NONCLUSTERED ([user_code]),
    CONSTRAINT [UQ_user_master_login_id] UNIQUE NONCLUSTERED ([login_id])
);
GO
-- security.user_refresh_token
IF OBJECT_ID(N'[security].[user_refresh_token]', N'U') IS NULL
CREATE TABLE [security].[user_refresh_token] (
    [refresh_token_id] bigint IDENTITY(1,1) NOT NULL,
    [user_id] int NOT NULL,
    [token_hash] varchar(128) NOT NULL,
    [expires_at] datetime2(0) NOT NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_user_refresh_token_created_at] DEFAULT (sysutcdatetime()),
    [revoked_at] datetime2(0) NULL,
    [replaced_by_token_hash] varchar(128) NULL,
    [created_by_ip] varchar(45) NULL,
    [revoked_by_ip] varchar(45) NULL,
    CONSTRAINT [PK_user_refresh_token] PRIMARY KEY CLUSTERED ([refresh_token_id]),
    CONSTRAINT [UQ_user_refresh_token_token_hash] UNIQUE NONCLUSTERED ([token_hash])
);
GO
-- configuration.document_sequence_configuration
IF OBJECT_ID(N'[configuration].[document_sequence_configuration]', N'U') IS NULL
CREATE TABLE [configuration].[document_sequence_configuration] (
    [document_sequence_id] int IDENTITY(1,1) NOT NULL,
    [document_type] varchar(30) NOT NULL,
    [prefix] varchar(10) NOT NULL,
    [pad_length] tinyint NOT NULL CONSTRAINT [DF_document_sequence_configuration_pad_length] DEFAULT ((4)),
    [last_number] int NOT NULL CONSTRAINT [DF_document_sequence_configuration_last_number] DEFAULT ((0)),
    [reset_policy] varchar(15) NOT NULL CONSTRAINT [DF_document_sequence_configuration_reset_policy] DEFAULT ('None'),
    [period_key] varchar(10) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_document_sequence_configuration_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_document_sequence_configuration_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_document_sequence_configuration] PRIMARY KEY CLUSTERED ([document_sequence_id]),
    CONSTRAINT [UQ_document_sequence_configuration_document_type] UNIQUE NONCLUSTERED ([document_type]),
    CONSTRAINT [CK_document_sequence_configuration_reset_policy] CHECK ([reset_policy]='FinancialYear' OR [reset_policy]='Yearly' OR [reset_policy]='None'),
    CONSTRAINT [CK_document_sequence_configuration_pad_length] CHECK ([pad_length]>(0))
);
GO
-- configuration.system_settings_configuration
IF OBJECT_ID(N'[configuration].[system_settings_configuration]', N'U') IS NULL
CREATE TABLE [configuration].[system_settings_configuration] (
    [system_setting_id] int IDENTITY(1,1) NOT NULL,
    [setting_key] varchar(100) NOT NULL,
    [setting_value] nvarchar(500) NULL,
    [description] nvarchar(200) NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_system_settings_configuration_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    CONSTRAINT [PK_system_settings_configuration] PRIMARY KEY CLUSTERED ([system_setting_id]),
    CONSTRAINT [UQ_system_settings_configuration_setting_key] UNIQUE NONCLUSTERED ([setting_key])
);
GO
-- configuration.user_settings_configuration
IF OBJECT_ID(N'[configuration].[user_settings_configuration]', N'U') IS NULL
CREATE TABLE [configuration].[user_settings_configuration] (
    [user_setting_id] int IDENTITY(1,1) NOT NULL,
    [user_id] int NOT NULL,
    [language] varchar(20) NOT NULL CONSTRAINT [DF_user_settings_configuration_language] DEFAULT ('English'),
    [date_format] varchar(20) NOT NULL CONSTRAINT [DF_user_settings_configuration_date_format] DEFAULT ('DD-MMM-YYYY'),
    [theme] varchar(10) NOT NULL CONSTRAINT [DF_user_settings_configuration_theme] DEFAULT ('Light'),
    [notifications_enabled] bit NOT NULL CONSTRAINT [DF_user_settings_configuration_notifications_enabled] DEFAULT ((1)),
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    CONSTRAINT [PK_user_settings_configuration] PRIMARY KEY CLUSTERED ([user_setting_id]),
    CONSTRAINT [UQ_user_settings_configuration_user] UNIQUE NONCLUSTERED ([user_id]),
    CONSTRAINT [CK_user_settings_configuration_language] CHECK ([language]='Tamil' OR [language]='English'),
    CONSTRAINT [CK_user_settings_configuration_theme] CHECK ([theme]='Dark' OR [theme]='Light')
);
GO
-- masters.breakdown_type_master
IF OBJECT_ID(N'[masters].[breakdown_type_master]', N'U') IS NULL
CREATE TABLE [masters].[breakdown_type_master] (
    [breakdown_type_id] int IDENTITY(1,1) NOT NULL,
    [breakdown_type_code] varchar(20) NOT NULL,
    [breakdown_type_name] nvarchar(100) NOT NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_breakdown_type_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_breakdown_type_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_breakdown_type_master] PRIMARY KEY CLUSTERED ([breakdown_type_id]),
    CONSTRAINT [UQ_breakdown_type_master_code] UNIQUE NONCLUSTERED ([breakdown_type_code])
);
GO
-- masters.department_master
IF OBJECT_ID(N'[masters].[department_master]', N'U') IS NULL
CREATE TABLE [masters].[department_master] (
    [department_id] int IDENTITY(1,1) NOT NULL,
    [department_code] varchar(20) NOT NULL,
    [department_name] nvarchar(100) NOT NULL,
    [remarks] nvarchar(500) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_department_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_department_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_department_master] PRIMARY KEY CLUSTERED ([department_id]),
    CONSTRAINT [UQ_department_master_department_code] UNIQUE NONCLUSTERED ([department_code])
);
GO
-- masters.employee_master
IF OBJECT_ID(N'[masters].[employee_master]', N'U') IS NULL
CREATE TABLE [masters].[employee_master] (
    [employee_id] int IDENTITY(1,1) NOT NULL,
    [employee_code] varchar(20) NOT NULL,
    [employee_name] nvarchar(100) NOT NULL,
    [designation] nvarchar(100) NOT NULL,
    [department_id] int NOT NULL,
    [mobile] varchar(15) NULL,
    [email] varchar(150) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_employee_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_employee_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_employee_master] PRIMARY KEY CLUSTERED ([employee_id]),
    CONSTRAINT [UQ_employee_master_employee_code] UNIQUE NONCLUSTERED ([employee_code])
);
GO
-- masters.machine_master
IF OBJECT_ID(N'[masters].[machine_master]', N'U') IS NULL
CREATE TABLE [masters].[machine_master] (
    [machine_id] int IDENTITY(1,1) NOT NULL,
    [machine_code] varchar(20) NOT NULL,
    [machine_name] nvarchar(150) NOT NULL,
    [machine_type] nvarchar(100) NOT NULL,
    [department_id] int NULL,
    [location] nvarchar(150) NOT NULL,
    [manufacturer] nvarchar(100) NULL,
    [model] nvarchar(100) NULL,
    [serial_number] nvarchar(100) NULL,
    [capacity] nvarchar(50) NULL,
    [installation_date] date NULL,
    [maintenance_frequency_days] int NOT NULL CONSTRAINT [DF_machine_master_maintenance_frequency_days] DEFAULT ((30)),
    [responsible_engineer_id] int NULL,
    [criticality] varchar(10) NOT NULL CONSTRAINT [DF_machine_master_criticality] DEFAULT ('Medium'),
    [operational_status] varchar(20) NOT NULL CONSTRAINT [DF_machine_master_operational_status] DEFAULT ('Running'),
    [last_maintenance_date] date NULL,
    [next_maintenance_date] date NULL,
    [remarks] nvarchar(500) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_machine_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_machine_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    [machine_range] nvarchar(100) NULL,
    [ownership] nvarchar(100) NULL,
    [purchase_date] date NULL,
    CONSTRAINT [PK_machine_master] PRIMARY KEY CLUSTERED ([machine_id]),
    CONSTRAINT [UQ_machine_master_machine_code] UNIQUE NONCLUSTERED ([machine_code]),
    CONSTRAINT [CK_machine_master_maintenance_frequency_days] CHECK ([maintenance_frequency_days]>(0)),
    CONSTRAINT [CK_machine_master_criticality] CHECK ([criticality]='High' OR [criticality]='Medium' OR [criticality]='Low'),
    CONSTRAINT [CK_machine_master_operational_status] CHECK ([operational_status]='Maintenance' OR [operational_status]='Breakdown' OR [operational_status]='Idle' OR [operational_status]='Running')
);
GO
-- masters.maintenance_checklist_item_master
IF OBJECT_ID(N'[masters].[maintenance_checklist_item_master]', N'U') IS NULL
CREATE TABLE [masters].[maintenance_checklist_item_master] (
    [checklist_item_id] int IDENTITY(1,1) NOT NULL,
    [checklist_id] int NOT NULL,
    [sort_order] int NOT NULL CONSTRAINT [DF_maintenance_checklist_item_master_sort_order] DEFAULT ((0)),
    [item_label] nvarchar(200) NOT NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_maintenance_checklist_item_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    CONSTRAINT [PK_maintenance_checklist_item_master] PRIMARY KEY CLUSTERED ([checklist_item_id])
);
GO
-- masters.maintenance_checklist_master
IF OBJECT_ID(N'[masters].[maintenance_checklist_master]', N'U') IS NULL
CREATE TABLE [masters].[maintenance_checklist_master] (
    [checklist_id] int IDENTITY(1,1) NOT NULL,
    [checklist_code] varchar(20) NOT NULL,
    [checklist_name] nvarchar(150) NOT NULL,
    [applies_to] varchar(10) NOT NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_maintenance_checklist_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_maintenance_checklist_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    [frequency] varchar(10) NULL,
    [machine_id] int NULL,
    [start_date] date NULL,
    [maintenance_type_id] int NULL,
    [source_checklist_id] int NULL,
    CONSTRAINT [PK_maintenance_checklist_master] PRIMARY KEY CLUSTERED ([checklist_id]),
    CONSTRAINT [UQ_maintenance_checklist_master_code] UNIQUE NONCLUSTERED ([checklist_code]),
    CONSTRAINT [CK_maintenance_checklist_master_frequency] CHECK ([frequency]='Yearly' OR [frequency]='Monthly' OR [frequency]='Weekly' OR [frequency]='Daily'),
    CONSTRAINT [CK_maintenance_checklist_master_machine] CHECK ([applies_to]='Machine' OR [machine_id] IS NULL),
    CONSTRAINT [CK_maintenance_checklist_master_applies_to] CHECK ([applies_to]='Mold' OR [applies_to]='Machine'),
    CONSTRAINT [CK_maintenance_checklist_master_active_config] CHECK ([frequency] IS NULL AND [machine_id] IS NULL AND [start_date] IS NULL AND [maintenance_type_id] IS NULL AND [source_checklist_id] IS NULL OR [frequency] IS NOT NULL AND ([is_active]=(0) OR [applies_to]='Mold' OR [machine_id] IS NOT NULL)),
    CONSTRAINT [CK_maintenance_checklist_master_start_date] CHECK ([is_active]=(0) OR [applies_to]='Mold' OR [frequency] IS NULL OR [start_date] IS NOT NULL),
    CONSTRAINT [CK_maintenance_checklist_master_source] CHECK ([source_checklist_id] IS NULL OR [applies_to]='Machine' AND [source_checklist_id]<>[checklist_id])
);
GO
-- masters.maintenance_type_master
IF OBJECT_ID(N'[masters].[maintenance_type_master]', N'U') IS NULL
CREATE TABLE [masters].[maintenance_type_master] (
    [maintenance_type_id] int IDENTITY(1,1) NOT NULL,
    [maintenance_type_code] varchar(20) NOT NULL,
    [maintenance_type_name] nvarchar(100) NOT NULL,
    [applies_to] varchar(10) NOT NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_maintenance_type_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_maintenance_type_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_maintenance_type_master] PRIMARY KEY CLUSTERED ([maintenance_type_id]),
    CONSTRAINT [UQ_maintenance_type_master_code] UNIQUE NONCLUSTERED ([maintenance_type_code]),
    CONSTRAINT [CK_maintenance_type_master_applies_to] CHECK ([applies_to]='Both' OR [applies_to]='Mold' OR [applies_to]='Machine')
);
GO
-- masters.mold_master
IF OBJECT_ID(N'[masters].[mold_master]', N'U') IS NULL
CREATE TABLE [masters].[mold_master] (
    [mold_id] int IDENTITY(1,1) NOT NULL,
    [mold_code] varchar(20) NOT NULL,
    [mold_name] nvarchar(150) NOT NULL,
    [product_id] int NOT NULL,
    [mold_type] nvarchar(100) NOT NULL,
    [cavity_count] int NOT NULL CONSTRAINT [DF_mold_master_cavity_count] DEFAULT ((1)),
    [manufacturer] nvarchar(100) NULL,
    [serial_number] nvarchar(100) NULL,
    [location] nvarchar(100) NULL,
    [storage_location] nvarchar(100) NULL,
    [commission_date] date NULL,
    [maximum_shots] int NOT NULL CONSTRAINT [DF_mold_master_maximum_shots] DEFAULT ((500000)),
    [warning_shots] int NOT NULL CONSTRAINT [DF_mold_master_warning_shots] DEFAULT ((450000)),
    [replacement_shots] int NOT NULL CONSTRAINT [DF_mold_master_replacement_shots] DEFAULT ((500000)),
    [maintenance_frequency_shots] int NULL CONSTRAINT [DF_mold_master_maintenance_frequency_shots] DEFAULT ((50000)),
    [current_usage_shots] int NOT NULL CONSTRAINT [DF_mold_master_current_usage_shots] DEFAULT ((0)),
    [responsible_employee_id] int NULL,
    [status] varchar(20) NOT NULL CONSTRAINT [DF_mold_master_status] DEFAULT ('Available'),
    [life_state] AS (case when [current_usage_shots]>=[replacement_shots] then 'Replace' when [current_usage_shots]>=[warning_shots] then 'Warning' else 'Normal' end) PERSISTED,
    [remarks] nvarchar(500) NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_mold_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    [pm_cycle_start_shots] int NOT NULL CONSTRAINT [DF_mold_master_pm_cycle_start_shots] DEFAULT ((0)),
    [pm_warning_shots] int NULL,
    [part_no] nvarchar(100) NULL,
    [part_description] nvarchar(250) NULL,
    [ownership] nvarchar(100) NULL,
    CONSTRAINT [PK_mold_master] PRIMARY KEY CLUSTERED ([mold_id]),
    CONSTRAINT [UQ_mold_master_mold_code] UNIQUE NONCLUSTERED ([mold_code]),
    CONSTRAINT [CK_mold_master_pm_cycle_start_shots] CHECK ([pm_cycle_start_shots]>=(0)),
    CONSTRAINT [CK_mold_master_maintenance_frequency_shots] CHECK ([maintenance_frequency_shots] IS NULL OR [maintenance_frequency_shots]>(0)),
    CONSTRAINT [CK_mold_master_pm_warning_shots] CHECK ([pm_warning_shots] IS NULL OR [pm_warning_shots]>(0) AND [maintenance_frequency_shots] IS NOT NULL AND [pm_warning_shots]<[maintenance_frequency_shots]),
    CONSTRAINT [CK_mold_master_cavity_count] CHECK ([cavity_count]>=(1)),
    CONSTRAINT [CK_mold_master_maximum_shots] CHECK ([maximum_shots]>(0)),
    CONSTRAINT [CK_mold_master_warning_shots] CHECK ([warning_shots]<[maximum_shots]),
    CONSTRAINT [CK_mold_master_replacement_shots] CHECK ([replacement_shots]>[warning_shots] AND [replacement_shots]<=[maximum_shots]),
    CONSTRAINT [CK_mold_master_current_usage_shots] CHECK ([current_usage_shots]>=(0)),
    CONSTRAINT [CK_mold_master_status] CHECK ([status]='Retired' OR [status]='Replacement Due' OR [status]='Maintenance' OR [status]='In Production' OR [status]='Available')
);
GO
-- masters.product_master
IF OBJECT_ID(N'[masters].[product_master]', N'U') IS NULL
CREATE TABLE [masters].[product_master] (
    [product_id] int IDENTITY(1,1) NOT NULL,
    [product_code] varchar(20) NOT NULL,
    [product_name] nvarchar(150) NOT NULL,
    [category] nvarchar(100) NULL,
    [unit_of_measure] nvarchar(20) NOT NULL,
    [standard_cycle_time_sec] decimal(8,2) NULL,
    [remarks] nvarchar(500) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_product_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_product_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_product_master] PRIMARY KEY CLUSTERED ([product_id]),
    CONSTRAINT [UQ_product_master_product_code] UNIQUE NONCLUSTERED ([product_code]),
    CONSTRAINT [CK_product_master_standard_cycle_time_sec] CHECK ([standard_cycle_time_sec] IS NULL OR [standard_cycle_time_sec]>(0))
);
GO
-- masters.spare_part_master
IF OBJECT_ID(N'[masters].[spare_part_master]', N'U') IS NULL
CREATE TABLE [masters].[spare_part_master] (
    [spare_part_id] int IDENTITY(1,1) NOT NULL,
    [spare_part_code] varchar(20) NOT NULL,
    [spare_part_name] nvarchar(150) NOT NULL,
    [category] nvarchar(100) NULL,
    [machine_id] int NULL,
    [part_number] nvarchar(50) NULL,
    [unit] nvarchar(20) NOT NULL,
    [minimum_stock] int NOT NULL CONSTRAINT [DF_spare_part_master_minimum_stock] DEFAULT ((0)),
    [current_stock] int NOT NULL CONSTRAINT [DF_spare_part_master_current_stock] DEFAULT ((0)),
    [vendor_id] int NULL,
    [store_location] nvarchar(100) NULL,
    [unit_cost] decimal(18,2) NULL,
    [stock_status] AS (case when [current_stock]<=(0) then 'Out of Stock' when [current_stock]<=[minimum_stock] then 'Low Stock' else 'Available' end) PERSISTED,
    [is_active] bit NOT NULL CONSTRAINT [DF_spare_part_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_spare_part_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_spare_part_master] PRIMARY KEY CLUSTERED ([spare_part_id]),
    CONSTRAINT [UQ_spare_part_master_spare_part_code] UNIQUE NONCLUSTERED ([spare_part_code]),
    CONSTRAINT [CK_spare_part_master_minimum_stock] CHECK ([minimum_stock]>=(0)),
    CONSTRAINT [CK_spare_part_master_current_stock] CHECK ([current_stock]>=(0)),
    CONSTRAINT [CK_spare_part_master_unit_cost] CHECK ([unit_cost] IS NULL OR [unit_cost]>=(0))
);
GO
-- masters.vendor_master
IF OBJECT_ID(N'[masters].[vendor_master]', N'U') IS NULL
CREATE TABLE [masters].[vendor_master] (
    [vendor_id] int IDENTITY(1,1) NOT NULL,
    [vendor_code] varchar(20) NOT NULL,
    [vendor_name] nvarchar(150) NOT NULL,
    [category] nvarchar(100) NULL,
    [contact_person] nvarchar(100) NULL,
    [mobile] varchar(15) NULL,
    [email] varchar(150) NULL,
    [address] nvarchar(500) NULL,
    [is_active] bit NOT NULL CONSTRAINT [DF_vendor_master_is_active] DEFAULT ((1)),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_vendor_master_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_vendor_master] PRIMARY KEY CLUSTERED ([vendor_id]),
    CONSTRAINT [UQ_vendor_master_vendor_code] UNIQUE NONCLUSTERED ([vendor_code])
);
GO
-- transactions.breakdown_stage_transaction
IF OBJECT_ID(N'[transactions].[breakdown_stage_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[breakdown_stage_transaction] (
    [breakdown_stage_id] int IDENTITY(1,1) NOT NULL,
    [machine_breakdown_id] int NOT NULL,
    [from_stage] varchar(25) NULL,
    [to_stage] varchar(25) NOT NULL,
    [changed_at] datetime2(0) NOT NULL CONSTRAINT [DF_breakdown_stage_transaction_changed_at] DEFAULT (sysutcdatetime()),
    [changed_by] int NULL,
    [remarks] nvarchar(500) NULL,
    CONSTRAINT [PK_breakdown_stage_transaction] PRIMARY KEY CLUSTERED ([breakdown_stage_id])
);
GO
-- transactions.machine_breakdown_transaction
IF OBJECT_ID(N'[transactions].[machine_breakdown_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[machine_breakdown_transaction] (
    [machine_breakdown_id] int IDENTITY(1,1) NOT NULL,
    [breakdown_no] varchar(20) NOT NULL,
    [machine_id] int NOT NULL,
    [breakdown_date] date NOT NULL,
    [breakdown_time] time(0) NOT NULL,
    [reported_by_employee_id] int NULL,
    [problem] nvarchar(500) NOT NULL,
    [breakdown_type_id] int NULL,
    [priority] varchar(10) NOT NULL CONSTRAINT [DF_machine_breakdown_transaction_priority] DEFAULT ('Medium'),
    [description] nvarchar(1000) NULL,
    [stage] varchar(25) NOT NULL CONSTRAINT [DF_machine_breakdown_transaction_stage] DEFAULT ('Reported'),
    [assigned_engineer_id] int NULL,
    [assigned_at] datetime2(0) NULL,
    [maintenance_started_at] datetime2(0) NULL,
    [resolved_at] datetime2(0) NULL,
    [closed_at] datetime2(0) NULL,
    [root_cause] nvarchar(1000) NULL,
    [corrective_action] nvarchar(1000) NULL,
    [downtime_hours] decimal(8,1) NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_machine_breakdown_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    [reported_by] nvarchar(100) NULL,
    [assigned_to_name] nvarchar(100) NULL,
    CONSTRAINT [PK_machine_breakdown_transaction] PRIMARY KEY CLUSTERED ([machine_breakdown_id]),
    CONSTRAINT [UQ_machine_breakdown_transaction_breakdown_no] UNIQUE NONCLUSTERED ([breakdown_no]),
    CONSTRAINT [CK_machine_breakdown_transaction_priority] CHECK ([priority]='Critical' OR [priority]='High' OR [priority]='Medium' OR [priority]='Low'),
    CONSTRAINT [CK_machine_breakdown_transaction_stage] CHECK ([stage]='Closed' OR [stage]='Resolved' OR [stage]='Maintenance Started' OR [stage]='Assigned' OR [stage]='Reported'),
    CONSTRAINT [CK_machine_breakdown_transaction_downtime_hours] CHECK ([downtime_hours] IS NULL OR [downtime_hours]>=(0)),
    CONSTRAINT [CK_machine_breakdown_transaction_assignee] CHECK ([assigned_engineer_id] IS NULL OR [assigned_to_name] IS NULL)
);
GO
-- transactions.machine_pm_checklist_transaction
IF OBJECT_ID(N'[transactions].[machine_pm_checklist_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[machine_pm_checklist_transaction] (
    [machine_pm_checklist_id] int IDENTITY(1,1) NOT NULL,
    [machine_pm_id] int NOT NULL,
    [sort_order] int NOT NULL CONSTRAINT [DF_machine_pm_checklist_transaction_sort_order] DEFAULT ((0)),
    [item_label] nvarchar(200) NOT NULL,
    [checklist_item_id] int NULL,
    [is_checked] bit NOT NULL CONSTRAINT [DF_machine_pm_checklist_transaction_is_checked] DEFAULT ((0)),
    CONSTRAINT [PK_machine_pm_checklist_transaction] PRIMARY KEY CLUSTERED ([machine_pm_checklist_id])
);
GO
-- transactions.machine_pm_transaction
IF OBJECT_ID(N'[transactions].[machine_pm_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[machine_pm_transaction] (
    [machine_pm_id] int IDENTITY(1,1) NOT NULL,
    [pm_no] varchar(20) NOT NULL,
    [machine_id] int NOT NULL,
    [maintenance_type_id] int NULL,
    [scheduled_date] date NOT NULL,
    [completed_date] date NULL,
    [engineer_id] int NULL,
    [checklist_id] int NULL,
    [remarks] nvarchar(1000) NULL,
    [status] varchar(15) NOT NULL CONSTRAINT [DF_machine_pm_transaction_status] DEFAULT ('Scheduled'),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_machine_pm_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    [maintenance_by] nvarchar(100) NULL,
    [schedule_type] varchar(10) NOT NULL CONSTRAINT [DF_machine_pm_transaction_schedule_type] DEFAULT ('Automatic'),
    [title] nvarchar(150) NULL,
    CONSTRAINT [PK_machine_pm_transaction] PRIMARY KEY CLUSTERED ([machine_pm_id]),
    CONSTRAINT [UQ_machine_pm_transaction_pm_no] UNIQUE NONCLUSTERED ([pm_no]),
    CONSTRAINT [CK_machine_pm_transaction_status] CHECK ([status]='Completed' OR [status]='In Progress' OR [status]='Scheduled'),
    CONSTRAINT [CK_machine_pm_transaction_schedule_type] CHECK ([schedule_type]='Automatic' OR [schedule_type]='Manual' AND [title] IS NOT NULL AND [checklist_id] IS NULL)
);
GO
-- transactions.maintenance_work_order_transaction
IF OBJECT_ID(N'[transactions].[maintenance_work_order_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[maintenance_work_order_transaction] (
    [work_order_id] int IDENTITY(1,1) NOT NULL,
    [work_order_no] varchar(20) NOT NULL,
    [source] varchar(25) NOT NULL CONSTRAINT [DF_maintenance_work_order_transaction_source] DEFAULT ('Manual Request'),
    [asset_type] varchar(10) NOT NULL,
    [machine_id] int NULL,
    [mold_id] int NULL,
    [machine_breakdown_id] int NULL,
    [machine_pm_id] int NULL,
    [mold_pm_id] int NULL,
    [problem] nvarchar(500) NOT NULL,
    [priority] varchar(10) NOT NULL CONSTRAINT [DF_maintenance_work_order_transaction_priority] DEFAULT ('Medium'),
    [assigned_engineer_id] int NULL,
    [start_date] date NULL,
    [expected_completion_date] date NULL,
    [action_taken] nvarchar(1000) NULL,
    [status] varchar(15) NOT NULL CONSTRAINT [DF_maintenance_work_order_transaction_status] DEFAULT ('Open'),
    [completed_at] datetime2(0) NULL,
    [closed_at] datetime2(0) NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_maintenance_work_order_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_maintenance_work_order_transaction] PRIMARY KEY CLUSTERED ([work_order_id]),
    CONSTRAINT [UQ_work_order_transaction_work_order_no] UNIQUE NONCLUSTERED ([work_order_no]),
    CONSTRAINT [CK_work_order_transaction_asset_type] CHECK ([asset_type]='Mold' OR [asset_type]='Machine'),
    CONSTRAINT [CK_work_order_transaction_asset_fk] CHECK ([asset_type]='Machine' AND [machine_id] IS NOT NULL AND [mold_id] IS NULL OR [asset_type]='Mold' AND [mold_id] IS NOT NULL AND [machine_id] IS NULL),
    CONSTRAINT [CK_work_order_transaction_source] CHECK ([source]='Manual Request' OR [source]='Preventive Maintenance' OR [source]='Mold Breakdown' OR [source]='Machine Breakdown'),
    CONSTRAINT [CK_work_order_transaction_priority] CHECK ([priority]='Critical' OR [priority]='High' OR [priority]='Medium' OR [priority]='Low'),
    CONSTRAINT [CK_work_order_transaction_status] CHECK ([status]='Closed' OR [status]='Completed' OR [status]='In Progress' OR [status]='Open'),
    CONSTRAINT [CK_work_order_transaction_expected_completion] CHECK ([expected_completion_date] IS NULL OR [start_date] IS NULL OR [expected_completion_date]>=[start_date])
);
GO
-- transactions.mold_pm_checklist_transaction
IF OBJECT_ID(N'[transactions].[mold_pm_checklist_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[mold_pm_checklist_transaction] (
    [mold_pm_checklist_id] int IDENTITY(1,1) NOT NULL,
    [mold_pm_id] int NOT NULL,
    [sort_order] int NOT NULL CONSTRAINT [DF_mold_pm_checklist_transaction_sort_order] DEFAULT ((0)),
    [item_label] nvarchar(200) NOT NULL,
    [checklist_item_id] int NULL,
    [is_checked] bit NOT NULL CONSTRAINT [DF_mold_pm_checklist_transaction_is_checked] DEFAULT ((0)),
    CONSTRAINT [PK_mold_pm_checklist_transaction] PRIMARY KEY CLUSTERED ([mold_pm_checklist_id])
);
GO
-- transactions.mold_pm_transaction
IF OBJECT_ID(N'[transactions].[mold_pm_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[mold_pm_transaction] (
    [mold_pm_id] int IDENTITY(1,1) NOT NULL,
    [pm_no] varchar(20) NOT NULL,
    [mold_id] int NOT NULL,
    [category] varchar(20) NOT NULL CONSTRAINT [DF_mold_pm_transaction_category] DEFAULT ('Preventive'),
    [scheduled_date] date NOT NULL,
    [completed_date] date NULL,
    [engineer_id] int NULL,
    [checklist_id] int NULL,
    [mold_usage_at_service] int NOT NULL,
    [usage_reset] bit NOT NULL CONSTRAINT [DF_mold_pm_transaction_usage_reset] DEFAULT ((0)),
    [usage_before_reset] int NULL,
    [remarks] nvarchar(1000) NULL,
    [status] varchar(15) NOT NULL CONSTRAINT [DF_mold_pm_transaction_status] DEFAULT ('Scheduled'),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_mold_pm_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    [maintenance_by] nvarchar(100) NULL,
    [threshold_shots] int NULL,
    [interval_shots] int NULL,
    [usage_at_completion] int NULL,
    [schedule_type] varchar(10) NOT NULL CONSTRAINT [DF_mold_pm_transaction_schedule_type] DEFAULT ('Automatic'),
    [title] nvarchar(150) NULL,
    CONSTRAINT [PK_mold_pm_transaction] PRIMARY KEY CLUSTERED ([mold_pm_id]),
    CONSTRAINT [UQ_mold_pm_transaction_pm_no] UNIQUE NONCLUSTERED ([pm_no]),
    CONSTRAINT [CK_mold_pm_transaction_shot_based] CHECK ([category]<>'Shot-based' OR [threshold_shots] IS NOT NULL AND [threshold_shots]>(0) AND [interval_shots] IS NOT NULL AND [interval_shots]>(0)),
    CONSTRAINT [CK_mold_pm_transaction_usage_at_completion] CHECK ([usage_at_completion] IS NULL OR [usage_at_completion]>=(0)),
    CONSTRAINT [CK_mold_pm_transaction_category] CHECK ([category]='Replacement' OR [category]='Preventive' OR [category]='Inspection' OR [category]='Cleaning' OR [category]='Damage Repair' OR [category]='Shot-based' OR [category]='Scheduled'),
    CONSTRAINT [CK_mold_pm_transaction_status] CHECK ([status]='Completed' OR [status]='In Progress' OR [status]='Scheduled'),
    CONSTRAINT [CK_mold_pm_transaction_usage_reset] CHECK ([usage_reset]=(0) OR [category]='Replacement'),
    CONSTRAINT [CK_mold_pm_transaction_schedule_type] CHECK ([schedule_type]='Automatic' OR [schedule_type]='Manual' AND [title] IS NOT NULL AND [checklist_id] IS NULL AND [category]<>'Shot-based' AND [threshold_shots] IS NULL)
);
GO
-- transactions.notification_read_transaction
IF OBJECT_ID(N'[transactions].[notification_read_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[notification_read_transaction] (
    [user_id] int NOT NULL,
    [notification_id] int NOT NULL,
    [read_at] datetime2(0) NOT NULL CONSTRAINT [DF_notification_read_transaction_read_at] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_notification_read_transaction] PRIMARY KEY CLUSTERED ([user_id], [notification_id])
);
GO
-- transactions.notification_transaction
IF OBJECT_ID(N'[transactions].[notification_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[notification_transaction] (
    [notification_id] int IDENTITY(1,1) NOT NULL,
    [notification_type] varchar(30) NOT NULL,
    [module_code] varchar(50) NOT NULL,
    [severity] varchar(10) NOT NULL,
    [title] nvarchar(150) NOT NULL,
    [message] nvarchar(500) NOT NULL,
    [entity_name] varchar(50) NULL,
    [entity_id] int NULL,
    [record_ref] nvarchar(50) NULL,
    [link_path] varchar(200) NULL,
    [event_key] varchar(100) NOT NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_notification_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    CONSTRAINT [PK_notification_transaction] PRIMARY KEY CLUSTERED ([notification_id]),
    CONSTRAINT [UQ_notification_transaction_event_key] UNIQUE NONCLUSTERED ([event_key]),
    CONSTRAINT [CK_notification_transaction_severity] CHECK ([severity]='Critical' OR [severity]='Warning' OR [severity]='Info'),
    CONSTRAINT [CK_notification_transaction_type] CHECK ([notification_type]='MachinePmOverdue' OR [notification_type]='MachinePmDue' OR [notification_type]='MachinePmScheduled' OR [notification_type]='BreakdownReopened' OR [notification_type]='BreakdownClosed' OR [notification_type]='BreakdownResolved' OR [notification_type]='BreakdownStarted' OR [notification_type]='BreakdownAssigned' OR [notification_type]='BreakdownReported' OR [notification_type]='SparePartOutOfStock' OR [notification_type]='SparePartLowStock' OR [notification_type]='MoldPmDue' OR [notification_type]='MoldPmWarning')
);
GO
-- transactions.production_entry_transaction
IF OBJECT_ID(N'[transactions].[production_entry_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[production_entry_transaction] (
    [production_entry_id] int IDENTITY(1,1) NOT NULL,
    [entry_no] varchar(20) NOT NULL,
    [entry_date] date NOT NULL,
    [shift] varchar(10) NOT NULL,
    [machine_id] int NOT NULL,
    [product_id] int NOT NULL,
    [mold_id] int NOT NULL,
    [production_qty] int NOT NULL,
    [rejected_qty] int NOT NULL CONSTRAINT [DF_production_entry_transaction_rejected_qty] DEFAULT ((0)),
    [good_qty] AS ([production_qty]-[rejected_qty]) PERSISTED,
    [mold_usage_before] int NOT NULL,
    [mold_usage_after] int NOT NULL,
    [remarks] nvarchar(500) NULL,
    [status] varchar(10) NOT NULL CONSTRAINT [DF_production_entry_transaction_status] DEFAULT ('Saved'),
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_production_entry_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_production_entry_transaction] PRIMARY KEY CLUSTERED ([production_entry_id]),
    CONSTRAINT [UQ_production_entry_transaction_entry_no] UNIQUE NONCLUSTERED ([entry_no]),
    CONSTRAINT [CK_production_entry_transaction_shift] CHECK ([shift]='Shift C' OR [shift]='Shift B' OR [shift]='Shift A'),
    CONSTRAINT [CK_production_entry_transaction_production_qty] CHECK ([production_qty]>(0)),
    CONSTRAINT [CK_production_entry_transaction_rejected_qty] CHECK ([rejected_qty]>=(0) AND [rejected_qty]<=[production_qty]),
    CONSTRAINT [CK_production_entry_transaction_status] CHECK ([status]='Cancelled' OR [status]='Saved')
);
GO
-- transactions.spare_part_stock_transaction
IF OBJECT_ID(N'[transactions].[spare_part_stock_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[spare_part_stock_transaction] (
    [stock_transaction_id] bigint IDENTITY(1,1) NOT NULL,
    [spare_part_id] int NOT NULL,
    [transaction_type] varchar(15) NOT NULL,
    [quantity] int NOT NULL,
    [previous_stock] int NOT NULL,
    [new_stock] int NOT NULL,
    [reference_type] varchar(30) NULL,
    [reference_id] int NULL,
    [reference_no] nvarchar(50) NULL,
    [transaction_at] datetime2(0) NOT NULL CONSTRAINT [DF_spare_part_stock_transaction_transaction_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [remarks] nvarchar(500) NULL,
    CONSTRAINT [PK_spare_part_stock_transaction] PRIMARY KEY CLUSTERED ([stock_transaction_id]),
    CONSTRAINT [CK_spare_part_stock_transaction_balance] CHECK ([new_stock]=([previous_stock]+[quantity])),
    CONSTRAINT [CK_spare_part_stock_transaction_direction] CHECK (([transaction_type]<>'Issue' OR [quantity]<(0)) AND ([transaction_type]<>'Reversal' OR [quantity]>(0))),
    CONSTRAINT [CK_spare_part_stock_transaction_new_stock] CHECK ([new_stock]>=(0)),
    CONSTRAINT [CK_spare_part_stock_transaction_quantity] CHECK ([quantity]<>(0) OR [transaction_type]='Opening'),
    CONSTRAINT [CK_spare_part_stock_transaction_type] CHECK ([transaction_type]='Adjustment' OR [transaction_type]='Reversal' OR [transaction_type]='Issue' OR [transaction_type]='Opening')
);
GO
-- transactions.spare_part_usage_transaction
IF OBJECT_ID(N'[transactions].[spare_part_usage_transaction]', N'U') IS NULL
CREATE TABLE [transactions].[spare_part_usage_transaction] (
    [spare_part_usage_id] int IDENTITY(1,1) NOT NULL,
    [usage_no] varchar(20) NOT NULL,
    [spare_part_id] int NOT NULL,
    [quantity] int NOT NULL,
    [usage_date] date NOT NULL,
    [used_for] varchar(25) NOT NULL,
    [reference_no] nvarchar(50) NULL,
    [machine_id] int NULL,
    [mold_id] int NULL,
    [machine_breakdown_id] int NULL,
    [machine_pm_id] int NULL,
    [mold_pm_id] int NULL,
    [work_order_id] int NULL,
    [used_by_employee_id] int NULL,
    [unit_cost_at_issue] decimal(18,2) NULL,
    [remarks] nvarchar(500) NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_spare_part_usage_transaction_created_at] DEFAULT (sysutcdatetime()),
    [created_by] int NULL,
    [updated_at] datetime2(0) NULL,
    [updated_by] int NULL,
    [status] varchar(10) NOT NULL CONSTRAINT [DF_spare_part_usage_transaction_status] DEFAULT ('Posted'),
    [reversed_at] datetime2(0) NULL,
    [reversed_by] int NULL,
    [reversal_reason] nvarchar(500) NULL,
    [request_id] uniqueidentifier NULL,
    [row_version] timestamp NOT NULL,
    CONSTRAINT [PK_spare_part_usage_transaction] PRIMARY KEY CLUSTERED ([spare_part_usage_id]),
    CONSTRAINT [UQ_spare_part_usage_transaction_usage_no] UNIQUE NONCLUSTERED ([usage_no]),
    CONSTRAINT [CK_spare_part_usage_transaction_status] CHECK ([status]='Reversed' OR [status]='Posted'),
    CONSTRAINT [CK_spare_part_usage_transaction_reversal] CHECK ([status]='Posted' AND [reversed_at] IS NULL OR [status]='Reversed' AND [reversed_at] IS NOT NULL),
    CONSTRAINT [CK_spare_part_usage_transaction_maintenance_source] CHECK (([used_for]<>'Machine Maintenance' OR [machine_pm_id] IS NOT NULL AND [mold_pm_id] IS NULL) AND ([used_for]<>'Mold Maintenance' OR [mold_pm_id] IS NOT NULL AND [machine_pm_id] IS NULL)),
    CONSTRAINT [CK_spare_part_usage_transaction_quantity] CHECK ([quantity]>(0)),
    CONSTRAINT [CK_spare_part_usage_transaction_used_for] CHECK ([used_for]='Other' OR [used_for]='Work Order' OR [used_for]='Mold Maintenance' OR [used_for]='Machine Maintenance' OR [used_for]='Machine Breakdown')
);
GO
-- audit.application_log
IF OBJECT_ID(N'[audit].[application_log]', N'U') IS NULL
CREATE TABLE [audit].[application_log] (
    [application_log_id] bigint IDENTITY(1,1) NOT NULL,
    [log_level] varchar(15) NOT NULL,
    [log_date_time] datetime2(0) NOT NULL CONSTRAINT [DF_application_log_log_date_time] DEFAULT (sysutcdatetime()),
    [message] nvarchar(2000) NOT NULL,
    [exception] nvarchar(MAX) NULL,
    [source] nvarchar(200) NULL,
    [request_path] nvarchar(500) NULL,
    [http_method] varchar(10) NULL,
    [user_id] int NULL,
    [correlation_id] varchar(50) NULL,
    [machine_name] nvarchar(100) NULL,
    [created_at] datetime2(0) NOT NULL CONSTRAINT [DF_application_log_created_at] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_application_log] PRIMARY KEY CLUSTERED ([application_log_id]),
    CONSTRAINT [CK_application_log_log_level] CHECK ([log_level]='Critical' OR [log_level]='Error' OR [log_level]='Warning' OR [log_level]='Information')
);
GO
-- audit.audit_log
IF OBJECT_ID(N'[audit].[audit_log]', N'U') IS NULL
CREATE TABLE [audit].[audit_log] (
    [audit_log_id] bigint IDENTITY(1,1) NOT NULL,
    [event_at] datetime2(0) NOT NULL CONSTRAINT [DF_audit_log_event_at] DEFAULT (sysutcdatetime()),
    [user_id] int NULL,
    [user_name] nvarchar(100) NOT NULL,
    [module] nvarchar(50) NOT NULL,
    [action] varchar(30) NOT NULL,
    [entity_name] varchar(50) NULL,
    [entity_id] int NULL,
    [record_ref] nvarchar(50) NULL,
    [description] nvarchar(1000) NOT NULL,
    [ip_address] varchar(45) NULL,
    CONSTRAINT [PK_audit_log] PRIMARY KEY CLUSTERED ([audit_log_id])
);
GO
-- audit.audit_log_detail
IF OBJECT_ID(N'[audit].[audit_log_detail]', N'U') IS NULL
CREATE TABLE [audit].[audit_log_detail] (
    [audit_log_detail_id] bigint IDENTITY(1,1) NOT NULL,
    [audit_log_id] bigint NOT NULL,
    [field_name] varchar(100) NOT NULL,
    [old_value] nvarchar(1000) NULL,
    [new_value] nvarchar(1000) NULL,
    CONSTRAINT [PK_audit_log_detail] PRIMARY KEY CLUSTERED ([audit_log_detail_id])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_application_log_correlation_id' AND object_id=OBJECT_ID(N'[audit].[application_log]'))
CREATE NONCLUSTERED INDEX [IX_application_log_correlation_id] ON [audit].[application_log] ([correlation_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_application_log_log_date_time' AND object_id=OBJECT_ID(N'[audit].[application_log]'))
CREATE NONCLUSTERED INDEX [IX_application_log_log_date_time] ON [audit].[application_log] ([log_date_time] DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_audit_log_entity' AND object_id=OBJECT_ID(N'[audit].[audit_log]'))
CREATE NONCLUSTERED INDEX [IX_audit_log_entity] ON [audit].[audit_log] ([entity_name], [entity_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_audit_log_event_at' AND object_id=OBJECT_ID(N'[audit].[audit_log]'))
CREATE NONCLUSTERED INDEX [IX_audit_log_event_at] ON [audit].[audit_log] ([event_at] DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_audit_log_module_event_at' AND object_id=OBJECT_ID(N'[audit].[audit_log]'))
CREATE NONCLUSTERED INDEX [IX_audit_log_module_event_at] ON [audit].[audit_log] ([module], [event_at]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_audit_log_detail_audit_log' AND object_id=OBJECT_ID(N'[audit].[audit_log_detail]'))
CREATE NONCLUSTERED INDEX [IX_audit_log_detail_audit_log] ON [audit].[audit_log_detail] ([audit_log_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_employee_master_department' AND object_id=OBJECT_ID(N'[masters].[employee_master]'))
CREATE NONCLUSTERED INDEX [IX_employee_master_department] ON [masters].[employee_master] ([department_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_master_department' AND object_id=OBJECT_ID(N'[masters].[machine_master]'))
CREATE NONCLUSTERED INDEX [IX_machine_master_department] ON [masters].[machine_master] ([department_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_master_machine_name' AND object_id=OBJECT_ID(N'[masters].[machine_master]'))
CREATE NONCLUSTERED INDEX [IX_machine_master_machine_name] ON [masters].[machine_master] ([machine_name]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_master_next_maintenance_date' AND object_id=OBJECT_ID(N'[masters].[machine_master]'))
CREATE NONCLUSTERED INDEX [IX_machine_master_next_maintenance_date] ON [masters].[machine_master] ([next_maintenance_date]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_master_operational_status' AND object_id=OBJECT_ID(N'[masters].[machine_master]'))
CREATE NONCLUSTERED INDEX [IX_machine_master_operational_status] ON [masters].[machine_master] ([operational_status]) INCLUDE ([is_active]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_machine_master_serial_number' AND object_id=OBJECT_ID(N'[masters].[machine_master]'))
CREATE UNIQUE NONCLUSTERED INDEX [UX_machine_master_serial_number] ON [masters].[machine_master] ([serial_number]) WHERE ([serial_number] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_checklist_item_master_checklist' AND object_id=OBJECT_ID(N'[masters].[maintenance_checklist_item_master]'))
CREATE NONCLUSTERED INDEX [IX_checklist_item_master_checklist] ON [masters].[maintenance_checklist_item_master] ([checklist_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_maintenance_checklist_master_machine' AND object_id=OBJECT_ID(N'[masters].[maintenance_checklist_master]'))
CREATE NONCLUSTERED INDEX [IX_maintenance_checklist_master_machine] ON [masters].[maintenance_checklist_master] ([machine_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_maintenance_checklist_master_maintenance_type' AND object_id=OBJECT_ID(N'[masters].[maintenance_checklist_master]'))
CREATE NONCLUSTERED INDEX [IX_maintenance_checklist_master_maintenance_type] ON [masters].[maintenance_checklist_master] ([maintenance_type_id]) WHERE ([maintenance_type_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_maintenance_checklist_master_source' AND object_id=OBJECT_ID(N'[masters].[maintenance_checklist_master]'))
CREATE NONCLUSTERED INDEX [IX_maintenance_checklist_master_source] ON [masters].[maintenance_checklist_master] ([source_checklist_id]) WHERE ([source_checklist_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_master_life_state' AND object_id=OBJECT_ID(N'[masters].[mold_master]'))
CREATE NONCLUSTERED INDEX [IX_mold_master_life_state] ON [masters].[mold_master] ([life_state]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_master_mold_name' AND object_id=OBJECT_ID(N'[masters].[mold_master]'))
CREATE NONCLUSTERED INDEX [IX_mold_master_mold_name] ON [masters].[mold_master] ([mold_name]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_master_product' AND object_id=OBJECT_ID(N'[masters].[mold_master]'))
CREATE NONCLUSTERED INDEX [IX_mold_master_product] ON [masters].[mold_master] ([product_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_master_status' AND object_id=OBJECT_ID(N'[masters].[mold_master]'))
CREATE NONCLUSTERED INDEX [IX_mold_master_status] ON [masters].[mold_master] ([status]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_mold_master_serial_number' AND object_id=OBJECT_ID(N'[masters].[mold_master]'))
CREATE UNIQUE NONCLUSTERED INDEX [UX_mold_master_serial_number] ON [masters].[mold_master] ([serial_number]) WHERE ([serial_number] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_master_machine' AND object_id=OBJECT_ID(N'[masters].[spare_part_master]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_master_machine] ON [masters].[spare_part_master] ([machine_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_master_stock_status' AND object_id=OBJECT_ID(N'[masters].[spare_part_master]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_master_stock_status] ON [masters].[spare_part_master] ([stock_status]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_master_vendor' AND object_id=OBJECT_ID(N'[masters].[spare_part_master]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_master_vendor] ON [masters].[spare_part_master] ([vendor_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_module_master_parent_module' AND object_id=OBJECT_ID(N'[security].[module_master]'))
CREATE NONCLUSTERED INDEX [IX_module_master_parent_module] ON [security].[module_master] ([parent_module_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_permission_master_module' AND object_id=OBJECT_ID(N'[security].[permission_master]'))
CREATE NONCLUSTERED INDEX [IX_permission_master_module] ON [security].[permission_master] ([module_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_user_master_employee' AND object_id=OBJECT_ID(N'[security].[user_master]'))
CREATE NONCLUSTERED INDEX [IX_user_master_employee] ON [security].[user_master] ([employee_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_user_master_role' AND object_id=OBJECT_ID(N'[security].[user_master]'))
CREATE NONCLUSTERED INDEX [IX_user_master_role] ON [security].[user_master] ([role_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_user_refresh_token_expires_at' AND object_id=OBJECT_ID(N'[security].[user_refresh_token]'))
CREATE NONCLUSTERED INDEX [IX_user_refresh_token_expires_at] ON [security].[user_refresh_token] ([expires_at]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_user_refresh_token_user' AND object_id=OBJECT_ID(N'[security].[user_refresh_token]'))
CREATE NONCLUSTERED INDEX [IX_user_refresh_token_user] ON [security].[user_refresh_token] ([user_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_breakdown_stage_transaction_breakdown' AND object_id=OBJECT_ID(N'[transactions].[breakdown_stage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_breakdown_stage_transaction_breakdown] ON [transactions].[breakdown_stage_transaction] ([machine_breakdown_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_breakdown_transaction_breakdown_date' AND object_id=OBJECT_ID(N'[transactions].[machine_breakdown_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_breakdown_transaction_breakdown_date] ON [transactions].[machine_breakdown_transaction] ([breakdown_date]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_breakdown_transaction_breakdown_type' AND object_id=OBJECT_ID(N'[transactions].[machine_breakdown_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_breakdown_transaction_breakdown_type] ON [transactions].[machine_breakdown_transaction] ([breakdown_type_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_breakdown_transaction_machine_date' AND object_id=OBJECT_ID(N'[transactions].[machine_breakdown_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_breakdown_transaction_machine_date] ON [transactions].[machine_breakdown_transaction] ([machine_id], [breakdown_date]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_breakdown_transaction_stage' AND object_id=OBJECT_ID(N'[transactions].[machine_breakdown_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_breakdown_transaction_stage] ON [transactions].[machine_breakdown_transaction] ([stage]) INCLUDE ([machine_id], [priority]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_pm_checklist_transaction_pm' AND object_id=OBJECT_ID(N'[transactions].[machine_pm_checklist_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_pm_checklist_transaction_pm] ON [transactions].[machine_pm_checklist_transaction] ([machine_pm_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_pm_transaction_machine' AND object_id=OBJECT_ID(N'[transactions].[machine_pm_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_pm_transaction_machine] ON [transactions].[machine_pm_transaction] ([machine_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_machine_pm_transaction_status_scheduled_date' AND object_id=OBJECT_ID(N'[transactions].[machine_pm_transaction]'))
CREATE NONCLUSTERED INDEX [IX_machine_pm_transaction_status_scheduled_date] ON [transactions].[machine_pm_transaction] ([status], [scheduled_date]) INCLUDE ([machine_id], [engineer_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_machine_pm_transaction_open_occurrence' AND object_id=OBJECT_ID(N'[transactions].[machine_pm_transaction]'))
CREATE UNIQUE NONCLUSTERED INDEX [UX_machine_pm_transaction_open_occurrence] ON [transactions].[machine_pm_transaction] ([checklist_id]) WHERE ([checklist_id] IS NOT NULL AND ([status] IN ('Scheduled', 'In Progress')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_work_order_transaction_machine' AND object_id=OBJECT_ID(N'[transactions].[maintenance_work_order_transaction]'))
CREATE NONCLUSTERED INDEX [IX_work_order_transaction_machine] ON [transactions].[maintenance_work_order_transaction] ([machine_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_work_order_transaction_mold' AND object_id=OBJECT_ID(N'[transactions].[maintenance_work_order_transaction]'))
CREATE NONCLUSTERED INDEX [IX_work_order_transaction_mold] ON [transactions].[maintenance_work_order_transaction] ([mold_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_work_order_transaction_status' AND object_id=OBJECT_ID(N'[transactions].[maintenance_work_order_transaction]'))
CREATE NONCLUSTERED INDEX [IX_work_order_transaction_status] ON [transactions].[maintenance_work_order_transaction] ([status]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_pm_checklist_transaction_pm' AND object_id=OBJECT_ID(N'[transactions].[mold_pm_checklist_transaction]'))
CREATE NONCLUSTERED INDEX [IX_mold_pm_checklist_transaction_pm] ON [transactions].[mold_pm_checklist_transaction] ([mold_pm_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_pm_transaction_mold' AND object_id=OBJECT_ID(N'[transactions].[mold_pm_transaction]'))
CREATE NONCLUSTERED INDEX [IX_mold_pm_transaction_mold] ON [transactions].[mold_pm_transaction] ([mold_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_mold_pm_transaction_status_scheduled_date' AND object_id=OBJECT_ID(N'[transactions].[mold_pm_transaction]'))
CREATE NONCLUSTERED INDEX [IX_mold_pm_transaction_status_scheduled_date] ON [transactions].[mold_pm_transaction] ([status], [scheduled_date]) INCLUDE ([mold_id], [engineer_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_mold_pm_transaction_open_shot_based' AND object_id=OBJECT_ID(N'[transactions].[mold_pm_transaction]'))
CREATE UNIQUE NONCLUSTERED INDEX [UX_mold_pm_transaction_open_shot_based] ON [transactions].[mold_pm_transaction] ([mold_id]) WHERE ([category]='Shot-based' AND ([status] IN ('Scheduled', 'In Progress')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_mold_pm_transaction_shot_based_threshold' AND object_id=OBJECT_ID(N'[transactions].[mold_pm_transaction]'))
CREATE UNIQUE NONCLUSTERED INDEX [UX_mold_pm_transaction_shot_based_threshold] ON [transactions].[mold_pm_transaction] ([mold_id], [threshold_shots]) WHERE ([category]='Shot-based' AND [threshold_shots] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_notification_read_transaction_notification' AND object_id=OBJECT_ID(N'[transactions].[notification_read_transaction]'))
CREATE NONCLUSTERED INDEX [IX_notification_read_transaction_notification] ON [transactions].[notification_read_transaction] ([notification_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_notification_transaction_module_created_at' AND object_id=OBJECT_ID(N'[transactions].[notification_transaction]'))
CREATE NONCLUSTERED INDEX [IX_notification_transaction_module_created_at] ON [transactions].[notification_transaction] ([module_code], [created_at] DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_production_entry_transaction_date_machine' AND object_id=OBJECT_ID(N'[transactions].[production_entry_transaction]'))
CREATE NONCLUSTERED INDEX [IX_production_entry_transaction_date_machine] ON [transactions].[production_entry_transaction] ([entry_date], [machine_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_production_entry_transaction_mold_date' AND object_id=OBJECT_ID(N'[transactions].[production_entry_transaction]'))
CREATE NONCLUSTERED INDEX [IX_production_entry_transaction_mold_date] ON [transactions].[production_entry_transaction] ([mold_id], [entry_date]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_stock_transaction_reference' AND object_id=OBJECT_ID(N'[transactions].[spare_part_stock_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_stock_transaction_reference] ON [transactions].[spare_part_stock_transaction] ([reference_type], [reference_id]) WHERE ([reference_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_stock_transaction_spare_part' AND object_id=OBJECT_ID(N'[transactions].[spare_part_stock_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_stock_transaction_spare_part] ON [transactions].[spare_part_stock_transaction] ([spare_part_id], [stock_transaction_id]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_usage_transaction_machine_date' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_usage_transaction_machine_date] ON [transactions].[spare_part_usage_transaction] ([machine_id], [usage_date]) WHERE ([machine_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_usage_transaction_machine_pm' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_usage_transaction_machine_pm] ON [transactions].[spare_part_usage_transaction] ([machine_pm_id]) WHERE ([machine_pm_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_usage_transaction_mold_date' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_usage_transaction_mold_date] ON [transactions].[spare_part_usage_transaction] ([mold_id], [usage_date]) WHERE ([mold_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_usage_transaction_mold_pm' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_usage_transaction_mold_pm] ON [transactions].[spare_part_usage_transaction] ([mold_pm_id]) WHERE ([mold_pm_id] IS NOT NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_usage_transaction_spare_part_date' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_usage_transaction_spare_part_date] ON [transactions].[spare_part_usage_transaction] ([spare_part_id], [usage_date]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_spare_part_usage_transaction_usage_date' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE NONCLUSTERED INDEX [IX_spare_part_usage_transaction_usage_date] ON [transactions].[spare_part_usage_transaction] ([usage_date]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_spare_part_usage_transaction_request_id' AND object_id=OBJECT_ID(N'[transactions].[spare_part_usage_transaction]'))
CREATE UNIQUE NONCLUSTERED INDEX [UX_spare_part_usage_transaction_request_id] ON [transactions].[spare_part_usage_transaction] ([request_id]) WHERE ([request_id] IS NOT NULL);
GO
