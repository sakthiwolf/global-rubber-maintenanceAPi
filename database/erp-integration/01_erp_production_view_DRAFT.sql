-- ============================================================================
-- DRAFT - ERP production -> MMM integration (two-database approach)
-- Source : CHE_WA_GLOBAL_ERP.dbo.Production_Barcode_Details   (READ ONLY)
--          CHE_WA_GLOBAL_ERP.dbo.Item_Master
-- Target : CHE_WA_Global_Rubber_MMM (view only - nothing is written)
--
-- STATUS : DRAFT. Do NOT use for mold life until the open questions at the
--          bottom are answered by the business. Steps 1-2 are safe (read only).
-- Collation: ERP = SQL_Latin1_General_CP1_CI_AS, MMM = Latin1_General_CI_AS
--            -> every text join uses COLLATE DATABASE_DEFAULT.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Synonyms: the MMM app reads ERP tables as if they were local
IF OBJECT_ID(N'[transactions].[erp_production_barcode]', N'SN') IS NULL
    CREATE SYNONYM [transactions].[erp_production_barcode] FOR [CHE_WA_GLOBAL_ERP].[dbo].[Production_Barcode_Details];
GO
IF OBJECT_ID(N'[masters].[erp_item_master]', N'SN') IS NULL
    CREATE SYNONYM [masters].[erp_item_master] FOR [CHE_WA_GLOBAL_ERP].[dbo].[Item_Master];
GO

-- 2. Normalised ERP production view
--    tool_no  : 'DAI -49' / 'DAI - 49' / 'DAI-49'  -> 'DAI-49'
--    machine  : '17' / '017' / '6' / '06'          -> 'M/C-17' / 'M/C-06' (MMM machine_code)
--    shift    : 'I' / 'II' / 'III'                 -> 'Shift A' / 'Shift B' / 'Shift C'
CREATE OR ALTER VIEW [transactions].[vw_erp_production]
AS
SELECT
    -- every text column is converted to the MMM collation here, so nothing
    -- that uses this view has to deal with the ERP collation.
    p.pro_iTransId                                            AS erp_trans_id,
    p.pro_vBarcodeId COLLATE DATABASE_DEFAULT                 AS erp_barcode,
    p.pro_vLotNo     COLLATE DATABASE_DEFAULT                 AS lot_no,
    CAST(p.pro_MfgDate AS date)                               AS mfg_date,
    CASE LTRIM(RTRIM(p.pro_Shift)) COLLATE DATABASE_DEFAULT
         WHEN 'I'   THEN 'Shift A'
         WHEN 'II'  THEN 'Shift B'
         WHEN 'III' THEN 'Shift C' END                        AS shift,
    p.pro_starttime                                           AS start_time,
    p.pro_ENDTIME                                             AS end_time,

    -- machine
    p.pro_Machine COLLATE DATABASE_DEFAULT                    AS erp_machine_raw,
    CASE WHEN TRY_CAST(p.pro_Machine AS int) BETWEEN 1 AND 99
         THEN 'M/C-' + RIGHT('0' + CAST(TRY_CAST(p.pro_Machine AS int) AS varchar(2)), 2) END
                                                              AS machine_code,
    -- tool / mold
    p.Pro_ToolNo COLLATE DATABASE_DEFAULT                     AS erp_tool_raw,
    NULLIF(UPPER(REPLACE(LTRIM(RTRIM(p.Pro_ToolNo)), ' ', '')), '') COLLATE DATABASE_DEFAULT
                                                              AS tool_no,
    -- item / product
    TRY_CAST(p.pro_iItemId AS int)                            AS erp_item_id,
    i.IM_PartNo   COLLATE DATABASE_DEFAULT                    AS part_no,
    i.IM_PartName COLLATE DATABASE_DEFAULT                    AS part_name,
    p.pro_Model   COLLATE DATABASE_DEFAULT                    AS model,

    -- quantities (pieces)
    CAST(p.pro_nQty       AS int)                             AS production_qty,
    CAST(p.pro_nAcceptQty AS int)                             AS accepted_qty,
    CAST(p.pro_nRejQty    AS int)                             AS rejected_qty,

    RTRIM(p.pro_cStatus) COLLATE DATABASE_DEFAULT             AS erp_status,   -- A / SP / D
    p.pro_dCreatedDt                                          AS erp_created_at
FROM [transactions].[erp_production_barcode] AS p
LEFT JOIN [masters].[erp_item_master] AS i
       ON CAST(i.IM_ID AS varchar(50)) COLLATE DATABASE_DEFAULT
        = p.pro_iItemId COLLATE DATABASE_DEFAULT;
GO

-- 3. Data-quality check: rows that CANNOT be mapped (run and review)
SELECT
    SUM(CASE WHEN tool_no IS NULL THEN 1 ELSE 0 END)       AS no_tool,
    SUM(CASE WHEN machine_code IS NULL THEN 1 ELSE 0 END)  AS bad_machine,
    SUM(CASE WHEN shift IS NULL THEN 1 ELSE 0 END)         AS no_shift,
    SUM(CASE WHEN mfg_date > CAST(GETDATE() AS date) THEN 1 ELSE 0 END) AS future_date,
    SUM(CASE WHEN production_qty <> accepted_qty + rejected_qty THEN 1 ELSE 0 END) AS qty_mismatch
FROM [transactions].[vw_erp_production]
WHERE erp_status = 'A';

-- ERP machine values that do not exist in MMM machine_master
SELECT v.machine_code, v.erp_machine_raw, COUNT(*) AS rows_
FROM [transactions].[vw_erp_production] v
LEFT JOIN [masters].[machine_master] m ON m.machine_code = v.machine_code COLLATE DATABASE_DEFAULT
WHERE m.machine_id IS NULL
GROUP BY v.machine_code, v.erp_machine_raw
ORDER BY rows_ DESC;

-- 4. PREVIEW ONLY - mold usage per tool (NOT the final rule; see questions)
--    Uses status 'A' only and production_qty, same as the current MMM rule
--    (ProductionEntryService: usage += production_qty).
SELECT tool_no,
       COUNT(*)            AS entries,
       SUM(production_qty) AS total_pieces,
       MIN(mfg_date)       AS first_production,
       MAX(mfg_date)       AS last_production
FROM [transactions].[vw_erp_production]
WHERE erp_status = 'A' AND tool_no IS NOT NULL
GROUP BY tool_no
ORDER BY total_pieces DESC;
GO

-- ============================================================================
-- OPEN QUESTIONS - must be answered before mold life is calculated from ERP
-- ----------------------------------------------------------------------------
-- Q1 STATUS : A = active? SP = split/partial? D = deleted? Which ones count?
-- Q2 LH/RH  : One tool makes LH + RH items in the same shift (e.g. DAI-60 ->
--             items 172 & 173, DAI-66 -> 176 & 177). Is that ONE shot giving
--             2 pieces (count once) or two separate runs (count both)?
-- Q3 CAVITY : ERP qty is pieces. Shots = pieces / cavity_count ?
-- Q4 REJECT : Does rejected qty count towards mold life? (current MMM: yes)
-- Q5 TOOLS  : ERP tool numbers '1', '50', '51', '0.001' and blank - which
--             molds are these? 440 rows have no tool at all.
-- Q6 MOLD MASTER : MMM mold_master has only 2 test molds. Create the real
--             molds (DAI-21, DAI-42 ...) from ERP tool numbers? Opening
--             usage = history since 2026-02-28, or start from 0?
-- Q7 DATES  : Some rows have mfg_date 2026-11-30 (future) - data entry error?
-- ============================================================================
