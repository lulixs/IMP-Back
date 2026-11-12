using Imp.Api.Data;
using Imp.Api.Tests.Support;
using Xunit;

namespace Imp.Api.Tests;

// Tests for AuditLog.MatchActionTarget. No Docker or MySQL needed.
// The expected answers come from the ERD table in Support/ErdRules.cs, not from the code under test.
public class AuditLogMatchTests {
    // The full matrix

    // Every action paired with every target (9 x 5 = 45). The 17 pairs the ERD lists are
    // allowed, the other 28 are not.
    [Theory]
    [MemberData(nameof(ErdRules.AllPairsData), MemberType = typeof(ErdRules))]
    public void MatchActionTarget_AgreesWithTheErdForEveryPair(AuditAction action, AuditTarget target, bool expected) {
        Assert.Equal(expected, AuditLog.MatchActionTarget(action, target));
    }

    // Guards the table in ErdRules itself, so a typo there cannot make a wrong test pass.
    [Fact]
    public void ErdTable_ListsSeventeenPairs() {
        Assert.Equal(17, ErdRules.AllowedActions.Values.Sum(actions => actions.Length));
    }

    // The code must also allow exactly 17 pairs, not just give the right answer for the ones we asked about.
    [Fact]
    public void MatchActionTarget_AllowsExactlySeventeenPairs() {
        var allowed = 0;
        foreach (var target in Enum.GetValues<AuditTarget>())
            foreach (var action in Enum.GetValues<AuditAction>())
                if (AuditLog.MatchActionTarget(action, target)) allowed++;

        Assert.Equal(17, allowed);
    }

    // If someone adds a target or an action to the enums, this fails until the ERD table
    // (and so the matrix above) is updated, instead of the new value slipping through untested.
    [Fact]
    public void ErdTable_CoversEveryTargetAndEveryAction() {
        Assert.Equal(
            Enum.GetValues<AuditTarget>().OrderBy(t => t),
            ErdRules.AllowedActions.Keys.OrderBy(t => t));

        var usedActions = ErdRules.AllowedActions.Values.SelectMany(a => a).Distinct().OrderBy(a => a);
        Assert.Equal(Enum.GetValues<AuditAction>().OrderBy(a => a), usedActions);
    }

    // The ERD's rules, one action at a time

    // For each action, which targets may it be used on? Alphabetical, joined with commas.
    // Reads like the ERD: only boxes are soft deleted and restored, only employees are
    // activated, the Master account is only ever edited, and so on.
    [Theory]
    [InlineData(AuditAction.ADDED, "BOX,BUILDING,CATEGORY,EMPLOYEE")]
    [InlineData(AuditAction.EDITED, "BOX,CATEGORY,EMPLOYEE,MASTER")]
    [InlineData(AuditAction.DELETED, "BOX")]
    [InlineData(AuditAction.RESTORED, "BOX")]
    [InlineData(AuditAction.PERMANENTLY_DELETED, "BOX,BUILDING,CATEGORY")]
    [InlineData(AuditAction.RACKED, "BOX")]
    [InlineData(AuditAction.UNRACKED, "BOX")]
    [InlineData(AuditAction.ACTIVATED, "EMPLOYEE")]
    [InlineData(AuditAction.DEACTIVATED, "EMPLOYEE")]
    public void MatchActionTarget_EachActionIsAllowedOnlyOnItsTargets(AuditAction action, string expectedTargets) {
        var targets = Enum.GetValues<AuditTarget>()
            .Where(target => AuditLog.MatchActionTarget(action, target))
            .Select(target => target.ToString())
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedTargets, string.Join(",", targets));
    }

    // Values that are not in the enums

    // A number that is not one of the five targets cannot be matched, so it throws.
    // It must throw for every action, valid or not.
    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void MatchActionTarget_UndefinedTarget_Throws(int rawTarget) {
        foreach (var action in Enum.GetValues<AuditAction>()) {
            var exception = Record.Exception(() => AuditLog.MatchActionTarget(action, (AuditTarget)rawTarget));

            Assert.NotNull(exception);
        }
    }

    // Both values undefined: still throws, never returns an answer.
    [Fact]
    public void MatchActionTarget_UndefinedTargetAndAction_Throws() {
        var exception = Record.Exception(() => AuditLog.MatchActionTarget((AuditAction)99, (AuditTarget)99));

        Assert.NotNull(exception);
    }

    // A number that is not one of the nine actions must never be allowed, for any target.
    // The ERD says the backend rejects every pairing it does not list.
    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void MatchActionTarget_UndefinedAction_IsNeverAllowed(int rawAction) {
        foreach (var target in Enum.GetValues<AuditTarget>()) {
            var allowed = AuditLog.MatchActionTarget((AuditAction)rawAction, target);

            Assert.False(allowed, $"Undefined action {rawAction} was allowed on {target}.");
        }
    }
}
