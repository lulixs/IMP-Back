using System.Data;
using System.Text.Json;
using Dapper;
using Imp.Api.Data;
using Imp.Api.Models;
using Imp.Api.Tests.Support;
using MySqlConnector;
using Xunit;

namespace Imp.Api.Tests;

// Tests for AuditLog.WriteAsync against the real MySQL container (cd db && docker compose up -d).
// If the database is not running, every test here is skipped, not failed.
// See TestDatabase.cs for how each test gets its own employee and tablet and cleans up after itself.
//
// Rules these tests come from: schema.sql and IMP_ERD_v2.6.1 (AUDIT_LOG section).
public class AuditLogWriteTests {
    // Test data

    private static string Emoji(int count) => string.Concat(Enumerable.Repeat("🎄", count));

    // target_label is VARCHAR(100). MySQL counts characters, not bytes or C# string length,
    // so 100 emoji (200 in C#, 400 bytes) still fit.
    public static TheoryData<string> LabelsAtTheLimit => new() { new string('a', 100), Emoji(100) };
    public static TheoryData<string> LabelsOverTheLimit => new() { new string('a', 101), Emoji(101) };

    // Writing one entry

    // Every field lands in its own column. The device and action columns sit next to each
    // other in the INSERT, so this is the test that catches them being mixed up.
    [DatabaseFact]
    public async Task Write_StoresEveryFieldInItsOwnColumn() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.IsElevated = true;
        entry.ActionType = AuditAction.EDITED;
        entry.TargetType = AuditTarget.CATEGORY;
        entry.TargetId = 42;
        entry.TargetLabel = "Coats";
        entry.Details = """{"name":{"before":"Coats","after":"Jackets"}}""";

        await db.WriteAsync(entry);

        var row = Assert.Single(await db.RowsAsync());
        Assert.Equal(db.EmployeeId, row.EmployeeId);
        Assert.True(row.IsElevated);
        Assert.Equal(db.DeviceId, row.DeviceId);
        Assert.Equal("EDITED", row.ActionType);
        Assert.Equal("CATEGORY", row.TargetType);
        Assert.Equal(42, row.TargetId);
        Assert.Equal("Coats", row.TargetLabel);
        using var details = JsonDocument.Parse(row.Details!);
        Assert.Equal("Jackets", details.RootElement.GetProperty("name").GetProperty("after").GetString());
    }

    // The three plain numbers must not get swapped with each other either.
    [DatabaseFact]
    public async Task Write_KeepsEmployeeDeviceAndTargetIdsApart() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.TargetId = 987_654;

        await db.WriteAsync(entry);

        var row = Assert.Single(await db.RowsAsync());
        Assert.Equal(db.EmployeeId, row.EmployeeId);
        Assert.Equal(db.DeviceId, row.DeviceId);
        Assert.Equal(987_654, row.TargetId);
        Assert.NotEqual(row.DeviceId, row.TargetId);
    }

    // IsElevated is stored as written, true and false.
    [DatabaseTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Write_IsElevated_RoundTrips(bool isElevated) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.IsElevated = isElevated;

        await db.WriteAsync(entry);

        Assert.Equal(isElevated, Assert.Single(await db.RowsAsync()).IsElevated);
    }

    // Id and Timestamp on the entry are not inserted. The database picks both.
    // (They exist on AuditEntry so the same class can be used to read rows back.)
    [DatabaseFact]
    public async Task Write_IgnoresIdAndTimestampSetOnTheEntry() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Id = 777_777_777;
        entry.Timestamp = "1999-01-01 00:00:00";

        await db.WriteAsync(entry);

        var row = Assert.Single(await db.RowsAsync());
        Assert.NotEqual(777_777_777, row.Id);
        Assert.True(row.Timestamp.Year >= 2025, $"Timestamp was {row.Timestamp:O}");
    }

    // The ERD says timestamps are stored in UTC. A wrong server time zone would show up here.
    [DatabaseFact]
    public async Task Write_SetsTimestampToTheCurrentUtcTime() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();

        await db.WriteAsync(db.NewEntry());

        var row = Assert.Single(await db.RowsAsync());
        Assert.InRange(row.Timestamp, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(2));
    }

    // WriteAsync reads the entry and changes nothing on it.
    [DatabaseFact]
    public async Task Write_DoesNotChangeTheEntry() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = """{"a":1}""";
        var before = JsonSerializer.Serialize(entry);

        await db.WriteAsync(entry);

        Assert.Equal(before, JsonSerializer.Serialize(entry));
    }

    // Ids

    // Entries get increasing ids, oldest first.
    [DatabaseFact]
    public async Task Write_TwoEntries_GetIncreasingIds() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var first = db.NewEntry();
        first.TargetLabel = "first";
        var second = db.NewEntry();
        second.TargetLabel = "second";

        await db.WriteAsync(first);
        await db.WriteAsync(second);

        var rows = await db.RowsAsync();
        Assert.Equal(new[] { "first", "second" }, rows.Select(r => r.TargetLabel));
        Assert.True(rows[1].Id > rows[0].Id);
    }

    // The ERD says ids are never reused. An id used by a rolled back entry is not handed out again.
    [DatabaseFact]
    public async Task Write_IdOfARolledBackEntry_IsNotReused() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        await db.WriteAsync(db.NewEntry());
        var firstId = Assert.Single(await db.RowsAsync()).Id;
        await db.RollbackAsync();

        await db.BeginAsync();
        await db.WriteAsync(db.NewEntry());

        var secondId = Assert.Single(await db.RowsAsync()).Id;
        Assert.True(secondId > firstId, $"Second id {secondId} was not greater than {firstId}.");
    }

    // Writing the same entry twice makes two rows. Nothing is merged or skipped.
    [DatabaseFact]
    public async Task Write_SameEntryTwice_MakesTwoRows() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();

        await db.WriteAsync(entry);
        await db.WriteAsync(entry);

        var rows = await db.RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].Id, rows[1].Id);
    }

    // Action and target pairs

    // All 17 pairs the ERD allows are stored, as exactly their names.
    [DatabaseTheory]
    [MemberData(nameof(ErdRules.ValidPairsData), MemberType = typeof(ErdRules))]
    public async Task Write_AllowedPair_StoresBothNames(AuditAction action, AuditTarget target) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.ActionType = action;
        entry.TargetType = target;

        await db.WriteAsync(entry);

        var row = Assert.Single(await db.RowsAsync());
        Assert.Equal(action.ToString(), row.ActionType);
        Assert.Equal(target.ToString(), row.TargetType);
    }

    // The ERD says the backend rejects any pairing it does not list, for example a building
    // being RACKED or the Master account being DELETED. Nothing is written for these.
    // This fails today because WriteAsync does not call MatchActionTarget itself.
    // If every caller is meant to check first, say so and move this test to the callers.
    [DatabaseTheory]
    [MemberData(nameof(ErdRules.InvalidPairsData), MemberType = typeof(ErdRules))]
    public async Task Write_PairNotInTheErd_IsRejectedAndNothingIsWritten(AuditAction action, AuditTarget target) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.ActionType = action;
        entry.TargetType = target;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.NotNull(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // A number that is not one of the enum values turns into a string like "99" that the
    // database's ENUM column does not accept. Nothing is written.
    [DatabaseTheory]
    [InlineData(99, (int)AuditTarget.BOX)]
    [InlineData(-1, (int)AuditTarget.BOX)]
    [InlineData((int)AuditAction.ADDED, 99)]
    [InlineData((int)AuditAction.ADDED, -1)]
    [InlineData(99, 99)]
    public async Task Write_UndefinedEnumValue_IsRejectedAndNothingIsWritten(int rawAction, int rawTarget) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.ActionType = (AuditAction)rawAction;
        entry.TargetType = (AuditTarget)rawTarget;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.NotNull(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // The ERD says the first account made at setup is logged as adding itself,
    // so the employee and the target are the same person.
    [DatabaseFact]
    public async Task Write_EmployeeAddingThemself_IsAccepted() {
        await using var db = await TestDatabase.CreateAsync();
        var newEmployee = await db.CreateEmployeeAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.EmployeeId = newEmployee;
        entry.TargetType = AuditTarget.EMPLOYEE;
        entry.ActionType = AuditAction.ADDED;
        entry.TargetId = newEmployee;
        entry.TargetLabel = "First Account";

        await db.WriteAsync(entry);

        var row = Assert.Single(await db.RowsAsync(employeeId: newEmployee));
        Assert.Equal(row.EmployeeId, row.TargetId);
    }

    // Details (a JSON column)

    // No details is stored as a real NULL, not as an empty string.
    [DatabaseFact]
    public async Task Details_Null_IsStoredAsSqlNull() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = null;

        await db.WriteAsync(entry);

        Assert.Null(Assert.Single(await db.RowsAsync()).Details);
    }

    // The text "null" is valid JSON, and is a value in its own right. It is not the same as SQL NULL.
    [DatabaseFact]
    public async Task Details_JsonNullText_IsNotTheSameAsSqlNull() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = "null";

        await db.WriteAsync(entry);

        Assert.Equal("null", Assert.Single(await db.RowsAsync()).Details);
    }

    // Any valid JSON is stored and comes back with the same meaning. MySQL may change spacing
    // and the order of keys, so this compares the parsed JSON, not the text.
    [DatabaseTheory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"a":1}""")]
    [InlineData("\"text\"")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("""[1,2,{"x":null}]""")]
    [InlineData("""{"before":{"fullness":"HALF"},"after":{"fullness":"FULL"}}""")]
    public async Task Details_ValidJson_IsStoredWithTheSameMeaning(string json) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = json;

        await db.WriteAsync(entry);

        var stored = Assert.Single(await db.RowsAsync()).Details;
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(stored!);
        Assert.True(JsonEqual(expected.RootElement, actual.RootElement), $"Stored: {stored}");
    }

    // The column is JSON, so text that is not valid JSON is refused by the database.
    // That includes an empty string and plain words. Nothing is written.
    [DatabaseTheory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("{'a':1}")]
    [InlineData("""{"a":}""")]
    [InlineData("""{"a":1}}""")]
    [InlineData("undefined")]
    public async Task Details_NotValidJson_IsRejectedAndNothingIsWritten(string notJson) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = notJson;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // Emoji and accented letters survive, whether written directly or as \u escapes.
    [DatabaseFact]
    public async Task Details_Unicode_RoundTrips() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = """{"direct":"🎄 Café 日本語","escaped":"🎄"}""";

        await db.WriteAsync(entry);

        using var stored = JsonDocument.Parse(Assert.Single(await db.RowsAsync()).Details!);
        Assert.Equal("🎄 Café 日本語", stored.RootElement.GetProperty("direct").GetString());
        Assert.Equal("🎄", stored.RootElement.GetProperty("escaped").GetString());
    }

    // A big details value (200,000 characters) is stored in full, not cut off.
    [DatabaseFact]
    public async Task Details_Large_IsStoredInFull() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.Details = "{\"notes\":\"" + new string('a', 200_000) + "\"}";

        await db.WriteAsync(entry);

        using var stored = JsonDocument.Parse(Assert.Single(await db.RowsAsync()).Details!);
        Assert.Equal(200_000, stored.RootElement.GetProperty("notes").GetString()!.Length);
    }

    // The label (VARCHAR(100), NOT NULL)

    // Names and box numbers, plus awkward text. All are stored exactly as given.
    // The SQL injection string is stored as plain text, and the table is still there afterwards
    // (the read at the end would fail if it had been dropped).
    [DatabaseTheory]
    [InlineData("Coats")]
    [InlineData("#6789")]
    [InlineData("O'Brien")]
    [InlineData("say \"hi\"")]
    [InlineData(@"back\slash")]
    [InlineData("100% sure_")]
    [InlineData("line1\nline2")]
    [InlineData("tab\there")]
    [InlineData("  padded  ")]
    [InlineData("'); DROP TABLE AUDIT_LOG; --")]
    [InlineData("@Details")]
    [InlineData("Café 日本語 🎄")]
    [InlineData("🎄")]
    public async Task TargetLabel_AnyText_IsStoredExactly(string label) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.TargetLabel = label;

        await db.WriteAsync(entry);

        Assert.Equal(label, Assert.Single(await db.RowsAsync()).TargetLabel);
    }

    // Exactly 100 characters fit, whether they are plain letters or emoji.
    [DatabaseTheory]
    [MemberData(nameof(LabelsAtTheLimit))]
    public async Task TargetLabel_AtTheLimit_IsAccepted(string label) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.TargetLabel = label;

        await db.WriteAsync(entry);

        Assert.Equal(label, Assert.Single(await db.RowsAsync()).TargetLabel);
    }

    // 101 characters are refused. The database must not quietly cut the label short,
    // because the log is a record of what happened.
    [DatabaseTheory]
    [MemberData(nameof(LabelsOverTheLimit))]
    public async Task TargetLabel_OverTheLimit_IsRejectedAndNothingIsWritten(string label) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.TargetLabel = label;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // The label is required.
    [DatabaseFact]
    public async Task TargetLabel_Null_IsRejectedAndNothingIsWritten() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.TargetLabel = null!;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // Employee, device and target ids

    // The employee must exist (foreign key). Nothing is written otherwise.
    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task EmployeeId_NotInTheDatabase_IsRejected(int missingEmployeeId) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.EmployeeId = missingEmployeeId;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Empty(await db.RowsAsync(employeeId: missingEmployeeId));
    }

    // The tablet must exist (foreign key). Nothing is written otherwise.
    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task DeviceId_NotInTheDatabase_IsRejected(int missingDeviceId) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.DeviceId = missingDeviceId;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // target_id is deliberately not a foreign key (ERD D-047), so entries survive permanent
    // deletes. Any number is accepted, including ids of records that no longer exist.
    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(999_999)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task TargetId_AnyNumber_IsAccepted(int targetId) {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.ActionType = AuditAction.PERMANENTLY_DELETED;
        entry.TargetType = AuditTarget.BUILDING;
        entry.TargetId = targetId;

        await db.WriteAsync(entry);

        Assert.Equal(targetId, Assert.Single(await db.RowsAsync()).TargetId);
    }

    // The log is a permanent record, so an employee or tablet that has entries cannot be deleted.
    [DatabaseFact]
    public async Task Employee_WithAuditEntries_CannotBeDeleted() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        await db.WriteAsync(db.NewEntry());

        var exception = await Record.ExceptionAsync(
            () => db.ExecuteAsync("DELETE FROM EMPLOYEE_ACCOUNT WHERE id = @id", new { id = db.EmployeeId }));

        Assert.IsAssignableFrom<MySqlException>(exception);
    }

    [DatabaseFact]
    public async Task Device_WithAuditEntries_CannotBeDeleted() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        await db.WriteAsync(db.NewEntry());

        var exception = await Record.ExceptionAsync(
            () => db.ExecuteAsync("DELETE FROM DEVICE WHERE id = @id", new { id = db.DeviceId }));

        Assert.IsAssignableFrom<MySqlException>(exception);
    }

    // Transactions

    // Until the transaction commits, nobody else sees the entry. After it commits, everyone does.
    [DatabaseFact]
    public async Task Commit_MakesTheEntryVisibleToOtherConnections() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        await db.BeginAsync();

        await db.WriteAsync(db.NewEntry());

        Assert.Empty(await db.RowsAsync(on: other));
        await db.CommitAsync();
        Assert.Single(await db.RowsAsync(on: other));
    }

    // Rolling back removes the entry.
    [DatabaseFact]
    public async Task Rollback_RemovesTheEntry() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        await db.BeginAsync();
        await db.WriteAsync(db.NewEntry());

        await db.RollbackAsync();

        Assert.Empty(await db.RowsAsync(on: other));
        Assert.Empty(await db.RowsAsync());
    }

    // The point of passing the transaction in: if the change being logged fails afterwards,
    // the log entry is rolled back with it. Here the second building has the same name as the
    // first, which the database refuses.
    [DatabaseFact]
    public async Task AuditEntryThenFailingChange_RollsBackBoth() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        var buildingName = "test-building-" + Guid.NewGuid().ToString("N");
        await db.BeginAsync();
        await db.WriteAsync(db.NewEntry());
        await db.ExecuteAsync("INSERT INTO BUILDING (name) VALUES (@name)", new { name = buildingName });

        var exception = await Record.ExceptionAsync(
            () => db.ExecuteAsync("INSERT INTO BUILDING (name) VALUES (@name)", new { name = buildingName }));
        await db.RollbackAsync();

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Empty(await db.RowsAsync(on: other));
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM BUILDING WHERE name = @name", new { name = buildingName }, other));
    }

    // The other way round: the change succeeds, then the log write fails (unknown employee).
    // After the rollback the change is gone too. A change must never exist without its log entry.
    [DatabaseFact]
    public async Task ChangeThenFailingAuditEntry_RollsBackBoth() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        var buildingName = "test-building-" + Guid.NewGuid().ToString("N");
        await db.BeginAsync();
        await db.ExecuteAsync("INSERT INTO BUILDING (name) VALUES (@name)", new { name = buildingName });
        var entry = db.NewEntry();
        entry.EmployeeId = int.MaxValue;

        var exception = await Record.ExceptionAsync(() => db.WriteAsync(entry));
        await db.RollbackAsync();

        Assert.IsAssignableFrom<MySqlException>(exception);
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM BUILDING WHERE name = @name", new { name = buildingName }, other));
    }

    // One failed write does not ruin the transaction. Entries written before and after it
    // are kept, and the failed one leaves nothing behind.
    [DatabaseFact]
    public async Task FailedWrite_LeavesTheTransactionUsable() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        await db.BeginAsync();
        var before = db.NewEntry();
        before.TargetLabel = "before";
        var bad = db.NewEntry();
        bad.TargetLabel = "bad";
        bad.EmployeeId = int.MaxValue;
        var after = db.NewEntry();
        after.TargetLabel = "after";

        await db.WriteAsync(before);
        var exception = await Record.ExceptionAsync(() => db.WriteAsync(bad));
        await db.WriteAsync(after);
        await db.CommitAsync();

        Assert.IsAssignableFrom<MySqlException>(exception);
        var rows = await db.RowsAsync(on: other);
        Assert.Equal(new[] { "before", "after" }, rows.Select(r => r.TargetLabel));
    }

    // With no transaction, the entry is saved straight away.
    [DatabaseFact]
    public async Task NoTransaction_SavesImmediately() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();

        await AuditLog.WriteAsync(db.Connection, db.NewEntry());

        Assert.Single(await db.RowsAsync(on: other));
    }

    // If the connection has a transaction open and the caller forgets to pass it in,
    // MySqlConnector throws. It does not quietly write the entry outside the transaction,
    // which would break the "both or neither" guarantee.
    [DatabaseFact]
    public async Task TransactionOpenButNotPassedIn_Throws() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();

        var exception = await Record.ExceptionAsync(() => AuditLog.WriteAsync(db.Connection, db.NewEntry()));

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // A transaction that belongs to a different connection is refused.
    [DatabaseFact]
    public async Task TransactionFromAnotherConnection_Throws() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        var otherTransaction = await db.BeginOnAsync(other);

        var exception = await Record.ExceptionAsync(
            () => AuditLog.WriteAsync(db.Connection, db.NewEntry(), otherTransaction));

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // A transaction that has already been committed cannot be used to write more entries.
    [DatabaseFact]
    public async Task AlreadyCommittedTransaction_Throws() {
        await using var db = await TestDatabase.CreateAsync();
        var finished = await db.BeginAsync();
        await db.CommitAsync();

        var exception = await Record.ExceptionAsync(
            () => AuditLog.WriteAsync(db.Connection, db.NewEntry(), finished));

        Assert.NotNull(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // Bad input and broken connections

    // No entry at all is an error. (WriteAsync is not an async method, so today this throws
    // right away instead of returning a failed Task. This test accepts either.
    // An ArgumentNullException would be a clearer error than the NullReferenceException you get now.)
    [DatabaseFact]
    public async Task NullEntry_Throws() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();

        var exception = await Record.ExceptionAsync(() => AuditLog.WriteAsync(db.Connection, null!, db.Transaction));

        Assert.NotNull(exception);
        Assert.Empty(await db.RowsAsync());
    }

    // No connection at all is an error too.
    [DatabaseFact]
    public async Task NullConnection_Throws() {
        await using var db = await TestDatabase.CreateAsync();

        var exception = await Record.ExceptionAsync(() => AuditLog.WriteAsync(null!, db.NewEntry()));

        Assert.NotNull(exception);
    }

    // A closed connection is fine: Dapper opens it for the insert and closes it again.
    [DatabaseFact]
    public async Task ClosedConnection_IsOpenedForTheWriteAndClosedAgain() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        await db.Connection.CloseAsync();

        await AuditLog.WriteAsync(db.Connection, db.NewEntry());

        Assert.Equal(ConnectionState.Closed, db.Connection.State);
        Assert.Single(await db.RowsAsync(on: other));
    }

    // A connection that has been disposed cannot be used, and the error is not swallowed.
    [DatabaseFact]
    public async Task DisposedConnection_Throws() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        var doomed = await db.OpenAnotherAsync();
        await doomed.DisposeAsync();

        var exception = await Record.ExceptionAsync(() => AuditLog.WriteAsync(doomed, db.NewEntry()));

        Assert.NotNull(exception);
        Assert.Empty(await db.RowsAsync(on: other));
    }

    // A token that is already cancelled stops the write before it happens.
    // (Accepts the MySqlException MySqlConnector uses when the cancel reaches the server.)
    [DatabaseFact]
    public async Task CancelledToken_StopsTheWrite() {
        await using var db = await TestDatabase.CreateAsync();
        var other = await db.OpenAnotherAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var exception = await Record.ExceptionAsync(
            () => AuditLog.WriteAsync(db.Connection, db.NewEntry(), null, cancelled.Token));

        Assert.True(exception is OperationCanceledException or MySqlException,
            $"Expected a cancellation error but got: {exception?.GetType().Name ?? "no error"}");
        Assert.Empty(await db.RowsAsync(on: other));
    }

    // 50 writes at once, each on its own connection (the way 50 simultaneous requests would).
    // Every entry arrives, each with its own id.
    [DatabaseFact]
    public async Task ManyWritesAtOnce_AllArriveWithUniqueIds() {
        await using var db = await TestDatabase.CreateAsync();
        var factory = db.NewFactory();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(async i => {
            await using var connection = await factory.OpenConnectionAsync();
            var entry = db.NewEntry();
            entry.TargetLabel = $"parallel-{i}";
            await AuditLog.WriteAsync(connection, entry);
        }));

        var rows = await db.RowsAsync();
        Assert.Equal(50, rows.Count);
        Assert.Equal(50, rows.Select(r => r.Id).Distinct().Count());
        Assert.Equal(50, rows.Select(r => r.TargetLabel).Distinct().Count());
    }

    // Reading rows back into AuditEntry

    // A stored row can be read back into AuditEntry, with the names turned back into enums.
    // AuditEntry.Timestamp is a string. If this test throws while reading, a DATETIME column
    // cannot be read into a string by Dapper, and Timestamp should become a DateTime.
    [DatabaseFact]
    public async Task StoredRow_CanBeReadBackIntoAuditEntry() {
        await using var db = await TestDatabase.CreateAsync();
        await db.BeginAsync();
        var entry = db.NewEntry();
        entry.ActionType = AuditAction.RACKED;
        entry.TargetType = AuditTarget.BOX;
        entry.TargetLabel = "#6789";
        entry.IsElevated = true;
        await db.WriteAsync(entry);

        var read = await db.Connection.QuerySingleAsync<AuditEntry>(new CommandDefinition(
            """
            SELECT id AS Id, `timestamp` AS Timestamp, employee_id AS EmployeeId, is_elevated AS IsElevated,
                   device_id AS DeviceId, action_type AS ActionType, target_type AS TargetType,
                   target_id AS TargetId, target_label AS TargetLabel, details AS Details
            FROM AUDIT_LOG WHERE employee_id = @employeeId
            """,
            new { employeeId = db.EmployeeId }, db.Transaction));

        Assert.Equal(AuditAction.RACKED, read.ActionType);
        Assert.Equal(AuditTarget.BOX, read.TargetType);
        Assert.Equal("#6789", read.TargetLabel);
        Assert.True(read.IsElevated);
        Assert.NotEqual("", read.Timestamp);
        Assert.True(read.Id > 0);
    }

    // Helpers

    // Compares two parsed JSON values by meaning, ignoring spacing and the order of keys
    // (MySQL rewrites both). Numbers are compared as written, so use whole numbers in tests.
    private static bool JsonEqual(JsonElement a, JsonElement b) {
        if (a.ValueKind != b.ValueKind) return false;

        switch (a.ValueKind) {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                return left.Count == right.Count
                    && left.All(pair => right.TryGetValue(pair.Key, out var value) && JsonEqual(pair.Value, value));
            case JsonValueKind.Array:
                var leftItems = a.EnumerateArray().ToList();
                var rightItems = b.EnumerateArray().ToList();
                return leftItems.Count == rightItems.Count
                    && leftItems.Zip(rightItems).All(pair => JsonEqual(pair.First, pair.Second));
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();
            default:
                return true; // true, false and null: the kind already matched
        }
    }
}
