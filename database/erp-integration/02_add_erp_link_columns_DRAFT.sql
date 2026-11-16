-- ============================================================================
-- DRAFT - Add ERP link columns to MMM (additive only: nothing renamed/removed)
-- Needs approval (CLAUDE.md section 4). Safe to re-run.
-- Existing EF Core code keeps working: new columns are NULL or have a default.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
SET XACT_ABORT ON;
GO

-- 1. Mold  <->  ERP tool number  (e.g. MLD-0006 <-> 'DAI-49')
IF COL_LENGTH('masters.mold_master', 'erp_tool_no') IS NULL
    ALTER TABLE [masters].[mold_master] ADD [erp_tool_no] varchar(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_mold_master_erp_tool_no')
    CREATE UNIQUE NONCLUSTERED INDEX [UX_mold_master_erp_tool_no]
        ON [masters].[mold_master] ([erp_tool_no]) WHERE [erp_tool_no] IS NOT NULL;
GO

-- 2. Product  <->  ERP Item_Master.IM_ID
IF COL_LENGTH('masters.product_master', 'erp_item_id') IS NULL
    ALTER TABLE [masters].[product_master] ADD [erp_item_id] int NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_product_master_erp_item_id')
    CREATE UNIQUE NONCLUSTERED INDEX [UX_product_master_erp_item_id]
        ON [masters].[product_master] ([erp_item_id]) WHERE [erp_item_id] IS NOT NULL;
GO

-- 3. Production entry  <->  ERP Production_Barcode_Details.pro_iTransId
IF COL_LENGTH('transactions.production_entry_transaction', 'source') IS NULL
    ALTER TABLE [transactions].[production_entry_transaction]
        ADD [source] varchar(10) NOT NULL
            CONSTRAINT [DF_production_entry_transaction_source] DEFAULT ('Manual')
            CONSTRAINT [CK_production_entry_transaction_source] CHECK ([source] IN ('Manual','ERP'));
GO
IF COL_LENGTH('transactions.production_entry_transaction', 'erp_trans_id') IS NULL
    ALTER TABLE [transactions].[production_entry_transaction] ADD [erp_trans_id] int NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_production_entry_transaction_erp_trans_id')
    CREATE UNIQUE NONCLUSTERED INDEX [UX_production_entry_transaction_erp_trans_id]
        ON [transactions].[production_entry_transaction] ([erp_trans_id]) WHERE [erp_trans_id] IS NOT NULL;
GO

-- 4. Sync watermark (existing settings table - no new table)
IF NOT EXISTS (SELECT 1 FROM [configuration].[system_settings_configuration] WHERE setting_key = 'ErpProductionLastTransId')
    INSERT INTO [configuration].[system_settings_configuration] (setting_key, setting_value, description)
    VALUES ('ErpProductionLastTransId', '0', N'Last ERP Production_Barcode_Details.pro_iTransId synced into MMM');
GO
