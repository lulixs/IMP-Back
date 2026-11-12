using Imp.Api.Data;
using Imp.Api.Models;
using Xunit;

namespace Imp.Api.Tests;

// Tests for the AuditEntry class itself. No Docker needed.
// ActionType and TargetType are "required", so an entry without them doesn't compile.
public class AuditEntryTests {
    // A new entry starts empty apart from its action and target: ids 0, empty text, no details.
    [Fact]
    public void NewEntry_StartsEmpty() {
        var entry = new AuditEntry { ActionType = AuditAction.EDITED, TargetType = AuditTarget.CATEGORY };

        Assert.Equal(0, entry.Id);
        Assert.Equal("", entry.Timestamp);
        Assert.Equal(0, entry.EmployeeId);
        Assert.False(entry.IsElevated);
        Assert.Equal(0, entry.DeviceId);
        Assert.Equal(0, entry.TargetId);
        Assert.Equal("", entry.TargetLabel);
        Assert.Null(entry.Details);
    }
}
