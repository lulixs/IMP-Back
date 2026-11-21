using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Imp.Api.Controllers;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Imp.Api.Tests;

// Tests for POST /api/boxes (docs/api/boxes.md). They call the controller directly, as the employee and tablet
// TestDatabase makes, with categories and buildings each test makes for itself (see BoxTestData). Every box a
// test creates is tracked and removed afterward.
public class BoxCreateTests {
    // The sentences the controller sends.
    private const string NoCategory = "Category is required.";
    private const string NoBuilding = "Building is required.";
    private const string BadShelfLetter = "Shelf letter must be one to three capital letters.";
    private const string BadShelfLevel = "Shelf level must be 1, 2, or 3.";
    private const string BadFullness = "Fullness must be FULL, HALF, or EMPTY.";
    private const string BadDate = "Racked since must be of the form yyyy-MM-dd.";
    private const string FutureDate = "The storage date cannot be in the future.";
    private const string CategoryGone = "That category no longer exists. Choose another.";
    private const string BuildingGone = "That building no longer exists. Choose another.";

    // Setup

    // Fails the test if the controller touches the database, to show a request was refused before any query.
    private sealed class NoDatabase : IDbConnectionFactory {
        public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The request should have been refused before opening a connection.");
    }

    // A controller that sees the request as coming from this employee, signed in on this tablet.
    private static BoxController ControllerFor(int employeeId, int deviceId, IDbConnectionFactory? factory = null) {
        var identity = new ClaimsIdentity(new[] {
            new Claim(TokenClaims.Kind, TokenClaims.SessionKind),
            new Claim(TokenClaims.EmployeeId, employeeId.ToString()),
            new Claim(TokenClaims.DeviceId, deviceId.ToString()),
        }, "test");
        return new BoxController(factory ?? new MySqlConnectionFactory(TestDatabase.ConnectionString)) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static BoxController ControllerFor(TestDatabase db) => ControllerFor(db.EmployeeId, db.DeviceId);

    // A controller that must refuse the request without opening a connection.
    private static BoxController Offline() => ControllerFor(1, 1, new NoDatabase());

    private static string TodayText => DateOnly.FromDateTime(StoreClock.Today()).ToString("yyyy-MM-dd");

    // A valid request. Change what the test cares about.
    private static NewBoxRequest Request(int categoryId = 1, int buildingId = 1, string? shelfLetter = "A",
            int? shelfLevel = 2, string? fullness = "FULL", string? notes = null, string? rackedSince = null) => new() {
        CategoryID = categoryId,
        BuildingID = buildingId,
        ShelfLetter = shelfLetter,
        ShelfLevel = shelfLevel,
        Fullness = fullness,
        Notes = notes,
        RackedSince = rackedSince ?? TodayText,
    };

    // Posts the request, checks for 201, tracks the new box for cleanup, and returns it.
    private static async Task<Box> CreateAsync(TestDatabase db, BoxTestData data, NewBoxRequest request) {
        var result = Assert.IsType<CreatedAtActionResult>(await ControllerFor(db).AddBox(request, CancellationToken.None));
        var box = Assert.IsType<Box>(result.Value);
        data.Track(box.Id);
        return box;
    }

    // A request with a category and building of its own.
    private static async Task<NewBoxRequest> ValidRequestAsync(BoxTestData data) =>
        Request(await data.CategoryAsync(), await data.BuildingAsync());

    private static async Task<ApiError> RefusedAsync(BoxController controller, NewBoxRequest request) =>
        await Assert.ThrowsAsync<ApiError>(() => controller.AddBox(request, CancellationToken.None));

    private static async Task AssertBadRequestAsync(NewBoxRequest request, string message) {
        var error = await RefusedAsync(Offline(), request);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(message, error.Message);
        Assert.Null(error.Code);
    }

    private static Task<int> BoxCountForAsync(TestDatabase db) =>
        db.ScalarAsync<int>("SELECT COUNT(*) FROM BOX WHERE created_by = @id", new { id = db.EmployeeId })!;

    // Routing and binding

    [Fact]
    public void AddBox_IsAPostToTheBoxesRoute() {
        var post = typeof(BoxController).GetMethod(nameof(BoxController.AddBox))!.GetCustomAttribute<HttpPostAttribute>();
        var route = typeof(BoxController).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(post);
        Assert.Null(post!.Template);
        Assert.Equal("api/boxes", route!.Template);
    }

    [Fact]
    public void Request_BindsTheContractsJson() {
        // The contract's example request, read the way ASP.NET Core reads request bodies (camelCase, any capitals).
        const string json = """
            {
              "categoryId": 1,
              "buildingId": 2,
              "shelfLetter": "A",
              "shelfLevel": 2,
              "fullness": "FULL",
              "notes": "Mostly adult sizes.",
              "rackedSince": "2026-11-09"
            }
            """;

        var request = JsonSerializer.Deserialize<NewBoxRequest>(json, JsonSerializerOptions.Web)!;

        Assert.Equal(1, request.CategoryID);
        Assert.Equal(2, request.BuildingID);
        Assert.Equal("A", request.ShelfLetter);
        Assert.Equal(2, request.ShelfLevel);
        Assert.Equal("FULL", request.Fullness);
        Assert.Equal("Mostly adult sizes.", request.Notes);
        Assert.Equal("2026-11-09", request.RackedSince);
    }

    [Fact]
    public void Request_MissingFieldsArriveAsNull() {
        var request = JsonSerializer.Deserialize<NewBoxRequest>("{}", JsonSerializerOptions.Web)!;

        Assert.Null(request.CategoryID);
        Assert.Null(request.BuildingID);
        Assert.Null(request.ShelfLetter);
        Assert.Null(request.ShelfLevel);
        Assert.Null(request.Fullness);
        Assert.Null(request.Notes);
        Assert.Null(request.RackedSince);
    }

    // Refused before any query (400)

    [Fact]
    public async Task EmptyBody_IsRefusedForTheCategoryFirst() {
        await AssertBadRequestAsync(new NewBoxRequest(), NoCategory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task Category_MissingOrNegative_IsRefused(int? categoryId) {
        var request = Request();
        request.CategoryID = categoryId;
        await AssertBadRequestAsync(request, NoCategory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task Building_MissingOrNegative_IsRefused(int? buildingId) {
        var request = Request();
        request.BuildingID = buildingId;
        await AssertBadRequestAsync(request, NoBuilding);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("Ab")]
    [InlineData("ABCD")]
    [InlineData("A1")]
    [InlineData("1")]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData("A\n")]
    [InlineData("A-B")]
    [InlineData("É")]
    public async Task ShelfLetter_BreakingTheRule_IsRefused(string? shelfLetter) {
        await AssertBadRequestAsync(Request(shelfLetter: shelfLetter), BadShelfLetter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task ShelfLevel_OutsideOneToThree_IsRefused(int? shelfLevel) {
        await AssertBadRequestAsync(Request(shelfLevel: shelfLevel), BadShelfLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("full")]
    [InlineData("Full")]
    [InlineData("FULL ")]
    [InlineData("QUARTER")]
    [InlineData("0")]
    public async Task Fullness_NotOneOfTheThree_IsRefused(string? fullness) {
        await AssertBadRequestAsync(Request(fullness: fullness), BadFullness);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("11/09/2026")]
    [InlineData("2026/11/09")]
    [InlineData("2026-11-9")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("2026-11-09T00:00:00")]
    [InlineData(" 2026-11-09")]
    public async Task RackedSince_NotADate_IsRefused(string rackedSince) {
        await AssertBadRequestAsync(Request(rackedSince: rackedSince), BadDate);
    }

    [Fact]
    public async Task RackedSince_Missing_IsRefused() {
        var request = Request();
        request.RackedSince = null;
        await AssertBadRequestAsync(request, BadDate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public async Task RackedSince_InTheFuture_IsRefused(int daysAhead) {
        var future = DateOnly.FromDateTime(StoreClock.Today()).AddDays(daysAhead).ToString("yyyy-MM-dd");
        await AssertBadRequestAsync(Request(rackedSince: future), FutureDate);
    }

    // Refused after checking the database (400)

    [DatabaseFact]
    public async Task Category_ThatDoesNotExist_IsRefused() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var missing = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM CATEGORY");
        var request = Request(missing, await data.BuildingAsync());

        var error = await RefusedAsync(ControllerFor(db), request);

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(CategoryGone, error.Message);
        Assert.Equal(0, await BoxCountForAsync(db));
        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task Category_Zero_IsRefusedAsNoLongerExisting() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var error = await RefusedAsync(ControllerFor(db), Request(0, await data.BuildingAsync()));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(CategoryGone, error.Message);
    }

    [DatabaseFact]
    public async Task Building_ThatDoesNotExist_IsRefused() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var missing = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM BUILDING");
        var request = Request(await data.CategoryAsync(), missing);

        var error = await RefusedAsync(ControllerFor(db), request);

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(BuildingGone, error.Message);
        Assert.Equal(0, await BoxCountForAsync(db));
        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task Category_DeletedWhileTheFormWasOpen_IsRefused() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        await db.ExecuteAsync("DELETE FROM CATEGORY WHERE id = @categoryId", new { categoryId });

        var error = await RefusedAsync(ControllerFor(db), Request(categoryId, await data.BuildingAsync()));

        Assert.Equal(CategoryGone, error.Message);
    }

    [DatabaseFact]
    public async Task BothMissing_TheCategoryIsReportedFirst() {
        await using var db = await TestDatabase.CreateAsync();
        var missingCategory = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM CATEGORY");
        var missingBuilding = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM BUILDING");

        var error = await RefusedAsync(ControllerFor(db), Request(missingCategory, missingBuilding));

        Assert.Equal(CategoryGone, error.Message);
    }

    // Created (201)

    [DatabaseFact]
    public async Task Create_Returns201PointingToGetBox() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var result = Assert.IsType<CreatedAtActionResult>(
            await ControllerFor(db).AddBox(await ValidRequestAsync(data), CancellationToken.None));
        var box = Assert.IsType<Box>(result.Value);
        data.Track(box.Id);

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        Assert.Equal(nameof(BoxController.GetBox), result.ActionName);
        Assert.Equal(box.Id, result.RouteValues!["id"]);
    }

    [DatabaseFact]
    public async Task Create_ReturnsEveryField() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryName = BoxTestData.RandomName("test-category-");
        var buildingName = BoxTestData.RandomName("test-building-");
        var categoryId = await data.CategoryAsync(categoryName, "🎄");
        var buildingId = await data.BuildingAsync(buildingName);
        var employeeName = await db.ScalarAsync<string>(
            "SELECT name FROM EMPLOYEE_ACCOUNT WHERE id = @id", new { id = db.EmployeeId });
        var before = DateTime.UtcNow.AddSeconds(-5);

        var box = await CreateAsync(db, data, Request(categoryId, buildingId, "BC", 3, "HALF", "Mostly adult sizes."));

        Assert.True(box.Id > 0);
        Assert.Equal(categoryId, box.Category.Id);
        Assert.Equal(categoryName, box.Category.Name);
        Assert.Equal("🎄", box.Category.Symbol);
        Assert.Equal(buildingId, box.Building.Id);
        Assert.Equal(buildingName, box.Building.Name);
        Assert.Equal("BC", box.ShelfLetter);
        Assert.Equal(3, box.ShelfLevel);
        Assert.Equal("HALF", box.Fullness);
        Assert.Equal("Mostly adult sizes.", box.Notes);
        Assert.True(box.IsRacked);
        Assert.Equal(DateOnly.FromDateTime(StoreClock.Today()), box.RackedSince);
        Assert.Equal(0, box.DaysStored);
        Assert.False(box.StoredTooLong);
        Assert.Equal(DateTimeKind.Utc, box.CreatedAt.Kind);
        Assert.InRange(box.CreatedAt, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(db.EmployeeId, box.CreatedBy.Id);
        Assert.Equal(employeeName, box.CreatedBy.Name);
        Assert.Null(box.LastUpdatedAt);
        Assert.Null(box.LastUpdatedBy);
        Assert.Null(box.DeletedAt);
        Assert.Null(box.DeletedBy);
    }

    [DatabaseFact]
    public async Task Create_ReplyMatchesGetBox() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var created = await CreateAsync(db, data, await ValidRequestAsync(data));
        var fetched = Assert.IsType<Box>(Assert.IsType<OkObjectResult>(
            await ControllerFor(db).GetBox(created.Id, CancellationToken.None)).Value);

        Assert.Equal(JsonSerializer.Serialize(fetched, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(created, JsonSerializerOptions.Web));
    }

    [DatabaseFact]
    public async Task Create_SavesTheRowAsSent() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);
        request.ShelfLetter = "ZZZ";
        request.ShelfLevel = 1;
        request.Fullness = "EMPTY";
        request.RackedSince = "2026-01-15";

        var box = await CreateAsync(db, data, request);
        var row = await db.ScalarAsync<string>("""
            SELECT CONCAT_WS('|', category_id, building_id, shelf_letter, shelf_level, fullness, is_racked,
                              racked_since, created_by, last_updated_at IS NULL, deleted_at IS NULL)
            FROM BOX WHERE id = @id
            """, new { id = box.Id });

        Assert.Equal($"{request.CategoryID}|{request.BuildingID}|ZZZ|1|EMPTY|1|2026-01-15|{db.EmployeeId}|1|1", row);
    }

    [DatabaseTheory]
    [InlineData("EMPTY")]
    [InlineData("HALF")]
    [InlineData("FULL")]
    public async Task Create_AcceptsEveryFullness(string fullness) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);
        request.Fullness = fullness;

        Assert.Equal(fullness, (await CreateAsync(db, data, request)).Fullness);
    }

    [DatabaseTheory]
    [InlineData("A", 1)]
    [InlineData("AB", 2)]
    [InlineData("ABC", 3)]
    public async Task Create_AcceptsEveryShelfLetterLengthAndLevel(string shelfLetter, int shelfLevel) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);
        request.ShelfLetter = shelfLetter;
        request.ShelfLevel = shelfLevel;

        var box = await CreateAsync(db, data, request);

        Assert.Equal(shelfLetter, box.ShelfLetter);
        Assert.Equal(shelfLevel, box.ShelfLevel);
    }

    [DatabaseFact]
    public async Task Create_TodayIsAllowed() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var box = await CreateAsync(db, data, await ValidRequestAsync(data));

        Assert.Equal(DateOnly.FromDateTime(StoreClock.Today()), box.RackedSince);
        Assert.Equal(0, box.DaysStored);
    }

    [DatabaseFact]
    public async Task Create_AnEarlierDate_CountsDaysStoredFromIt() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);
        var earlier = DateOnly.FromDateTime(StoreClock.Today()).AddDays(-30);
        request.RackedSince = earlier.ToString("yyyy-MM-dd");

        var box = await CreateAsync(db, data, request);

        Assert.Equal(earlier, box.RackedSince);
        Assert.Equal(30, box.DaysStored);
        Assert.True(box.IsRacked);
    }

    [DatabaseFact]
    public async Task Create_ADatePastTheThreshold_IsStoredTooLong() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var threshold = await data.AlertThresholdDaysAsync();
        var request = await ValidRequestAsync(data);
        request.RackedSince = DateOnly.FromDateTime(StoreClock.Today()).AddDays(-threshold).ToString("yyyy-MM-dd");

        var box = await CreateAsync(db, data, request);

        Assert.Equal(threshold, box.DaysStored);
        Assert.True(box.StoredTooLong);
    }

    [DatabaseFact]
    public async Task Create_ADayShortOfTheThreshold_IsNotStoredTooLong() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var threshold = await data.AlertThresholdDaysAsync();
        var request = await ValidRequestAsync(data);
        request.RackedSince = DateOnly.FromDateTime(StoreClock.Today()).AddDays(-(threshold - 1)).ToString("yyyy-MM-dd");

        var box = await CreateAsync(db, data, request);

        Assert.False(box.StoredTooLong);
    }

    // Notes

    [DatabaseTheory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\t\n ", null)]
    [InlineData("Lights", "Lights")]
    [InlineData("  Lights  ", "Lights")]
    [InlineData("\nLights\t", "Lights")]
    [InlineData("Two  spaces inside stay", "Two  spaces inside stay")]
    [InlineData("Line one\nLine two", "Line one\nLine two")]
    public async Task Create_NotesAreTrimmedAndBlankBecomesNull(string? sent, string? saved) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);
        request.Notes = sent;

        var box = await CreateAsync(db, data, request);
        var stored = await db.ScalarAsync<string?>("SELECT notes FROM BOX WHERE id = @id", new { id = box.Id });

        Assert.Equal(saved, box.Notes);
        Assert.Equal(saved, stored);
    }

    [DatabaseFact]
    public async Task Create_NotesKeepEmojiAndQuotes() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);
        request.Notes = "Santa's 🎅 \"good\" lights; DROP TABLE BOX; --";

        var box = await CreateAsync(db, data, request);

        Assert.Equal(request.Notes, box.Notes);
        Assert.True(await db.ScalarAsync<bool>("SELECT COUNT(*) > 0 FROM BOX"));
    }

    // Audit log

    [DatabaseFact]
    public async Task Create_WritesOneBoxAddedEntry() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var box = await CreateAsync(db, data, await ValidRequestAsync(data));
        var rows = await db.RowsAsync();

        var entry = Assert.Single(rows);
        Assert.Equal(db.EmployeeId, entry.EmployeeId);
        Assert.Equal(db.DeviceId, entry.DeviceId);
        Assert.False(entry.IsElevated);
        Assert.Equal("ADDED", entry.ActionType);
        Assert.Equal("BOX", entry.TargetType);
        Assert.Equal(box.Id, entry.TargetId);
        Assert.Equal($"#{box.Id}", entry.TargetLabel);
        Assert.Null(entry.Details);
    }

    [DatabaseFact]
    public async Task Create_InMasterMode_TheEntryIsElevated() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        await db.ExecuteAsync("UPDATE DEVICE SET is_elevated = TRUE WHERE id = @id", new { id = db.DeviceId });

        await CreateAsync(db, data, await ValidRequestAsync(data));

        Assert.True(Assert.Single(await db.RowsAsync()).IsElevated);
    }

    [DatabaseFact]
    public async Task Create_WhenTheAuditEntryFails_NoBoxIsSaved() {
        // A tablet id with no DEVICE row makes the audit insert break its foreign key after the box insert has run.
        // The transaction must undo the box too.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var missingDevice = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM DEVICE");
        var request = await ValidRequestAsync(data);

        var error = await Assert.ThrowsAsync<MySqlConnector.MySqlException>(
            () => ControllerFor(db.EmployeeId, missingDevice).AddBox(request, CancellationToken.None));

        // The failure is the audit entry's tablet, so the box insert before it did run.
        Assert.Contains("AUDIT_LOG", error.Message);
        Assert.Contains("device_id", error.Message);
        Assert.Equal(0, await BoxCountForAsync(db));
        Assert.Empty(await db.RowsAsync());
    }

    // More than one box

    [DatabaseFact]
    public async Task Create_TwoBoxes_GetTheirOwnNumbersAndEntries() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);

        var first = await CreateAsync(db, data, request);
        var second = await CreateAsync(db, data, request);

        Assert.True(second.Id > first.Id);
        var rows = await db.RowsAsync();
        Assert.Equal(new[] { first.Id, second.Id }, rows.Select(r => r.TargetId));
        Assert.All(rows, r => Assert.Equal("ADDED", r.ActionType));
    }

    [DatabaseFact]
    public async Task Create_TheNewBoxShowsUpInSearch() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var request = await ValidRequestAsync(data);

        var box = await CreateAsync(db, data, request);
        var result = Assert.IsType<OkObjectResult>(await ControllerFor(db).GetBoxes(
            null, request.CategoryID.ToString(), null, null, CancellationToken.None));
        var boxes = (IEnumerable<Box>)result.Value!.GetType().GetProperty("boxes")!.GetValue(result.Value)!;

        Assert.Equal(new[] { box.Id }, boxes.Select(b => b.Id));
    }
}
