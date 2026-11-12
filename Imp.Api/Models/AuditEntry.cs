using Imp.Api.Data;

namespace Imp.Api.Models;

public class AuditEntry {
    public int Id { get; set; }
    public string Timestamp { get; set; } = "";
    public int EmployeeId { get; set; }
    public bool IsElevated { get; set; }
    public int DeviceId { get; set; }
    // Required, because an unset enum is quietly 0 (ADDED and BOX) and would log the wrong thing.
    public required AuditAction ActionType { get; set; }
    public required AuditTarget TargetType { get; set; }
    public int TargetId { get; set; }
    public string TargetLabel { get; set; } = "";
    public string? Details { get; set; }
}
