-- ============================================================================
-- One-time alignment of the EXISTING moulds with the rules approved on 2026-11-18:
--   A5 - one life limit: Maximum Shots is hidden in the form and saved equal to Replacement Shots, so Life % and
--        Remaining are measured against the limit that blocks production. Moulds whose maximum differs are aligned.
--   A2/A3 - the status is system-managed: a mould with shots is In Production, one at its limit is Replacement Due.
--        Retired and Maintenance (a PM in progress) are left exactly as they are.
-- Run it once, after deploying the new API. Step 1 only shows what will change; nothing is written until step 2.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];   -- check the database name before running
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ---- Step 1: preview --------------------------------------------------------
SELECT mold_code, mold_name, maximum_shots, replacement_shots, warning_shots, current_usage_shots, status AS status_now,
       CASE WHEN status IN ('Retired', 'Maintenance') THEN status
            WHEN current_usage_shots >= replacement_shots THEN 'Replacement Due'
            WHEN current_usage_shots > 0 THEN 'In Production'
            ELSE 'Available' END AS status_after,
       CASE WHEN maximum_shots <> replacement_shots THEN 'maximum -> ' + CAST(replacement_shots AS varchar(12)) ELSE '' END AS life_change
FROM [masters].[mold_master]
WHERE maximum_shots <> replacement_shots
   OR (status NOT IN ('Retired', 'Maintenance')
       AND status <> CASE WHEN current_usage_shots >= replacement_shots THEN 'Replacement Due'
                          WHEN current_usage_shots > 0 THEN 'In Production'
                          ELSE 'Available' END)
ORDER BY mold_code;
GO

-- ---- Step 2: apply (one transaction) -----------------------------------------
BEGIN TRANSACTION;

DECLARE @now datetime2(0) = SYSUTCDATETIME();
DECLARE @life int, @status int;

-- A5: warning < replacement <= maximum holds before and after (CK constraints), since replacement <= maximum already.
UPDATE [masters].[mold_master]
SET maximum_shots = replacement_shots, updated_at = @now
WHERE maximum_shots <> replacement_shots;
SET @life = @@ROWCOUNT;

UPDATE [masters].[mold_master]
SET status = CASE WHEN current_usage_shots >= replacement_shots THEN 'Replacement Due'
                  WHEN current_usage_shots > 0 THEN 'In Production'
                  ELSE 'Available' END,
    updated_at = @now
WHERE status NOT IN ('Retired', 'Maintenance')
  AND status <> CASE WHEN current_usage_shots >= replacement_shots THEN 'Replacement Due'
                     WHEN current_usage_shots > 0 THEN 'In Production'
                     ELSE 'Available' END;
SET @status = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT CAST(@life AS varchar(10)) + ' mould(s): maximum shots set to the replacement shots.';
PRINT CAST(@status AS varchar(10)) + ' mould(s): status corrected from their usage.';
GO
