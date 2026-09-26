namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// document_type values exactly as seeded in database/scripts/010_seed_data.sql
/// (configuration.document_sequence_configuration) - transcribed, not invented. Only the types the
/// backend actually issues codes for so far are listed; add the others as their modules arrive.
/// </summary>
public static class DocumentTypes
{
    public const string User = "USER";
    public const string Department = "DEPARTMENT";
    public const string Employee = "EMPLOYEE";
    public const string Vendor = "VENDOR";
    public const string Product = "PRODUCT";
    public const string Machine = "MACHINE";
    public const string Mold = "MOLD";
    public const string MaintenanceType = "MAINTENANCE_TYPE";
    public const string BreakdownType = "BREAKDOWN_TYPE";
    public const string MaintenanceChecklist = "MAINTENANCE_CHECKLIST";
    public const string ProductionEntry = "PRODUCTION_ENTRY";
    public const string MachinePm = "MACHINE_PM";
    public const string MoldPm = "MOLD_PM";
    public const string SparePart = "SPARE_PART";
    public const string MachineBreakdown = "MACHINE_BREAKDOWN";
    public const string SparePartUsage = "SPARE_PART_USAGE";
}
