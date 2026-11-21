using System.Reflection;
using System.Security.Claims;
using Imp.Api.Controllers;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Imp.Api.Tests;

// DELETE /api/boxes/{id} (docs/api/boxes.md). Boxes come from BoxTestData, so the only audit rows
// for the test employee are the ones a delete writes.
public class BoxDeleteTests {
    private const string NoSuchBox = "This box doesn't exist.";
    private const string DeletedBox = "This box was deleted.";

    // Setup

    private static BoxController ControllerFor(int employeeId, int deviceId) {
        var identity = new ClaimsIdentity(new[] {
            new Claim(TokenClaims.Kind, TokenClaims.SessionKind),
            new Claim(TokenClaims.EmployeeId, employeeId.ToString()),
            new Claim(TokenClaims.DeviceId, deviceId.ToString()),
        }, "test");
        return new BoxController(new MySqlConnectionFactory(TestDatabase.ConnectionString)) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static BoxController ControllerFor(TestDatabase db) => ControllerFor(db.EmployeeId, db.DeviceId);

    private static async Task DeleteAsync(TestDatabase db, int id) =>
        Assert.IsType<NoContentResult>(await ControllerFor(db).DeleteBox(id, CancellationToken.None));

    private static Task<ApiError> RefusedAsync(TestDatabase db, int id) =>
        Assert.ThrowsAsync<ApiError>(() => ControllerFor(db).DeleteBox(id, CancellationToken.None));

    private static async Task<Box> GetAsync(TestDatabase db, int id) =>
        Assert.IsType<Box>(Assert.IsType<OkObjectResult>(
            await ControllerFor(db).GetBox(id, CancellationToken.None)).Value);

    // raw delete columns
    private sealed class DeleteColumns {
        public DateTime? DeletedAt { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public int? LastUpdatedBy { get; set; }
    }

    private static async Task<DeleteColumns> ColumnsAsync(TestDatabase db, int id) {
        var rows = await Dapper.SqlMapper.QueryAsync<DeleteColumns>(db.Connection,
            "SELECT deleted_at, deleted_by, last_updated_at, last_updated_by FROM BOX WHERE id = @id", new { id });
        return Assert.Single(rows);
    }

    // Routing

    [Fact]
    public void DeleteBox_IsADeleteOfABoxNumber() {
        var delete = typeof(BoxController).GetMethod(nameof(BoxController.DeleteBox))!
            .GetCustomAttribute<HttpDeleteAttribute>();

        Assert.Equal("{id:int}", delete!.Template);
    }

    [Fact]
    public void DeleteBox_DoesNotNeedMasterMode() {
        var method = typeof(BoxController).GetMethod(nameof(BoxController.DeleteBox))!;

        Assert.Null(method.GetCustomAttribute<RequireMasterModeAttribute>());
        Assert.Null(typeof(BoxController).GetCustomAttribute<RequireMasterModeAttribute>());
    }

    // 204

    [DatabaseFact]
    public async Task Delete_Returns204_AndSetsAllFourColumns() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        await DeleteAsync(db, id);

        var row = await ColumnsAsync(db, id);
        Assert.InRange(row.DeletedAt!.Value, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(db.EmployeeId, row.DeletedBy);
        Assert.Equal(row.DeletedAt, row.LastUpdatedAt);
        Assert.Equal(db.EmployeeId, row.LastUpdatedBy);
    }

    [DatabaseFact]
    public async Task Delete_BoxStillOpensWithDeletedFields() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        await DeleteAsync(db, id);
        var box = await GetAsync(db, id);

        Assert.NotNull(box.DeletedAt);
        Assert.Equal(DateTimeKind.Utc, box.DeletedAt!.Value.Kind);
        Assert.Equal(db.EmployeeId, box.DeletedBy!.Id);
        Assert.Equal(box.DeletedAt, box.LastUpdatedAt);
        Assert.Equal(db.EmployeeId, box.LastUpdatedBy!.Id);
    }

    [DatabaseFact]
    public async Task Delete_HidesTheBoxFromSearch() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var buildingId = await data.BuildingAsync();
        var kept = await data.BoxAsync(categoryId, buildingId);
        var gone = await data.BoxAsync(categoryId, buildingId);

        await DeleteAsync(db, gone);
        var result = Assert.IsType<OkObjectResult>(await ControllerFor(db).GetBoxes(
            null, categoryId.ToString(), null, null, CancellationToken.None));
        var boxes = (IEnumerable<Box>)result.Value!.GetType().GetProperty("boxes")!.GetValue(result.Value)!;

        Assert.Equal(new[] { kept }, boxes.Select(b => b.Id));
    }

    [DatabaseFact]
    public async Task Delete_AnUnrackedBox_Works() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync(isRacked: false);

        await DeleteAsync(db, id);

        Assert.NotNull((await GetAsync(db, id)).DeletedAt);
    }

    [DatabaseFact]
    public async Task Delete_ABoxSomeoneElseEdited_TakesOverLastUpdated() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var other = await db.CreateEmployeeAsync();
        var id = await data.LoneBoxAsync();
        await data.EditAsync(id, other);

        await DeleteAsync(db, id);

        Assert.Equal(db.EmployeeId, (await ColumnsAsync(db, id)).LastUpdatedBy);
    }

    [DatabaseFact]
    public async Task Delete_LeavesTheOtherFieldsAlone() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var before = await GetAsync(db, id);

        await DeleteAsync(db, id);
        var after = await GetAsync(db, id);

        Assert.Equal(before.Category.Id, after.Category.Id);
        Assert.Equal(before.Building.Id, after.Building.Id);
        Assert.Equal(before.ShelfLetter, after.ShelfLetter);
        Assert.Equal(before.ShelfLevel, after.ShelfLevel);
        Assert.Equal(before.Fullness, after.Fullness);
        Assert.Equal(before.IsRacked, after.IsRacked);
        Assert.Equal(before.RackedSince, after.RackedSince);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(before.CreatedBy.Id, after.CreatedBy.Id);
    }

    // Audit

    [DatabaseFact]
    public async Task Delete_WritesOneBoxDeletedEntry() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        await DeleteAsync(db, id);

        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("DELETED", entry.ActionType);
        Assert.Equal("BOX", entry.TargetType);
        Assert.Equal(id, entry.TargetId);
        Assert.Equal($"#{id}", entry.TargetLabel);
        Assert.Equal(db.DeviceId, entry.DeviceId);
        Assert.False(entry.IsElevated);
        Assert.Null(entry.Details);
    }

    [DatabaseFact]
    public async Task Delete_InMasterMode_TheEntryIsElevated() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        await db.ExecuteAsync("UPDATE DEVICE SET is_elevated = TRUE WHERE id = @id", new { id = db.DeviceId });
        var id = await data.LoneBoxAsync();

        await DeleteAsync(db, id);

        Assert.True(Assert.Single(await db.RowsAsync()).IsElevated);
    }

    [DatabaseFact]
    public async Task Delete_WhenTheAuditEntryFails_TheBoxIsNotDeleted() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var missingDevice = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM DEVICE");

        var error = await Assert.ThrowsAsync<MySqlConnector.MySqlException>(
            () => ControllerFor(db.EmployeeId, missingDevice).DeleteBox(id, CancellationToken.None));

        // fails on the audit insert, after the UPDATE ran
        Assert.Contains("AUDIT_LOG", error.Message);
        var row = await ColumnsAsync(db, id);
        Assert.Null(row.DeletedAt);
        Assert.Null(row.LastUpdatedAt);
    }

    // 404

    [DatabaseFact]
    public async Task MissingBox_Is404() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var error = await RefusedAsync(db, await data.MissingBoxIdAsync());

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(NoSuchBox, error.Message);
        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task AlreadyDeleted_Is404_AndKeepsTheFirstDelete() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var first = await db.CreateEmployeeAsync();
        var id = await data.LoneBoxAsync();
        await data.SoftDeleteAsync(id, first);
        var before = await ColumnsAsync(db, id);

        var error = await RefusedAsync(db, id);

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(DeletedBox, error.Message);
        var after = await ColumnsAsync(db, id);
        Assert.Equal(before.DeletedAt, after.DeletedAt);
        Assert.Equal(first, after.DeletedBy);
        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task DeletingTwice_SecondIs404() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        await DeleteAsync(db, id);
        var error = await RefusedAsync(db, id);

        Assert.Equal(DeletedBox, error.Message);
        Assert.Single(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task SimultaneousDeletes_OneWins_OneGets404() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        async Task<int> StatusOf() {
            try { await ControllerFor(db).DeleteBox(id, CancellationToken.None); return 204; }
            catch (ApiError e) { return e.StatusCode; }
        }
        var statuses = await Task.WhenAll(StatusOf(), StatusOf());

        Assert.Equal(new[] { 204, 404 }, statuses.OrderBy(x => x));
        Assert.Single(await db.RowsAsync());
    }

    // Other endpoints after a delete

    [DatabaseFact]
    public async Task SavingADeletedBox_Is404() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var box = await GetAsync(db, id);

        await DeleteAsync(db, id);
        var request = new SaveBoxRequest {
            CategoryID = box.Category.Id, BuildingID = box.Building.Id, ShelfLetter = box.ShelfLetter,
            ShelfLevel = box.ShelfLevel, Fullness = "EMPTY", Notes = box.Notes, IsRacked = box.IsRacked,
            ResetRackedSince = false, LastUpdatedAt = box.LastUpdatedAt,
        };
        var error = await Assert.ThrowsAsync<ApiError>(
            () => ControllerFor(db).SaveBox(id, request, CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(DeletedBox, error.Message);
    }
}
