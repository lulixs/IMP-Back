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

// PUT /api/boxes/{id} (docs/api/boxes.md). Boxes come from BoxTestData, so the only audit rows
// for the test employee are the ones a save writes.
public class BoxSaveTests {
    private const string NoSuchBox = "This box doesn't exist.";
    private const string DeletedBox = "This box was deleted.";
    private const string Stale = "Someone else changed this box. Reload it and make your change again.";
    private const string BadRacked = "Racked must be true or false.";
    private const string BadReset = "Reset racked since must be true or false.";
    private const string CategoryGone = "That category no longer exists. Choose another.";
    private const string BuildingGone = "That building no longer exists. Choose another.";

    // Setup

    // fails if touched
    private sealed class NoDatabase : IDbConnectionFactory {
        public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The request should have been refused before opening a connection.");
    }

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

    private static DateOnly Today => DateOnly.FromDateTime(StoreClock.Today());

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd");

    // what the Box page sends unchanged
    private static SaveBoxRequest SaveFrom(Box box) => new() {
        CategoryID = box.Category.Id,
        BuildingID = box.Building.Id,
        ShelfLetter = box.ShelfLetter,
        ShelfLevel = box.ShelfLevel,
        Fullness = box.Fullness,
        Notes = box.Notes,
        IsRacked = box.IsRacked,
        ResetRackedSince = false,
        LastUpdatedAt = box.LastUpdatedAt,
    };

    private static async Task<Box> GetAsync(TestDatabase db, int id) =>
        Assert.IsType<Box>(Assert.IsType<OkObjectResult>(
            await ControllerFor(db).GetBox(id, CancellationToken.None)).Value);

    private static async Task<Box> SaveAsync(TestDatabase db, int id, SaveBoxRequest request) =>
        Assert.IsType<Box>(Assert.IsType<OkObjectResult>(
            await ControllerFor(db).SaveBox(id, request, CancellationToken.None)).Value);

    private static Task<ApiError> RefusedAsync(TestDatabase db, int id, SaveBoxRequest request) =>
        Assert.ThrowsAsync<ApiError>(() => ControllerFor(db).SaveBox(id, request, CancellationToken.None));

    // test box in its own named category + building
    private sealed record Setup(TestDatabase Db, BoxTestData Data, Box Box, string Category, string Building);

    private static async Task<Setup> BoxAsync(TestDatabase db, BoxTestData data, bool isRacked = true,
            DateOnly? rackedSince = null, string fullness = "FULL", string? notes = null) {
        var category = BoxTestData.RandomName("test-category-");
        var building = BoxTestData.RandomName("test-building-");
        var id = await data.BoxAsync(await data.CategoryAsync(category), await data.BuildingAsync(building),
            (rackedSince ?? Today.AddDays(-50)).ToDateTime(TimeOnly.MinValue), isRacked, "A", 1, fullness, notes);
        return new Setup(db, data, await GetAsync(db, id), category, building);
    }

    private static string Location(string building, string letter, int level) =>
        $"{building} · Shelf {letter}, Level {level}";

    // details -> (field, from, to)
    private static List<(string Field, string? From, string? To)> Changes(string? details) {
        Assert.NotNull(details);
        using var doc = JsonDocument.Parse(details!);
        return doc.RootElement.GetProperty("changes").EnumerateArray()
            .Select(c => (c.GetProperty("field").GetString()!, Text(c.GetProperty("from")), Text(c.GetProperty("to"))))
            .ToList();
    }

    private static string? Text(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

    // Routing and binding

    [Fact]
    public void SaveBox_IsAPutToABoxNumber() {
        var put = typeof(BoxController).GetMethod(nameof(BoxController.SaveBox))!.GetCustomAttribute<HttpPutAttribute>();

        Assert.Equal("{id:int}", put!.Template);
    }

    [Fact]
    public void Request_BindsTheContractsJson() {
        const string json = """
            {
              "categoryId": 1, "buildingId": 3, "shelfLetter": "C", "shelfLevel": 1, "fullness": "HALF",
              "notes": "Mostly adult sizes.", "isRacked": true, "resetRackedSince": false,
              "lastUpdatedAt": "2026-10-07T18:05:00Z"
            }
            """;

        var request = JsonSerializer.Deserialize<SaveBoxRequest>(json, JsonSerializerOptions.Web)!;

        Assert.Equal(1, request.CategoryID);
        Assert.Equal(3, request.BuildingID);
        Assert.Equal("C", request.ShelfLetter);
        Assert.Equal(1, request.ShelfLevel);
        Assert.Equal("HALF", request.Fullness);
        Assert.Equal("Mostly adult sizes.", request.Notes);
        Assert.True(request.IsRacked);
        Assert.False(request.ResetRackedSince);
        Assert.Equal(new DateTime(2026, 10, 7, 18, 5, 0, DateTimeKind.Utc), request.LastUpdatedAt);
        Assert.Equal(DateTimeKind.Utc, request.LastUpdatedAt!.Value.Kind);
    }

    [Fact]
    public void Request_NullAndMissingArriveAsNull() {
        var request = JsonSerializer.Deserialize<SaveBoxRequest>("""{"lastUpdatedAt": null}""", JsonSerializerOptions.Web)!;

        Assert.Null(request.LastUpdatedAt);
        Assert.Null(request.IsRacked);
        Assert.Null(request.ResetRackedSince);
    }

    [Fact]
    public void Request_LastUpdatedAtLeftOut_IsRefused() {
        // [JsonRequired]: null ok, missing -> 400 "The request body is invalid."
        const string json = """{"categoryId": 1, "isRacked": true, "resetRackedSince": false}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SaveBoxRequest>(json, JsonSerializerOptions.Web));
    }

    [Fact]
    public void Request_SwaggerPlaceholderIsNotADate() {
        // Program.cs example text. unreadable body -> 400 "The request body is invalid."
        const string json = """{"lastUpdatedAt": "copy from GET /api/boxes/{id}, or null"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SaveBoxRequest>(json, JsonSerializerOptions.Web));
    }

    // 400 before any query

    private static SaveBoxRequest Valid() => new() {
        CategoryID = 1, BuildingID = 1, ShelfLetter = "A", ShelfLevel = 1, Fullness = "FULL",
        IsRacked = true, ResetRackedSince = false,
    };

    private static async Task AssertOfflineBadRequestAsync(SaveBoxRequest request, string message) {
        var error = await Assert.ThrowsAsync<ApiError>(
            () => ControllerFor(1, 1, new NoDatabase()).SaveBox(1, request, CancellationToken.None));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task SharedRules_AreChecked() {
        // full rule coverage is in BoxCreateTests, same CheckFields
        var r = Valid(); r.CategoryID = null;
        await AssertOfflineBadRequestAsync(r, "Category is required.");
        r = Valid(); r.BuildingID = null;
        await AssertOfflineBadRequestAsync(r, "Building is required.");
        r = Valid(); r.ShelfLetter = "abcd";
        await AssertOfflineBadRequestAsync(r, "Shelf letter must be one to three capital letters.");
        r = Valid(); r.ShelfLevel = 4;
        await AssertOfflineBadRequestAsync(r, "Shelf level must be 1, 2, or 3.");
        r = Valid(); r.Fullness = "full";
        await AssertOfflineBadRequestAsync(r, "Fullness must be FULL, HALF, or EMPTY.");
    }

    [Fact]
    public async Task IsRacked_Missing_IsRefused() {
        var r = Valid(); r.IsRacked = null;
        await AssertOfflineBadRequestAsync(r, BadRacked);
    }

    [Fact]
    public async Task ResetRackedSince_Missing_IsRefused() {
        var r = Valid(); r.ResetRackedSince = null;
        await AssertOfflineBadRequestAsync(r, BadReset);
    }

    // 404

    [DatabaseFact]
    public async Task MissingBox_Is404() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);

        var error = await RefusedAsync(db, await data.MissingBoxIdAsync(), Valid());

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(NoSuchBox, error.Message);
    }

    [DatabaseFact]
    public async Task DeletedBox_Is404_AndUnchanged() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        await data.SoftDeleteAsync(s.Box.Id, db.EmployeeId);
        var deleted = await GetAsync(db, s.Box.Id);
        var request = SaveFrom(deleted);
        request.Fullness = "EMPTY";

        var error = await RefusedAsync(db, s.Box.Id, request);

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(DeletedBox, error.Message);
        Assert.Equal("FULL", (await GetAsync(db, s.Box.Id)).Fullness);
        Assert.Empty(await db.RowsAsync());
    }

    // 409 (D-174)

    [DatabaseFact]
    public async Task NeverEdited_NullMatches() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        Assert.Null(s.Box.LastUpdatedAt);
        var request = SaveFrom(s.Box);
        request.Fullness = "HALF";

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal("HALF", saved.Fullness);
    }

    [DatabaseFact]
    public async Task NeverEdited_SentATime_Is409() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.LastUpdatedAt = DateTime.UtcNow;
        request.Fullness = "HALF";

        var error = await RefusedAsync(db, s.Box.Id, request);

        Assert.Equal(409, error.StatusCode);
        Assert.Equal(Stale, error.Message);
    }

    [DatabaseFact]
    public async Task Edited_SentNull_Is409() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        await data.EditAsync(s.Box.Id, db.EmployeeId);
        var request = SaveFrom(s.Box);
        request.LastUpdatedAt = null;

        Assert.Equal(409, (await RefusedAsync(db, s.Box.Id, request)).StatusCode);
    }

    [DatabaseFact]
    public async Task Edited_SentTheCurrentTime_Saves() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        await data.EditAsync(s.Box.Id, db.EmployeeId);
        var request = SaveFrom(await GetAsync(db, s.Box.Id));
        request.Fullness = "EMPTY";

        Assert.Equal("EMPTY", (await SaveAsync(db, s.Box.Id, request)).Fullness);
    }

    [DatabaseFact]
    public async Task Edited_SentTheSameInstantInLocalTime_Saves() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        await data.EditAsync(s.Box.Id, db.EmployeeId);
        var request = SaveFrom(await GetAsync(db, s.Box.Id));
        request.LastUpdatedAt = request.LastUpdatedAt!.Value.ToLocalTime();
        request.Fullness = "EMPTY";

        Assert.Equal("EMPTY", (await SaveAsync(db, s.Box.Id, request)).Fullness);
    }

    [DatabaseFact]
    public async Task SecondTablet_WithAnOldCopy_Is409_AndNothingIsWritten() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var first = SaveFrom(s.Box);
        first.Fullness = "HALF";
        var second = SaveFrom(s.Box);
        second.Fullness = "EMPTY";
        second.Notes = "mine";

        await SaveAsync(db, s.Box.Id, first);
        var error = await RefusedAsync(db, s.Box.Id, second);

        Assert.Equal(409, error.StatusCode);
        var box = await GetAsync(db, s.Box.Id);
        Assert.Equal("HALF", box.Fullness);
        Assert.Null(box.Notes);
        Assert.Single(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task SavingAgain_WithTheReplysTime_Works() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.Fullness = "HALF";

        var once = await SaveAsync(db, s.Box.Id, request);
        var again = SaveFrom(once);
        again.Fullness = "EMPTY";
        var twice = await SaveAsync(db, s.Box.Id, again);

        Assert.Equal("EMPTY", twice.Fullness);
        Assert.Equal(2, (await db.RowsAsync()).Count);
    }

    [DatabaseFact]
    public async Task SimultaneousSaves_OneWins_OneGets409() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var a = SaveFrom(s.Box);
        a.Fullness = "HALF";
        var b = SaveFrom(s.Box);
        b.Fullness = "EMPTY";

        async Task<int> StatusOf(SaveBoxRequest r) {
            try { await ControllerFor(db).SaveBox(s.Box.Id, r, CancellationToken.None); return 200; }
            catch (ApiError e) { return e.StatusCode; }
        }
        var statuses = await Task.WhenAll(StatusOf(a), StatusOf(b));

        Assert.Equal(new[] { 200, 409 }, statuses.OrderBy(x => x));
        Assert.Single(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task Save_AlwaysMovesLastUpdatedAtForward() {
        // stored time ahead of now = a save earlier this second. next save must still differ
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        await db.ExecuteAsync("UPDATE BOX SET last_updated_at = UTC_TIMESTAMP() + INTERVAL 30 SECOND WHERE id = @id",
            new { id = s.Box.Id });
        var loaded = await GetAsync(db, s.Box.Id);
        var request = SaveFrom(loaded);
        request.Fullness = "HALF";

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(loaded.LastUpdatedAt!.Value.AddSeconds(1), saved.LastUpdatedAt);
    }

    [DatabaseFact]
    public async Task SameSecond_ThirdTabletWithACopyFromBetween_Is409() {
        // the gap from the review: saved, loaded by C, saved again in the same second, then C saves
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var first = SaveFrom(s.Box);
        first.Fullness = "HALF";
        var afterFirst = await SaveAsync(db, s.Box.Id, first);
        var copyC = await GetAsync(db, s.Box.Id);
        var second = SaveFrom(afterFirst);
        second.Fullness = "EMPTY";
        await SaveAsync(db, s.Box.Id, second);
        var fromC = SaveFrom(copyC);
        fromC.Notes = "from C";

        var error = await RefusedAsync(db, s.Box.Id, fromC);

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("EMPTY", (await GetAsync(db, s.Box.Id)).Fullness);
    }

    // 400 after loading

    [DatabaseFact]
    public async Task CategoryGone_Is400_AndUnchanged() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.CategoryID = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM CATEGORY");

        var error = await RefusedAsync(db, s.Box.Id, request);

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(CategoryGone, error.Message);
        Assert.Equal(s.Box.Category.Id, (await GetAsync(db, s.Box.Id)).Category.Id);
        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task BuildingGone_Is400_AndUnchanged() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.BuildingID = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM BUILDING");

        var error = await RefusedAsync(db, s.Box.Id, request);

        Assert.Equal(BuildingGone, error.Message);
        Assert.Empty(await db.RowsAsync());
    }

    // Nothing changed

    [DatabaseFact]
    public async Task NoChange_WritesNothing() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, notes: "Lights");

        var saved = await SaveAsync(db, s.Box.Id, SaveFrom(s.Box));

        Assert.Null(saved.LastUpdatedAt);
        Assert.Null(saved.LastUpdatedBy);
        Assert.Empty(await db.RowsAsync());
        Assert.Equal(JsonSerializer.Serialize(s.Box), JsonSerializer.Serialize(saved));
    }

    [DatabaseFact]
    public async Task NoChange_KeepsAnEarlierLastUpdatedAt() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        await data.EditAsync(s.Box.Id, db.EmployeeId);
        var loaded = await GetAsync(db, s.Box.Id);

        var saved = await SaveAsync(db, s.Box.Id, SaveFrom(loaded));

        Assert.Equal(loaded.LastUpdatedAt, saved.LastUpdatedAt);
        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task NoChange_NotesThatOnlyDifferBySpaces() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, notes: "Lights");
        var request = SaveFrom(s.Box);
        request.Notes = "  Lights \n";

        await SaveAsync(db, s.Box.Id, request);

        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseFact]
    public async Task NoChange_BlankNotesOnABoxWithNone() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.Notes = "   ";

        await SaveAsync(db, s.Box.Id, request);

        Assert.Empty(await db.RowsAsync());
    }

    [DatabaseTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoChange_ResetIsIgnoredUnlessReRacking(bool isRacked) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, isRacked: isRacked);
        var request = SaveFrom(s.Box);
        request.ResetRackedSince = true;

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(s.Box.RackedSince, saved.RackedSince);
        Assert.Empty(await db.RowsAsync());
    }

    // EDITED

    [DatabaseFact]
    public async Task Fullness_WritesOneEditedEntry_AndSetsLastUpdated() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.Fullness = "HALF";
        var before = DateTime.UtcNow.AddSeconds(-5);

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal("HALF", saved.Fullness);
        Assert.InRange(saved.LastUpdatedAt!.Value, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(db.EmployeeId, saved.LastUpdatedBy!.Id);
        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("EDITED", entry.ActionType);
        Assert.Equal("BOX", entry.TargetType);
        Assert.Equal(s.Box.Id, entry.TargetId);
        Assert.Equal($"#{s.Box.Id}", entry.TargetLabel);
        Assert.Equal(db.DeviceId, entry.DeviceId);
        Assert.False(entry.IsElevated);
        Assert.Equal(new[] { ("fullness", (string?)"FULL", (string?)"HALF") }, Changes(entry.Details));
    }

    [DatabaseFact]
    public async Task Category_IsRecordedByName() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var newName = BoxTestData.RandomName("test-category-");
        var request = SaveFrom(s.Box);
        request.CategoryID = await data.CategoryAsync(newName);

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(newName, saved.Category.Name);
        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal(new[] { ("category", (string?)s.Category, (string?)newName) }, Changes(entry.Details));
    }

    [DatabaseTheory]
    [InlineData("building")]
    [InlineData("letter")]
    [InlineData("level")]
    public async Task AnyPartOfTheLocation_IsOneLocationChange(string part) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        var building = s.Building;
        if (part == "building") {
            building = BoxTestData.RandomName("test-building-");
            request.BuildingID = await data.BuildingAsync(building);
        }
        if (part == "letter") request.ShelfLetter = "BC";
        if (part == "level") request.ShelfLevel = 3;

        await SaveAsync(db, s.Box.Id, request);

        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("EDITED", entry.ActionType);
        Assert.Equal(new[] {
            ("location", (string?)Location(s.Building, "A", 1),
                (string?)Location(building, request.ShelfLetter!, request.ShelfLevel!.Value))
        }, Changes(entry.Details));
    }

    [DatabaseTheory]
    [InlineData(null, "Lights", null, "Lights")]
    [InlineData("Lights", null, "Lights", null)]
    [InlineData("Lights", "   ", "Lights", null)]
    [InlineData("Lights", "  Garland  ", "Lights", "Garland")]
    public async Task Notes_ChangesAreRecorded(string? stored, string? sent, string? from, string? to) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, notes: stored);
        var request = SaveFrom(s.Box);
        request.Notes = sent;

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(to, saved.Notes);
        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal(new[] { ("notes", from, to) }, Changes(entry.Details));
    }

    [DatabaseFact]
    public async Task EveryEditedField_InContractOrder() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, notes: "old");
        var category = BoxTestData.RandomName("test-category-");
        var request = SaveFrom(s.Box);
        request.CategoryID = await data.CategoryAsync(category);
        request.ShelfLevel = 2;
        request.Fullness = "EMPTY";
        request.Notes = "new";

        await SaveAsync(db, s.Box.Id, request);

        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal(new[] {
            ("category", (string?)s.Category, (string?)category),
            ("location", Location(s.Building, "A", 1), Location(s.Building, "A", 2)),
            ("fullness", "FULL", "EMPTY"),
            ("notes", "old", "new"),
        }, Changes(entry.Details));
    }

    // UNRACKED

    [DatabaseFact]
    public async Task Unrack_WritesUnrackedWithNoDetails() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.IsRacked = false;
        request.ResetRackedSince = true; // ignored

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.False(saved.IsRacked);
        Assert.Equal(s.Box.RackedSince, saved.RackedSince);
        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("UNRACKED", entry.ActionType);
        Assert.Null(entry.Details);
    }

    [DatabaseFact]
    public async Task Unrack_WithEdits_UnrackedThenEdited() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.IsRacked = false;
        request.Fullness = "HALF";
        request.ShelfLetter = "Q";

        await SaveAsync(db, s.Box.Id, request);

        var rows = await db.RowsAsync();
        Assert.Equal(new[] { "UNRACKED", "EDITED" }, rows.Select(r => r.ActionType));
        Assert.Null(rows[0].Details);
        Assert.Equal(new[] {
            ("location", (string?)Location(s.Building, "A", 1), (string?)Location(s.Building, "Q", 1)),
            ("fullness", "FULL", "HALF"),
        }, Changes(rows[1].Details));
    }

    // RACKED

    [DatabaseFact]
    public async Task ReRack_InPlace_NoReset_RackedWithNullDetails() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, isRacked: false);
        var request = SaveFrom(s.Box);
        request.IsRacked = true;

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.True(saved.IsRacked);
        Assert.Equal(s.Box.RackedSince, saved.RackedSince);
        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("RACKED", entry.ActionType);
        Assert.Null(entry.Details);
    }

    [DatabaseFact]
    public async Task ReRack_WithReset_StartsTheTimerToday() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var since = Today.AddDays(-200);
        var s = await BoxAsync(db, data, isRacked: false, rackedSince: since);
        var request = SaveFrom(s.Box);
        request.IsRacked = true;
        request.ResetRackedSince = true;

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(Today, saved.RackedSince);
        Assert.Equal(0, saved.DaysStored);
        Assert.False(saved.StoredTooLong);
        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("RACKED", entry.ActionType);
        Assert.Equal(new[] { ("rackedSince", (string?)Day(since), (string?)Day(Today)) }, Changes(entry.Details));
    }

    [DatabaseFact]
    public async Task ReRack_WithReset_SameDay_NoRackedSinceChange() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, isRacked: false, rackedSince: Today);
        var request = SaveFrom(s.Box);
        request.IsRacked = true;
        request.ResetRackedSince = true;

        await SaveAsync(db, s.Box.Id, request);

        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("RACKED", entry.ActionType);
        Assert.Null(entry.Details);
    }

    [DatabaseFact]
    public async Task ReRack_ToANewShelf_LocationGoesInRacked() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data, isRacked: false);
        var request = SaveFrom(s.Box);
        request.IsRacked = true;
        request.ShelfLetter = "C";

        await SaveAsync(db, s.Box.Id, request);

        var entry = Assert.Single(await db.RowsAsync());
        Assert.Equal("RACKED", entry.ActionType);
        Assert.Equal(new[] {
            ("location", (string?)Location(s.Building, "A", 1), (string?)Location(s.Building, "C", 1))
        }, Changes(entry.Details));
    }

    [DatabaseFact]
    public async Task ContractExample_ReRackNewShelfResetAndFullness() {
        // boxes.md: one RACKED (location, rackedSince) + one EDITED (fullness)
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var since = Today.AddDays(-260);
        var s = await BoxAsync(db, data, isRacked: false, rackedSince: since, fullness: "HALF");
        var trailer = BoxTestData.RandomName("test-building-");
        var request = SaveFrom(s.Box);
        request.IsRacked = true;
        request.ResetRackedSince = true;
        request.BuildingID = await data.BuildingAsync(trailer);
        request.ShelfLetter = "C";
        request.Fullness = "FULL";

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.True(saved.IsRacked);
        Assert.Equal(trailer, saved.Building.Name);
        var rows = await db.RowsAsync();
        Assert.Equal(new[] { "RACKED", "EDITED" }, rows.Select(r => r.ActionType));
        Assert.Equal(new[] {
            ("location", (string?)Location(s.Building, "A", 1), (string?)Location(trailer, "C", 1)),
            ("rackedSince", Day(since), Day(Today)),
        }, Changes(rows[0].Details));
        Assert.Equal(new[] { ("fullness", (string?)"HALF", (string?)"FULL") }, Changes(rows[1].Details));
    }

    // Audit entry details

    [DatabaseFact]
    public async Task InMasterMode_EntriesAreElevated() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        await db.ExecuteAsync("UPDATE DEVICE SET is_elevated = TRUE WHERE id = @id", new { id = db.DeviceId });
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.IsRacked = false;
        request.Fullness = "HALF";

        await SaveAsync(db, s.Box.Id, request);

        Assert.All(await db.RowsAsync(), r => Assert.True(r.IsElevated));
    }

    [DatabaseFact]
    public async Task WhenAnAuditEntryFails_NothingIsSaved() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var missingDevice = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM DEVICE");
        var request = SaveFrom(s.Box);
        request.Fullness = "HALF";

        var error = await Assert.ThrowsAsync<MySqlConnector.MySqlException>(
            () => ControllerFor(db.EmployeeId, missingDevice).SaveBox(s.Box.Id, request, CancellationToken.None));

        // fails on the audit insert, after the UPDATE ran
        Assert.Contains("AUDIT_LOG", error.Message);
        var box = await GetAsync(db, s.Box.Id);
        Assert.Equal("FULL", box.Fullness);
        Assert.Null(box.LastUpdatedAt);
    }

    // Reply

    [DatabaseFact]
    public async Task Reply_MatchesGetBox() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.Notes = "new";

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(JsonSerializer.Serialize(await GetAsync(db, s.Box.Id)), JsonSerializer.Serialize(saved));
    }

    [DatabaseFact]
    public async Task CreatedFieldsNeverChange() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var s = await BoxAsync(db, data);
        var request = SaveFrom(s.Box);
        request.IsRacked = false;
        request.Fullness = "EMPTY";

        var saved = await SaveAsync(db, s.Box.Id, request);

        Assert.Equal(s.Box.Id, saved.Id);
        Assert.Equal(s.Box.CreatedAt, saved.CreatedAt);
        Assert.Equal(s.Box.CreatedBy.Id, saved.CreatedBy.Id);
        Assert.Null(saved.DeletedAt);
    }
}
