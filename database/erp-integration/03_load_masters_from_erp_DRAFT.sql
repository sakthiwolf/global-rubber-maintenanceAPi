-- ============================================================================
-- DRAFT - Load real products and molds into MMM from the ERP
-- Requires: 01 (synonyms + view) and 02 (link columns).
-- Safe to re-run: only items/tools not yet linked are inserted.
-- Business codes come from configuration.document_sequence_configuration
-- (PRD-xxxx / MLD-xxxx), exactly like the application does.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID(N'[masters].[erp_uom_master]', N'SN') IS NULL
    CREATE SYNONYM [masters].[erp_uom_master] FOR [CHE_WA_GLOBAL_ERP].[dbo].[UOM_Master];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- A. PRODUCTS: every ERP item that has active production
-- ---------------------------------------------------------------------------
DECLARE @prefix varchar(10), @pad int, @last int, @n int;
SELECT @prefix = prefix, @pad = pad_length, @last = last_number
FROM [configuration].[document_sequence_configuration] WITH (UPDLOCK, HOLDLOCK)
WHERE document_type = 'PRODUCT';

;WITH items AS (
    SELECT DISTINCT v.erp_item_id
    FROM [transactions].[vw_erp_production] v
    WHERE v.erp_status = 'A' AND v.erp_item_id IS NOT NULL
), new_items AS (
    SELECT i.IM_ID, i.IM_PartNo, i.IM_PartName, i.IM_Category, u.UM_UOM,
           ROW_NUMBER() OVER (ORDER BY i.IM_ID) AS rn
    FROM items x
    JOIN [masters].[erp_item_master] i ON i.IM_ID = x.erp_item_id
    LEFT JOIN [masters].[erp_uom_master] u ON u.UM_ID = i.IM_UOM
    WHERE NOT EXISTS (SELECT 1 FROM [masters].[product_master] p WHERE p.erp_item_id = i.IM_ID)
)
INSERT INTO [masters].[product_master]
    (product_code, product_name, category, unit_of_measure, remarks, is_active, erp_item_id)
SELECT @prefix + '-' + RIGHT(REPLICATE('0', @pad) + CAST(@last + rn AS varchar(10)),
                             CASE WHEN LEN(CAST(@last + rn AS varchar(10))) > @pad THEN LEN(CAST(@last + rn AS varchar(10))) ELSE @pad END),
       LEFT(LTRIM(RTRIM(IM_PartName)) COLLATE DATABASE_DEFAULT, 150),
       LEFT(LTRIM(RTRIM(IM_Category)) COLLATE DATABASE_DEFAULT, 100),
       LEFT(ISNULL(NULLIF(LTRIM(RTRIM(UM_UOM)) COLLATE DATABASE_DEFAULT, ''), N'NOS'), 20),
       LEFT(N'ERP part no: ' + ISNULL(LTRIM(RTRIM(IM_PartNo)) COLLATE DATABASE_DEFAULT, N'-'), 500),
       1,
       IM_ID
FROM new_items;
SET @n = @@ROWCOUNT;

UPDATE [configuration].[document_sequence_configuration]
SET last_number = @last + @n, updated_at = SYSUTCDATETIME()
WHERE document_type = 'PRODUCT';

-- ---------------------------------------------------------------------------
-- B. MOLDS: one per ERP tool number
--    Only proper tool codes (DAI-xx, HASI-xx ...). Pure numbers ('1','50','51')
--    and '0.001' are held back until the business confirms what they are.
--    cavity_count = most different items the tool made in ONE shift
--    (LH + RH together -> 2).  Life limits keep the MMM defaults and MUST be
--    reviewed per mold by the business.
-- ---------------------------------------------------------------------------
SELECT @prefix = prefix, @pad = pad_length, @last = last_number
FROM [configuration].[document_sequence_configuration] WITH (UPDLOCK, HOLDLOCK)
WHERE document_type = 'MOLD';

;WITH prod AS (
    SELECT v.tool_no, v.erp_item_id, v.mfg_date, v.shift, v.machine_code, v.production_qty
    FROM [transactions].[vw_erp_production] v
    WHERE v.erp_status = 'A' AND v.tool_no LIKE '%[A-Z]%'
), cav AS (           -- items made together in one shift
    SELECT tool_no, MAX(n) AS cavity_count
    FROM (SELECT tool_no, COUNT(DISTINCT erp_item_id) n
          FROM prod GROUP BY tool_no, mfg_date, shift, machine_code) s
    GROUP BY tool_no
), main_item AS (     -- item with the most pieces = the mold's main product
    SELECT tool_no, erp_item_id,
           ROW_NUMBER() OVER (PARTITION BY tool_no ORDER BY SUM(production_qty) DESC, erp_item_id) r
    FROM prod GROUP BY tool_no, erp_item_id
), new_tools AS (
    SELECT c.tool_no, c.cavity_count, p.product_id, p.product_name,
           ROW_NUMBER() OVER (ORDER BY c.tool_no) AS rn
    FROM cav c
    JOIN main_item m ON m.tool_no = c.tool_no AND m.r = 1
    JOIN [masters].[product_master] p ON p.erp_item_id = m.erp_item_id
    WHERE NOT EXISTS (SELECT 1 FROM [masters].[mold_master] x
                      WHERE x.erp_tool_no = c.tool_no)
)
INSERT INTO [masters].[mold_master]
    (mold_code, mold_name, product_id, mold_type, cavity_count, status, remarks, erp_tool_no)
SELECT @prefix + '-' + RIGHT(REPLICATE('0', @pad) + CAST(@last + rn AS varchar(10)),
                             CASE WHEN LEN(CAST(@last + rn AS varchar(10))) > @pad THEN LEN(CAST(@last + rn AS varchar(10))) ELSE @pad END),
       LEFT(tool_no + N' - ' + product_name, 150),
       product_id,
       N'ERP Tool',
       CASE WHEN cavity_count < 1 THEN 1 ELSE cavity_count END,
       'Available',
       N'Imported from ERP. Review cavity count and life limits.',
       tool_no
FROM new_tools;
SET @n = @@ROWCOUNT;

UPDATE [configuration].[document_sequence_configuration]
SET last_number = @last + @n, updated_at = SYSUTCDATETIME()
WHERE document_type = 'MOLD';

COMMIT TRANSACTION;
GO

-- Result
SELECT mold_code, erp_tool_no, mold_name, cavity_count, maximum_shots, warning_shots, replacement_shots
FROM [masters].[mold_master] WHERE erp_tool_no IS NOT NULL ORDER BY mold_code;
GO
