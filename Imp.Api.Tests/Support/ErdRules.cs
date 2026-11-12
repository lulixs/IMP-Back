using Imp.Api.Data;
using Xunit;

namespace Imp.Api.Tests.Support;

// The rules the tests check the code against. They are copied from IMP_ERD_v2.6.1, section 3,
// "Allowed actions for each target type", NOT from AuditLog.cs, so a mistake in the code cannot
// also be a mistake in the expected answer. If the ERD changes, change this table.
public static class ErdRules {
    public static readonly IReadOnlyDictionary<AuditTarget, AuditAction[]> AllowedActions =
        new Dictionary<AuditTarget, AuditAction[]> {
            [AuditTarget.BOX] = new[] {
                AuditAction.ADDED, AuditAction.EDITED, AuditAction.RACKED, AuditAction.UNRACKED,
                AuditAction.DELETED, AuditAction.RESTORED, AuditAction.PERMANENTLY_DELETED
            },
            [AuditTarget.CATEGORY] = new[] {
                AuditAction.ADDED, AuditAction.EDITED, AuditAction.PERMANENTLY_DELETED
            },
            [AuditTarget.BUILDING] = new[] {
                AuditAction.ADDED, AuditAction.PERMANENTLY_DELETED
            },
            [AuditTarget.EMPLOYEE] = new[] {
                AuditAction.ADDED, AuditAction.EDITED, AuditAction.ACTIVATED, AuditAction.DEACTIVATED
            },
            [AuditTarget.MASTER] = new[] {
                AuditAction.EDITED
            },
        };

    public static bool IsAllowed(AuditAction action, AuditTarget target) =>
        AllowedActions[target].Contains(action);

    // Every action paired with every target: 9 x 5 = 45 pairs.
    public static IEnumerable<(AuditAction Action, AuditTarget Target)> AllPairs() {
        foreach (var target in Enum.GetValues<AuditTarget>()) {
            foreach (var action in Enum.GetValues<AuditAction>()) {
                yield return (action, target);
            }
        }
    }

    // Data for [MemberData(..., MemberType = typeof(ErdRules))]

    public static TheoryData<AuditAction, AuditTarget, bool> AllPairsData() {
        var data = new TheoryData<AuditAction, AuditTarget, bool>();
        foreach (var (action, target) in AllPairs()) {
            data.Add(action, target, IsAllowed(action, target));
        }
        return data;
    }

    public static TheoryData<AuditAction, AuditTarget> ValidPairsData() {
        var data = new TheoryData<AuditAction, AuditTarget>();
        foreach (var (action, target) in AllPairs()) {
            if (IsAllowed(action, target)) data.Add(action, target);
        }
        return data;
    }

    public static TheoryData<AuditAction, AuditTarget> InvalidPairsData() {
        var data = new TheoryData<AuditAction, AuditTarget>();
        foreach (var (action, target) in AllPairs()) {
            if (!IsAllowed(action, target)) data.Add(action, target);
        }
        return data;
    }
}
