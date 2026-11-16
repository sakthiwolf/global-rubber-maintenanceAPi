-- ============================================================================
-- READ ONLY - Mold life from ERP production (fixes the LH/RH double count)
--
--   shots per run = CEILING( SUM(pieces of ALL items on that tool, same
--                            date + shift + machine) / mold.cavity_count )
--
--   DAI-60, 14-Nov, Shift A: LH 328 + RH 328 = 656 pieces / cavity 2 = 328 shots
--   (counting barcode rows naively would give 656 -> life used 2x too fast)
--
-- Uses status 'A' only (the ERP's own reports use pro_cStatus = 'A').
-- Nothing is written. Requires 01, 02, 03.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
;WITH runs AS (
    SELECT m.mold_id, v.mfg_date, v.shift, v.machine_code,
           SUM(v.production_qty) AS pieces,
           SUM(v.rejected_qty)   AS rejected,
           CEILING(SUM(v.production_qty) * 1.0 / m.cavity_count) AS shots
    FROM [transactions].[vw_erp_production] v
    JOIN [masters].[mold_master] m ON m.erp_tool_no = v.tool_no
    WHERE v.erp_status = 'A'
      AND v.mfg_date <= CAST(GETDATE() AS date)           -- ignore future-dated rows
    GROUP BY m.mold_id, m.cavity_count, v.mfg_date, v.shift, v.machine_code
)
SELECT m.mold_code, m.erp_tool_no, m.cavity_count,
       COUNT(*)            AS runs,
       SUM(r.pieces)       AS total_pieces,
       SUM(r.shots)        AS total_shots,
       m.warning_shots, m.replacement_shots,
       CAST(SUM(r.shots) * 100.0 / m.replacement_shots AS decimal(5,1)) AS life_used_pct,
       CASE WHEN SUM(r.shots) >= m.replacement_shots THEN 'Replace'
            WHEN SUM(r.shots) >= m.warning_shots     THEN 'Warning'
            ELSE 'Normal' END AS life_state,
       MAX(r.mfg_date)     AS last_production
FROM runs r
JOIN [masters].[mold_master] m ON m.mold_id = r.mold_id
GROUP BY m.mold_code, m.erp_tool_no, m.cavity_count, m.warning_shots, m.replacement_shots
ORDER BY life_used_pct DESC;
GO
