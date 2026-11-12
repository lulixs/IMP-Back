using Imp.Api.Data;
using Xunit;

namespace Imp.Api.Tests;

// Checks that the C# enums and the database agree. Needs the MySQL container running
// (skipped otherwise). These catch drift: someone adds a value on one side and forgets the other.
public class AuditLogSchemaTests {
    // AUDIT_LOG.action_type allows exactly the values in the AuditAction enum.
    [DatabaseFact]
    public async Task ActionTypeColumn_AllowsExactlyTheAuditActionNames() {
        await using var db = await TestDatabase.CreateAsync();

        var columnValues = await db.EnumValuesAsync("action_type");

        Assert.Equal(
            Enum.GetNames<AuditAction>().Order(StringComparer.Ordinal),
            columnValues.Order(StringComparer.Ordinal));
    }

    // AUDIT_LOG.target_type allows exactly the values in the AuditTarget enum.
    [DatabaseFact]
    public async Task TargetTypeColumn_AllowsExactlyTheAuditTargetNames() {
        await using var db = await TestDatabase.CreateAsync();

        var columnValues = await db.EnumValuesAsync("target_type");

        Assert.Equal(
            Enum.GetNames<AuditTarget>().Order(StringComparer.Ordinal),
            columnValues.Order(StringComparer.Ordinal));
    }

    // The ERD says DATETIME values are stored in UTC, and the audit timestamp defaults to
    // CURRENT_TIMESTAMP, which follows the database session's time zone. If the server is set to
    // another zone, this fails, and every audit timestamp would be off by that many hours.
    [DatabaseFact]
    public async Task DatabaseClock_IsUtc() {
        await using var db = await TestDatabase.CreateAsync();

        var secondsOff = await db.ScalarAsync<long>("SELECT TIMESTAMPDIFF(SECOND, UTC_TIMESTAMP(), NOW())");

        Assert.InRange(secondsOff, -1, 1);
    }
}
