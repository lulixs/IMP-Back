namespace Imp.Api.Data;

using Imp.Api.Models;
using System.Data.Common;
using Dapper;
using System.ComponentModel;

public class AuditLog {
    public static Task WriteAsync(DbConnection connection, AuditEntry entry,
        DbTransaction? transaction = null, CancellationToken ct = default) {
    
        const string sql = $"""
            INSERT INTO AUDIT_LOG (id, timestamp, employee_id, is_elevated, device_id, action_type, target_type, target_id, target_label, details)
            VALUES (@Id, @Timestamp, @EmployeeId, @IsElevated, @ActionType, @TargetType, @TargetId, @TargetLabel, @Details)
        """;

        return connection.ExecuteAsync(
            new CommandDefinition(sql, entry, transaction, cancellationToken: ct));
      
    }

    public static bool MatchActionTarget(AuditAction action, AuditTarget target)
    {
        switch(target)
		{	
			case AuditTarget.BOX:
				return action is not (AuditAction.ACTIVATED or AuditAction.DEACTIVATED);
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