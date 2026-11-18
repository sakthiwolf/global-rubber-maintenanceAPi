-- ============================================================================
-- Removes TEST machine PM data: the plan(s) behind the PM numbers listed in @TestPmNos, EVERY PM job of those plans
-- (completed and the next scheduled one), their checklist ticks and notifications. Then the affected machines' last /
-- next maintenance dates and status are recalculated from the PM jobs that remain.
--
-- Default: MPM-0073 (M/C-01, plan "DAILY") and MPM-0075 (M/C-03, plan "TEST") - the two Daily test PMs completed on
-- 18 Nov 2026. The 72 imported Weekly / Monthly jobs are NOT touched.
--
-- Step 1 only SHOWS what will be deleted. Check it, then run the whole script with @Apply = 1.
-- Stops without changing anything if a spare-part usage or work order is linked to one of these PMs.
-- The audit trail (audit.audit_log) is kept.
-- ============================================================================
USE [CHE_WA_Global_Rubber_MMM];   -- check the database name before running
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @TestPmNos   nvarchar(max) = N'MPM-0073,MPM-0075';  -- the test PM numbers seen on the screen
DECLARE @DeletePlans bit = 1;                               -- 1 = also delete the test plans themselves
DECLARE @Apply       bit = 0;                               -- 0 = preview only, 1 = delete

-- The plans behind the listed PMs
DECLARE @Plans TABLE (checklist_id int PRIMARY KEY);
INSERT INTO @Plans
SELECT DISTINCT p.checklist_id
FROM [transactions].[machine_pm_transaction] p
JOIN STRING_SPLIT(@TestPmNos, ',') s ON p.pm_no = LTRIM(RTRIM(s.value))
WHERE p.checklist_id IS NOT NULL;

-- Every PM of those plans, plus the listed PMs themselves (a manual PM has no plan)
DECLARE @Pms TABLE (machine_pm_id int PRIMARY KEY, machine_id int NOT NULL);
INSERT INTO @Pms
SELECT p.machine_pm_id, p.machine_id
FROM [transactions].[machine_pm_transaction] p
WHERE p.checklist_id IN (SELECT checklist_id FROM @Plans)
   OR p.pm_no IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@TestPmNos, ','));

-- ---- Step 1: preview --------------------------------------------------------
SELECT 'Plan to delete' AS what, c.checklist_code AS code, c.checklist_name AS name, c.frequency, m.machine_code
FROM [masters].[maintenance_checklist_master] c
LEFT JOIN [masters].[machine_master] m ON m.machine_id = c.machine_id
WHERE c.checklist_id IN (SELECT checklist_id FROM @Plans) AND @DeletePlans = 1;

SELECT 'PM job to delete' AS what, p.pm_no, m.machine_code, c.checklist_name AS plan_name, p.status, p.scheduled_date, p.completed_date, p.maintenance_by
FROM [transactions].[machine_pm_transaction] p
JOIN [masters].[machine_master] m ON m.machine_id = p.machine_id
LEFT JOIN [masters].[maintenance_checklist_master] c ON c.checklist_id = p.checklist_id
WHERE p.machine_pm_id IN (SELECT machine_pm_id FROM @Pms)
ORDER BY p.pm_no;

DECLARE @spares int = (SELECT COUNT(*) FROM [transactions].[spare_part_usage_transaction] WHERE machine_pm_id IN (SELECT machine_pm_id FROM @Pms));
DECLARE @orders int = (SELECT COUNT(*) FROM [transactions].[maintenance_work_order_transaction] WHERE machine_pm_id IN (SELECT machine_pm_id FROM @Pms));
DECLARE @notes  int = (SELECT COUNT(*) FROM [transactions].[notification_transaction] WHERE entity_name = 'MachinePm' AND entity_id IN (SELECT machine_pm_id FROM @Pms));
DECLARE @pmCount int = (SELECT COUNT(*) FROM @Pms);
DECLARE @planCount int = (SELECT COUNT(*) FROM @Plans);
PRINT CAST(@pmCount AS varchar(10)) + ' PM job(s), ' + CAST(@planCount AS varchar(10)) + ' plan(s), '
    + CAST(@notes AS varchar(10)) + ' notification(s) found.';

IF @spares > 0 OR @orders > 0
BEGIN
    PRINT 'STOPPED: ' + CAST(@spares AS varchar(10)) + ' spare-part usage(s) / ' + CAST(@orders AS varchar(10))
        + ' work order(s) are linked to these PMs. Reverse those first (stock must be restored) - nothing was changed.';
    RETURN;
END

IF @Apply = 0
BEGIN
    PRINT 'Preview only. Set @Apply = 1 and run again to delete.';
    RETURN;
END

-- ---- Step 2: delete (one transaction) ----------------------------------------
BEGIN TRANSACTION;

DECLARE @Machines TABLE (machine_id int PRIMARY KEY);
INSERT INTO @Machines SELECT DISTINCT machine_id FROM @Pms;

-- notifications (their read receipts go with them - ON DELETE CASCADE)
DELETE FROM [transactions].[notification_transaction]
WHERE entity_name = 'MachinePm' AND entity_id IN (SELECT machine_pm_id FROM @Pms);

-- PM jobs (their checklist ticks go with them - ON DELETE CASCADE)
DELETE FROM [transactions].[machine_pm_transaction]
WHERE machine_pm_id IN (SELECT machine_pm_id FROM @Pms);
DECLARE @deletedPms int = @@ROWCOUNT;

-- the plans (their checklist items go with them - ON DELETE CASCADE); a plan still used elsewhere is kept
DECLARE @deletedPlans int = 0;
IF @DeletePlans = 1
BEGIN
    DELETE FROM [masters].[maintenance_checklist_master]
    WHERE checklist_id IN (SELECT checklist_id FROM @Plans)
      AND NOT EXISTS (SELECT 1 FROM [transactions].[machine_pm_transaction] x WHERE x.checklist_id = [masters].[maintenance_checklist_master].checklist_id)
      AND NOT EXISTS (SELECT 1 FROM [transactions].[mold_pm_transaction] y WHERE y.checklist_id = [masters].[maintenance_checklist_master].checklist_id)
      AND NOT EXISTS (SELECT 1 FROM [masters].[maintenance_checklist_master] z WHERE z.source_checklist_id = [masters].[maintenance_checklist_master].checklist_id);
    SET @deletedPlans = @@ROWCOUNT;
END

-- the machines, recalculated from what remains (automatic PMs only, as the system does)
UPDATE m
SET last_maintenance_date = (SELECT MAX(p.completed_date) FROM [transactions].[machine_pm_transaction] p
                             WHERE p.machine_id = m.machine_id AND p.status = 'Completed' AND p.schedule_type = 'Automatic'),
    next_maintenance_date = (SELECT MIN(p.scheduled_date) FROM [transactions].[machine_pm_transaction] p
                             WHERE p.machine_id = m.machine_id AND p.status IN ('Scheduled', 'In Progress') AND p.schedule_type = 'Automatic'),
    operational_status = CASE WHEN m.operational_status = 'Maintenance'
                                   AND NOT EXISTS (SELECT 1 FROM [transactions].[machine_pm_transaction] p
                                                   WHERE p.machine_id = m.machine_id AND p.status = 'In Progress')
                              THEN 'Running' ELSE m.operational_status END,
    updated_at = SYSUTCDATETIME()
FROM [masters].[machine_master] m
WHERE m.machine_id IN (SELECT machine_id FROM @Machines);

COMMIT TRANSACTION;

PRINT CAST(@deletedPms AS varchar(10)) + ' PM job(s) and ' + CAST(@deletedPlans AS varchar(10)) + ' plan(s) deleted; machine dates recalculated.';

SELECT m.machine_code, m.machine_name, m.operational_status, m.last_maintenance_date, m.next_maintenance_date
FROM [masters].[machine_master] m
WHERE m.machine_id IN (SELECT machine_id FROM @Machines);
GO
