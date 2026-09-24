namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// module_code values exactly as seeded in database/scripts/010_seed_data.sql (security.
/// module_master) - transcribed, not invented, so controllers reference these instead of
/// typing the string literal (and risking a typo) each time.
/// </summary>
public static class ModuleCodes
{
    public const string Dashboard = "DASHBOARD";

    public const string MasterMachine = "MASTER_MACHINE";
    public const string MasterMold = "MASTER_MOLD";
    public const string MasterProduct = "MASTER_PRODUCT";
    public const string MasterDepartment = "MASTER_DEPARTMENT";
    public const string MasterEmployee = "MASTER_EMPLOYEE";
    public const string MasterVendor = "MASTER_VENDOR";
    public const string MasterSparePart = "MASTER_SPARE_PART";
    public const string MasterMaintenanceType = "MASTER_MAINTENANCE_TYPE";
    public const string MasterBreakdownType = "MASTER_BREAKDOWN_TYPE";
    public const string MasterMaintenanceChecklist = "MASTER_MAINTENANCE_CHECKLIST";

    public const string TrnProductionEntry = "TRN_PRODUCTION_ENTRY";
    public const string TrnMachinePm = "TRN_MACHINE_PM";
    public const string TrnMoldPm = "TRN_MOLD_PM";
    public const string TrnMachineBreakdown = "TRN_MACHINE_BREAKDOWN";
    public const string TrnWorkOrder = "TRN_WORK_ORDER";
    public const string TrnSparePartUsage = "TRN_SPARE_PART_USAGE";

    public const string RptMachine = "RPT_MACHINE";
    public const string RptMold = "RPT_MOLD";
    public const string RptMaintenance = "RPT_MAINTENANCE";
    public const string RptBreakdown = "RPT_BREAKDOWN";
    public const string RptSparePart = "RPT_SPARE_PART";

    public const string AdminUsers = "ADMIN_USERS";
    public const string AdminRoles = "ADMIN_ROLES";
    public const string AdminMySettings = "ADMIN_MY_SETTINGS";
    public const string AdminSystemSettings = "ADMIN_SYSTEM_SETTINGS";
    public const string AdminAuditLog = "ADMIN_AUDIT_LOG";
    public const string AdminLogs = "ADMIN_LOGS";
}
