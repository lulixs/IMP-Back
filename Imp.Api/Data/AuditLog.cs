namespace Imp.Api.Data;

using Imp.Api.Models;
using System.Data.Common;
using Dapper;

public static class AuditLog {
    public static Task WriteAsync(DbConnection connection, AuditEntry entry,
        DbTransaction? transaction = null, CancellationToken ct = default) {

			if (!MatchActionTarget(entry.ActionType, entry.TargetType)) {
				// Unsure of how we want program to respond
				throw new InvalidOperationException($"Action ({entry.ActionType}) cannot be applied to Target ({entry.TargetType})");
    		}
    
        const string sql = """
            INSERT INTO AUDIT_LOG (employee_id, is_elevated, device_id, action_type, target_type, target_id, target_label, details)
            VALUES (@EmployeeId, @IsElevated, @DeviceId, @ActionType, @TargetType, @TargetId, @TargetLabel, @Details)
        """;

        return connection.ExecuteAsync(
            new CommandDefinition (
				sql, 
				new {
					entry.EmployeeId,
					entry.IsElevated,
					entry.DeviceId,
					ActionType = entry.ActionType.ToString(),
					TargetType = entry.TargetType.ToString(),
					entry.TargetId,
					entry.TargetLabel,
					entry.Details
				}, 
				transaction, 
				cancellationToken: ct
			)
		);
      
    }

    public static bool MatchActionTarget(AuditAction action, AuditTarget target)
    {
        switch(target)
		{	
			case AuditTarget.BOX:
				return action is AuditAction.ADDED or AuditAction.EDITED or AuditAction.DELETED or AuditAction.RESTORED or AuditAction.PERMANENTLY_DELETED or AuditAction.RACKED or AuditAction.UNRACKED;
			case AuditTarget.BUILDING:
				return action is AuditAction.ADDED or AuditAction.PERMANENTLY_DELETED;
			case AuditTarget.CATEGORY:
				return action is AuditAction.ADDED or AuditAction.EDITED or AuditAction.PERMANENTLY_DELETED;
			case AuditTarget.EMPLOYEE:
				return action is AuditAction.ADDED or AuditAction.EDITED or AuditAction.ACTIVATED or AuditAction.DEACTIVATED;
			case AuditTarget.MASTER:
				return action is AuditAction.EDITED;
			default:
				throw new Exception("Unknown Target Type");
		}
	}
}