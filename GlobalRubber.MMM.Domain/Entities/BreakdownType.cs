using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities
{
    public class BreakdownType : AuditableEntity
    {
        public int BreakdownTypeId { get; set; }

        public string BreakdownTypeCode { get; set; } = string.Empty;

        public string BreakdownTypeName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

    }
}