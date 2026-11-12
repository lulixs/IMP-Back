using Imp.Api.Data;
using Imp.Api.Models;
using Xunit;

namespace Imp.Api.Tests;

// Tests for the AuditEntry class itself. No Docker needed.
public class AuditEntryTests {
    // A new entry starts empty: ids 0, empty text, no details.
    [Fact]
    public void NewEntry_StartsEmpty() {
        var entry = new AuditEntry();

        Assert.Equal(0, entry.Id);
        Assert.Equal("", entry.Timestamp);
        Assert.Equal(0, entry.EmployeeId);
        Assert.False(entry.IsElevated);
        Assert.Equal(0, entry.DeviceId);
        Assert.Equal(0, entry.TargetId);
        Assert.Equal("", entry.TargetLabel);
        Assert.Null(entry.Details);
    }

    // PITFALL, written down on purpose: an entry whose action and target were never set quietly
    // becomes "ADDED a BOX", because the first member of each enum is 0. It also passes
    // MatchActionTarget. Forgetting to set them logs the wrong thing instead of failing.
    // If AuditEntry is changed to require them (C# "required"), delete this test.
    [Fact]
    public void NewEntry_UnsetActionAndTarget_SilentlyBecomeAddedAndBox() {
        var entry = new AuditEntry();

        Assert.Equal(AuditAction.ADDED, entry.ActionType);
        Assert.Equal(AuditTarget.BOX, entry.TargetType);
        Assert.True(AuditLog.MatchActionTarget(entry.ActionType, entry.TargetType));
    }
}
