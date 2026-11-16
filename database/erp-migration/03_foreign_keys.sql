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

-- STEP 03: 109 foreign keys (checked against the loaded data)

IF OBJECT_ID(N'[audit].[FK_application_log_user]', N'F') IS NULL
ALTER TABLE [audit].[application_log] ADD CONSTRAINT [FK_application_log_user] FOREIGN KEY ([user_id]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[audit].[FK_audit_log_user]', N'F') IS NULL
ALTER TABLE [audit].[audit_log] ADD CONSTRAINT [FK_audit_log_user] FOREIGN KEY ([user_id]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[audit].[FK_audit_log_detail_audit_log]', N'F') IS NULL
ALTER TABLE [audit].[audit_log_detail] ADD CONSTRAINT [FK_audit_log_detail_audit_log] FOREIGN KEY ([audit_log_id]) REFERENCES [audit].[audit_log] ([audit_log_id]) ON DELETE CASCADE;
GO
IF OBJECT_ID(N'[configuration].[FK_document_sequence_configuration_created_by]', N'F') IS NULL
ALTER TABLE [configuration].[document_sequence_configuration] ADD CONSTRAINT [FK_document_sequence_configuration_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[configuration].[FK_document_sequence_configuration_updated_by]', N'F') IS NULL
ALTER TABLE [configuration].[document_sequence_configuration] ADD CONSTRAINT [FK_document_sequence_configuration_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[configuration].[FK_system_settings_configuration_created_by]', N'F') IS NULL
ALTER TABLE [configuration].[system_settings_configuration] ADD CONSTRAINT [FK_system_settings_configuration_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[configuration].[FK_system_settings_configuration_updated_by]', N'F') IS NULL
ALTER TABLE [configuration].[system_settings_configuration] ADD CONSTRAINT [FK_system_settings_configuration_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[configuration].[FK_user_settings_configuration_updated_by]', N'F') IS NULL
ALTER TABLE [configuration].[user_settings_configuration] ADD CONSTRAINT [FK_user_settings_configuration_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[configuration].[FK_user_settings_configuration_user]', N'F') IS NULL
ALTER TABLE [configuration].[user_settings_configuration] ADD CONSTRAINT [FK_user_settings_configuration_user] FOREIGN KEY ([user_id]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_breakdown_type_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[breakdown_type_master] ADD CONSTRAINT [FK_breakdown_type_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_breakdown_type_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[breakdown_type_master] ADD CONSTRAINT [FK_breakdown_type_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_department_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[department_master] ADD CONSTRAINT [FK_department_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_department_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[department_master] ADD CONSTRAINT [FK_department_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_employee_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[employee_master] ADD CONSTRAINT [FK_employee_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_employee_master_department]', N'F') IS NULL
ALTER TABLE [masters].[employee_master] ADD CONSTRAINT [FK_employee_master_department] FOREIGN KEY ([department_id]) REFERENCES [masters].[department_master] ([department_id]);
GO
IF OBJECT_ID(N'[masters].[FK_employee_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[employee_master] ADD CONSTRAINT [FK_employee_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_machine_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[machine_master] ADD CONSTRAINT [FK_machine_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_machine_master_department]', N'F') IS NULL
ALTER TABLE [masters].[machine_master] ADD CONSTRAINT [FK_machine_master_department] FOREIGN KEY ([department_id]) REFERENCES [masters].[department_master] ([department_id]);
GO
IF OBJECT_ID(N'[masters].[FK_machine_master_responsible_engineer]', N'F') IS NULL
ALTER TABLE [masters].[machine_master] ADD CONSTRAINT [FK_machine_master_responsible_engineer] FOREIGN KEY ([responsible_engineer_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[masters].[FK_machine_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[machine_master] ADD CONSTRAINT [FK_machine_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_checklist_item_master_checklist]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_item_master] ADD CONSTRAINT [FK_checklist_item_master_checklist] FOREIGN KEY ([checklist_id]) REFERENCES [masters].[maintenance_checklist_master] ([checklist_id]) ON DELETE CASCADE;
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_item_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_item_master] ADD CONSTRAINT [FK_maintenance_checklist_item_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_item_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_item_master] ADD CONSTRAINT [FK_maintenance_checklist_item_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_master] ADD CONSTRAINT [FK_maintenance_checklist_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_master_machine]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_master] ADD CONSTRAINT [FK_maintenance_checklist_master_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_master_maintenance_type]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_master] ADD CONSTRAINT [FK_maintenance_checklist_master_maintenance_type] FOREIGN KEY ([maintenance_type_id]) REFERENCES [masters].[maintenance_type_master] ([maintenance_type_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_master_source]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_master] ADD CONSTRAINT [FK_maintenance_checklist_master_source] FOREIGN KEY ([source_checklist_id]) REFERENCES [masters].[maintenance_checklist_master] ([checklist_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_checklist_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_checklist_master] ADD CONSTRAINT [FK_maintenance_checklist_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_type_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_type_master] ADD CONSTRAINT [FK_maintenance_type_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_maintenance_type_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[maintenance_type_master] ADD CONSTRAINT [FK_maintenance_type_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_mold_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[mold_master] ADD CONSTRAINT [FK_mold_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_mold_master_product]', N'F') IS NULL
ALTER TABLE [masters].[mold_master] ADD CONSTRAINT [FK_mold_master_product] FOREIGN KEY ([product_id]) REFERENCES [masters].[product_master] ([product_id]);
GO
IF OBJECT_ID(N'[masters].[FK_mold_master_responsible_employee]', N'F') IS NULL
ALTER TABLE [masters].[mold_master] ADD CONSTRAINT [FK_mold_master_responsible_employee] FOREIGN KEY ([responsible_employee_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[masters].[FK_mold_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[mold_master] ADD CONSTRAINT [FK_mold_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_product_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[product_master] ADD CONSTRAINT [FK_product_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_product_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[product_master] ADD CONSTRAINT [FK_product_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_spare_part_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[spare_part_master] ADD CONSTRAINT [FK_spare_part_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_spare_part_master_machine]', N'F') IS NULL
ALTER TABLE [masters].[spare_part_master] ADD CONSTRAINT [FK_spare_part_master_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[masters].[FK_spare_part_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[spare_part_master] ADD CONSTRAINT [FK_spare_part_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_spare_part_master_vendor]', N'F') IS NULL
ALTER TABLE [masters].[spare_part_master] ADD CONSTRAINT [FK_spare_part_master_vendor] FOREIGN KEY ([vendor_id]) REFERENCES [masters].[vendor_master] ([vendor_id]);
GO
IF OBJECT_ID(N'[masters].[FK_vendor_master_created_by]', N'F') IS NULL
ALTER TABLE [masters].[vendor_master] ADD CONSTRAINT [FK_vendor_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[masters].[FK_vendor_master_updated_by]', N'F') IS NULL
ALTER TABLE [masters].[vendor_master] ADD CONSTRAINT [FK_vendor_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_module_master_created_by]', N'F') IS NULL
ALTER TABLE [security].[module_master] ADD CONSTRAINT [FK_module_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_module_master_parent_module]', N'F') IS NULL
ALTER TABLE [security].[module_master] ADD CONSTRAINT [FK_module_master_parent_module] FOREIGN KEY ([parent_module_id]) REFERENCES [security].[module_master] ([module_id]);
GO
IF OBJECT_ID(N'[security].[FK_module_master_updated_by]', N'F') IS NULL
ALTER TABLE [security].[module_master] ADD CONSTRAINT [FK_module_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_permission_master_created_by]', N'F') IS NULL
ALTER TABLE [security].[permission_master] ADD CONSTRAINT [FK_permission_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_permission_master_module]', N'F') IS NULL
ALTER TABLE [security].[permission_master] ADD CONSTRAINT [FK_permission_master_module] FOREIGN KEY ([module_id]) REFERENCES [security].[module_master] ([module_id]);
GO
IF OBJECT_ID(N'[security].[FK_permission_master_role]', N'F') IS NULL
ALTER TABLE [security].[permission_master] ADD CONSTRAINT [FK_permission_master_role] FOREIGN KEY ([role_id]) REFERENCES [security].[role_master] ([role_id]);
GO
IF OBJECT_ID(N'[security].[FK_permission_master_updated_by]', N'F') IS NULL
ALTER TABLE [security].[permission_master] ADD CONSTRAINT [FK_permission_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_role_master_created_by]', N'F') IS NULL
ALTER TABLE [security].[role_master] ADD CONSTRAINT [FK_role_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_role_master_updated_by]', N'F') IS NULL
ALTER TABLE [security].[role_master] ADD CONSTRAINT [FK_role_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_user_master_created_by]', N'F') IS NULL
ALTER TABLE [security].[user_master] ADD CONSTRAINT [FK_user_master_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_user_master_department]', N'F') IS NULL
ALTER TABLE [security].[user_master] ADD CONSTRAINT [FK_user_master_department] FOREIGN KEY ([department_id]) REFERENCES [masters].[department_master] ([department_id]);
GO
IF OBJECT_ID(N'[security].[FK_user_master_employee]', N'F') IS NULL
ALTER TABLE [security].[user_master] ADD CONSTRAINT [FK_user_master_employee] FOREIGN KEY ([employee_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[security].[FK_user_master_role]', N'F') IS NULL
ALTER TABLE [security].[user_master] ADD CONSTRAINT [FK_user_master_role] FOREIGN KEY ([role_id]) REFERENCES [security].[role_master] ([role_id]);
GO
IF OBJECT_ID(N'[security].[FK_user_master_updated_by]', N'F') IS NULL
ALTER TABLE [security].[user_master] ADD CONSTRAINT [FK_user_master_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[security].[FK_user_refresh_token_user]', N'F') IS NULL
ALTER TABLE [security].[user_refresh_token] ADD CONSTRAINT [FK_user_refresh_token_user] FOREIGN KEY ([user_id]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_breakdown_stage_transaction_breakdown]', N'F') IS NULL
ALTER TABLE [transactions].[breakdown_stage_transaction] ADD CONSTRAINT [FK_breakdown_stage_transaction_breakdown] FOREIGN KEY ([machine_breakdown_id]) REFERENCES [transactions].[machine_breakdown_transaction] ([machine_breakdown_id]) ON DELETE CASCADE;
GO
IF OBJECT_ID(N'[transactions].[FK_breakdown_stage_transaction_changed_by]', N'F') IS NULL
ALTER TABLE [transactions].[breakdown_stage_transaction] ADD CONSTRAINT [FK_breakdown_stage_transaction_changed_by] FOREIGN KEY ([changed_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_breakdown_transaction_assigned_engineer]', N'F') IS NULL
ALTER TABLE [transactions].[machine_breakdown_transaction] ADD CONSTRAINT [FK_machine_breakdown_transaction_assigned_engineer] FOREIGN KEY ([assigned_engineer_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_breakdown_transaction_breakdown_type]', N'F') IS NULL
ALTER TABLE [transactions].[machine_breakdown_transaction] ADD CONSTRAINT [FK_machine_breakdown_transaction_breakdown_type] FOREIGN KEY ([breakdown_type_id]) REFERENCES [masters].[breakdown_type_master] ([breakdown_type_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_breakdown_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[machine_breakdown_transaction] ADD CONSTRAINT [FK_machine_breakdown_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_breakdown_transaction_machine]', N'F') IS NULL
ALTER TABLE [transactions].[machine_breakdown_transaction] ADD CONSTRAINT [FK_machine_breakdown_transaction_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_breakdown_transaction_updated_by]', N'F') IS NULL
ALTER TABLE [transactions].[machine_breakdown_transaction] ADD CONSTRAINT [FK_machine_breakdown_transaction_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_checklist_transaction_item]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_checklist_transaction] ADD CONSTRAINT [FK_machine_pm_checklist_transaction_item] FOREIGN KEY ([checklist_item_id]) REFERENCES [masters].[maintenance_checklist_item_master] ([checklist_item_id]) ON DELETE SET NULL;
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_checklist_transaction_pm]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_checklist_transaction] ADD CONSTRAINT [FK_machine_pm_checklist_transaction_pm] FOREIGN KEY ([machine_pm_id]) REFERENCES [transactions].[machine_pm_transaction] ([machine_pm_id]) ON DELETE CASCADE;
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_transaction_checklist]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_transaction] ADD CONSTRAINT [FK_machine_pm_transaction_checklist] FOREIGN KEY ([checklist_id]) REFERENCES [masters].[maintenance_checklist_master] ([checklist_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_transaction] ADD CONSTRAINT [FK_machine_pm_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_transaction_engineer]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_transaction] ADD CONSTRAINT [FK_machine_pm_transaction_engineer] FOREIGN KEY ([engineer_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_transaction_machine]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_transaction] ADD CONSTRAINT [FK_machine_pm_transaction_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_transaction_maintenance_type]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_transaction] ADD CONSTRAINT [FK_machine_pm_transaction_maintenance_type] FOREIGN KEY ([maintenance_type_id]) REFERENCES [masters].[maintenance_type_master] ([maintenance_type_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_machine_pm_transaction_updated_by]', N'F') IS NULL
ALTER TABLE [transactions].[machine_pm_transaction] ADD CONSTRAINT [FK_machine_pm_transaction_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_maintenance_work_order_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_maintenance_work_order_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_maintenance_work_order_transaction_updated_by]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_maintenance_work_order_transaction_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_work_order_transaction_assigned_engineer]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_work_order_transaction_assigned_engineer] FOREIGN KEY ([assigned_engineer_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_work_order_transaction_breakdown]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_work_order_transaction_breakdown] FOREIGN KEY ([machine_breakdown_id]) REFERENCES [transactions].[machine_breakdown_transaction] ([machine_breakdown_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_work_order_transaction_machine]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_work_order_transaction_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_work_order_transaction_machine_pm]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_work_order_transaction_machine_pm] FOREIGN KEY ([machine_pm_id]) REFERENCES [transactions].[machine_pm_transaction] ([machine_pm_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_work_order_transaction_mold]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_work_order_transaction_mold] FOREIGN KEY ([mold_id]) REFERENCES [masters].[mold_master] ([mold_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_work_order_transaction_mold_pm]', N'F') IS NULL
ALTER TABLE [transactions].[maintenance_work_order_transaction] ADD CONSTRAINT [FK_work_order_transaction_mold_pm] FOREIGN KEY ([mold_pm_id]) REFERENCES [transactions].[mold_pm_transaction] ([mold_pm_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_checklist_transaction_item]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_checklist_transaction] ADD CONSTRAINT [FK_mold_pm_checklist_transaction_item] FOREIGN KEY ([checklist_item_id]) REFERENCES [masters].[maintenance_checklist_item_master] ([checklist_item_id]) ON DELETE SET NULL;
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_checklist_transaction_pm]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_checklist_transaction] ADD CONSTRAINT [FK_mold_pm_checklist_transaction_pm] FOREIGN KEY ([mold_pm_id]) REFERENCES [transactions].[mold_pm_transaction] ([mold_pm_id]) ON DELETE CASCADE;
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_transaction_checklist]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_transaction] ADD CONSTRAINT [FK_mold_pm_transaction_checklist] FOREIGN KEY ([checklist_id]) REFERENCES [masters].[maintenance_checklist_master] ([checklist_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_transaction] ADD CONSTRAINT [FK_mold_pm_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_transaction_engineer]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_transaction] ADD CONSTRAINT [FK_mold_pm_transaction_engineer] FOREIGN KEY ([engineer_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_transaction_mold]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_transaction] ADD CONSTRAINT [FK_mold_pm_transaction_mold] FOREIGN KEY ([mold_id]) REFERENCES [masters].[mold_master] ([mold_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_mold_pm_transaction_updated_by]', N'F') IS NULL
ALTER TABLE [transactions].[mold_pm_transaction] ADD CONSTRAINT [FK_mold_pm_transaction_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_notification_read_transaction_notification]', N'F') IS NULL
ALTER TABLE [transactions].[notification_read_transaction] ADD CONSTRAINT [FK_notification_read_transaction_notification] FOREIGN KEY ([notification_id]) REFERENCES [transactions].[notification_transaction] ([notification_id]) ON DELETE CASCADE;
GO
IF OBJECT_ID(N'[transactions].[FK_notification_read_transaction_user]', N'F') IS NULL
ALTER TABLE [transactions].[notification_read_transaction] ADD CONSTRAINT [FK_notification_read_transaction_user] FOREIGN KEY ([user_id]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_notification_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[notification_transaction] ADD CONSTRAINT [FK_notification_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_notification_transaction_module]', N'F') IS NULL
ALTER TABLE [transactions].[notification_transaction] ADD CONSTRAINT [FK_notification_transaction_module] FOREIGN KEY ([module_code]) REFERENCES [security].[module_master] ([module_code]);
GO
IF OBJECT_ID(N'[transactions].[FK_production_entry_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[production_entry_transaction] ADD CONSTRAINT [FK_production_entry_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_production_entry_transaction_machine]', N'F') IS NULL
ALTER TABLE [transactions].[production_entry_transaction] ADD CONSTRAINT [FK_production_entry_transaction_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_production_entry_transaction_mold]', N'F') IS NULL
ALTER TABLE [transactions].[production_entry_transaction] ADD CONSTRAINT [FK_production_entry_transaction_mold] FOREIGN KEY ([mold_id]) REFERENCES [masters].[mold_master] ([mold_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_production_entry_transaction_product]', N'F') IS NULL
ALTER TABLE [transactions].[production_entry_transaction] ADD CONSTRAINT [FK_production_entry_transaction_product] FOREIGN KEY ([product_id]) REFERENCES [masters].[product_master] ([product_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_production_entry_transaction_updated_by]', N'F') IS NULL
ALTER TABLE [transactions].[production_entry_transaction] ADD CONSTRAINT [FK_production_entry_transaction_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_stock_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_stock_transaction] ADD CONSTRAINT [FK_spare_part_stock_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_stock_transaction_spare_part]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_stock_transaction] ADD CONSTRAINT [FK_spare_part_stock_transaction_spare_part] FOREIGN KEY ([spare_part_id]) REFERENCES [masters].[spare_part_master] ([spare_part_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_breakdown]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_breakdown] FOREIGN KEY ([machine_breakdown_id]) REFERENCES [transactions].[machine_breakdown_transaction] ([machine_breakdown_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_created_by]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_created_by] FOREIGN KEY ([created_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_machine]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_machine] FOREIGN KEY ([machine_id]) REFERENCES [masters].[machine_master] ([machine_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_machine_pm]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_machine_pm] FOREIGN KEY ([machine_pm_id]) REFERENCES [transactions].[machine_pm_transaction] ([machine_pm_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_mold]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_mold] FOREIGN KEY ([mold_id]) REFERENCES [masters].[mold_master] ([mold_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_mold_pm]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_mold_pm] FOREIGN KEY ([mold_pm_id]) REFERENCES [transactions].[mold_pm_transaction] ([mold_pm_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_reversed_by]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_reversed_by] FOREIGN KEY ([reversed_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_spare_part]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_spare_part] FOREIGN KEY ([spare_part_id]) REFERENCES [masters].[spare_part_master] ([spare_part_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_updated_by]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_updated_by] FOREIGN KEY ([updated_by]) REFERENCES [security].[user_master] ([user_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_used_by]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_used_by] FOREIGN KEY ([used_by_employee_id]) REFERENCES [masters].[employee_master] ([employee_id]);
GO
IF OBJECT_ID(N'[transactions].[FK_spare_part_usage_transaction_work_order]', N'F') IS NULL
ALTER TABLE [transactions].[spare_part_usage_transaction] ADD CONSTRAINT [FK_spare_part_usage_transaction_work_order] FOREIGN KEY ([work_order_id]) REFERENCES [transactions].[maintenance_work_order_transaction] ([work_order_id]);
GO
