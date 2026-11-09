namespace Imp.Api.Models;

using Imp.Api.Data;

public class AuditEntry 
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = "";
    public int EmployeeId { get; set; }
    public bool IsElevated { get; set; }
    public AuditAction ActionType { get; set; }
    public AuditTarget TargetType { get; set; }
    public int TargetId { get; set; }
    public string TargetLabel { get; set; } = "";
    public string? Details { get; set; }
}