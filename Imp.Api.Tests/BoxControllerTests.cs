using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Imp.Api.Controllers;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Imp.Api.Tests;

// Tests for GET /api/boxes/{id} and GET /api/boxes (docs/api/boxes.md). They call the controller directly,
// against the Docker database, with boxes each test makes for itself (see BoxTestData). The checks that the
// session token itself is valid belong to TokenEvents and are not repeated here.
//
// The sample data's boxes are also in the database, so search tests check only what is true of every result
// (for example, that every box number starts with the digits searched) plus the test's own boxes. Most tests
// give their boxes a category of their own and filter on it, so nothing else shows up.
public class BoxControllerTests {
    // The exact sentences from boxes.md.
    private const string NoSuchBox = "This box doesn't exist.";
    private const string BadCategoryId = "Category ID must be a number.";
    private const string BadRacked = "Racked must be true or false.";
    private const string BadSort = "Sort must be oldest or newest.";

    // Setup

    private static BoxController Controller() => new(new MySqlConnectionFactory(TestDatabase.ConnectionString));

    // Fails the test if the controller touches the database, to show a request was refused before any query.
    private sealed class NoDatabase : IDbConnectionFactory {
        public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The request should have been refused before opening a connection.");
    }

    private static async Task<Box> GetBoxAsync(int id) {
        var result = Assert.IsType<OkObjectResult>(await Controller().GetBox(id, CancellationToken.None));
        return Assert.IsType<Box>(result.Value);
    }

    // The boxes from GET /api/boxes, in the order returned.
    private static async Task<List<Box>> SearchAsync(string? search = null, int? categoryId = null, string? racked = null,
            string? sort = null) {
        var result = Assert.IsType<OkObjectResult>(
            await Controller().GetBoxes(search, categoryId?.ToString(), racked, sort, CancellationToken.None));
        var boxes = result.Value!.GetType().GetProperty("boxes")!.GetValue(result.Value);
        return Assert.IsAssignableFrom<IEnumerable<Box>>(boxes).ToList();
    }

    private static List<int> Ids(IEnumerable<Box> boxes) => boxes.Select(b => b.Id).ToList();

    // Both endpoints

    [Fact]
    public void Controller_NeedsTheSessionToken() {
        var authorize = typeof(BoxController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthPolicies.Session, authorize!.Policy);
    }

    // GET /api/boxes/{id}

    [Fact]
    public void GetBox_RouteOnlyMatchesNumbers() {
        // {id:int}: /api/boxes/abc never reaches GetBox, so the framework answers 404 instead of a 400 about the body.
        var get = typeof(BoxController).GetMethod(nameof(BoxController.GetBox))!.GetCustomAttribute<HttpGetAttribute>();

        Assert.Equal("{id:int}", get!.Template);
    }

    [DatabaseFact]
    public async Task GetBox_ReturnsEveryField() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryName = BoxTestData.RandomName("test-category-");
        var buildingName = BoxTestData.RandomName("test-building-");
        var categoryId = await data.CategoryAsync(categoryName, "🧥");
        var buildingId = await data.BuildingAsync(buildingName);
        var rackedSince = BoxTestData.Today.AddDays(-5);
        var id = await data.BoxAsync(categoryId, buildingId, rackedSince, shelfLetter: "ABC", shelfLevel: 3,
            fullness: "HALF", notes: "Check zippers.");
        var employeeName = await db.ScalarAsync<string>("SELECT name FROM EMPLOYEE_ACCOUNT WHERE id = @id", new { id = db.EmployeeId });
        var createdAt = await db.ScalarAsync<DateTime>("SELECT created_at FROM BOX WHERE id = @id", new { id });

        var box = await GetBoxAsync(id);

        Assert.Equal(id, box.Id);
        Assert.Equal(categoryId, box.Category.Id);
        Assert.Equal(categoryName, box.Category.Name);
        Assert.Equal("🧥", box.Category.Symbol);
        Assert.Equal(buildingId, box.Building.Id);
        Assert.Equal(buildingName, box.Building.Name);
        Assert.Equal("ABC", box.ShelfLetter);
        Assert.Equal(3, box.ShelfLevel);
        Assert.Equal("HALF", box.Fullness);
        Assert.Equal("Check zippers.", box.Notes);
        Assert.True(box.IsRacked);
        Assert.Equal(DateOnly.FromDateTime(rackedSince), box.RackedSince);
        Assert.Equal(createdAt, box.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, box.CreatedAt.Kind);
        Assert.Equal(db.EmployeeId, box.CreatedBy.Id);
        Assert.Equal(employeeName, box.CreatedBy.Name);
    }

    [DatabaseFact]
    public async Task GetBox_NeverChanged_HasNoUpdateOrDelete() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        var box = await GetBoxAsync(id);

        Assert.Null(box.LastUpdatedAt);
        Assert.Null(box.LastUpdatedBy);
        Assert.Null(box.DeletedAt);
        Assert.Null(box.DeletedBy);
    }

    [DatabaseFact]
    public async Task GetBox_NoNotesOrSymbol_AreNull() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.BoxAsync(await data.CategoryAsync(symbol: null), await data.BuildingAsync(), notes: null);

        var box = await GetBoxAsync(id);

        Assert.Null(box.Notes);
        Assert.Null(box.Category.Symbol);
    }

    [DatabaseFact]
    public async Task GetBox_Changed_ShowsWhoChangedItAndWhen() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var editorId = await db.CreateEmployeeAsync();
        await data.EditAsync(id, editorId);
        var editorName = await db.ScalarAsync<string>("SELECT name FROM EMPLOYEE_ACCOUNT WHERE id = @editorId", new { editorId });
        var lastUpdatedAt = await db.ScalarAsync<DateTime>("SELECT last_updated_at FROM BOX WHERE id = @id", new { id });

        var box = await GetBoxAsync(id);

        Assert.Equal(editorId, box.LastUpdatedBy!.Id);
        Assert.Equal(editorName, box.LastUpdatedBy.Name);
        Assert.Equal(lastUpdatedAt, box.LastUpdatedAt);
        Assert.Equal(DateTimeKind.Utc, box.LastUpdatedAt!.Value.Kind);
        Assert.Null(box.DeletedBy);
    }

    [DatabaseFact]
    public async Task GetBox_EmployeeRenamed_ShowsTheCurrentName() {
        // boxes.md: "Employee names are the current names, so a renamed employee shows their new name."
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var newName = BoxTestData.RandomName("test-renamed-");
        await db.ExecuteAsync("UPDATE EMPLOYEE_ACCOUNT SET name = @newName WHERE id = @id", new { newName, id = db.EmployeeId });

        var box = await GetBoxAsync(id);

        Assert.Equal(newName, box.CreatedBy.Name);
    }

    [DatabaseFact]
    public async Task GetBox_SoftDeleted_IsReturnedWithWhoDeletedItAndWhen() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var deleterId = await db.CreateEmployeeAsync();
        await data.SoftDeleteAsync(id, deleterId);
        var deleterName = await db.ScalarAsync<string>("SELECT name FROM EMPLOYEE_ACCOUNT WHERE id = @deleterId", new { deleterId });
        var deletedAt = await db.ScalarAsync<DateTime>("SELECT deleted_at FROM BOX WHERE id = @id", new { id });

        var box = await GetBoxAsync(id);

        Assert.Equal(deletedAt, box.DeletedAt);
        Assert.Equal(DateTimeKind.Utc, box.DeletedAt!.Value.Kind);
        Assert.Equal(deleterId, box.DeletedBy!.Id);
        Assert.Equal(deleterName, box.DeletedBy.Name);
    }

    [DatabaseFact]
    public async Task GetBox_NoSuchNumber_Is404() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var missingId = await data.MissingBoxIdAsync();

        var error = await Assert.ThrowsAsync<ApiError>(() => Controller().GetBox(missingId, CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(NoSuchBox, error.Message);
    }

    // Days stored and the alert

    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(400)]
    public async Task GetBox_DaysStored_CountsFromRackedSinceToTheStoresToday(int daysAgo) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync(BoxTestData.Today.AddDays(-daysAgo));

        var box = await GetBoxAsync(id);

        Assert.Equal(daysAgo, box.DaysStored);
    }

    [DatabaseTheory]
    [InlineData(-1, false)] // a day short of the threshold
    [InlineData(0, true)]   // exactly the threshold counts ("at least")
    [InlineData(1, true)]   // past it
    public async Task GetBox_StoredTooLong_FollowsTheAlertThreshold(int daysPastThreshold, bool expected) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var threshold = await data.AlertThresholdDaysAsync();
        var id = await data.LoneBoxAsync(BoxTestData.Today.AddDays(-(threshold + daysPastThreshold)));

        var box = await GetBoxAsync(id);

        Assert.Equal(threshold + daysPastThreshold, box.DaysStored);
        Assert.Equal(expected, box.StoredTooLong);
    }

    [DatabaseFact]
    public async Task GetBox_UnrackedBox_IsNeverStoredTooLong() {
        // boxes.md: always false for an unracked box. Days stored still counts from rackedSince.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var threshold = await data.AlertThresholdDaysAsync();
        var id = await data.LoneBoxAsync(BoxTestData.Today.AddDays(-(threshold + 30)), isRacked: false);

        var box = await GetBoxAsync(id);

        Assert.False(box.IsRacked);
        Assert.Equal(threshold + 30, box.DaysStored);
        Assert.False(box.StoredTooLong);
    }

    // GET /api/boxes: bad parameters

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("#3")]
    public async Task GetBoxes_CategoryIdNotANumber_Is400BeforeAnyQuery(string categoryId) {
        var controller = new BoxController(new NoDatabase());

        var error = await Assert.ThrowsAsync<ApiError>(
            () => controller.GetBoxes(null, categoryId, null, null, CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(BadCategoryId, error.Message);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("racked")]
    public async Task GetBoxes_RackedNotTrueOrFalse_Is400BeforeAnyQuery(string racked) {
        var controller = new BoxController(new NoDatabase());

        var error = await Assert.ThrowsAsync<ApiError>(
            () => controller.GetBoxes(null, null, racked, null, CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(BadRacked, error.Message);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("old")]
    [InlineData("racked_since")]
    public async Task GetBoxes_SortNotOldestOrNewest_Is400BeforeAnyQuery(string sort) {
        var controller = new BoxController(new NoDatabase());

        var error = await Assert.ThrowsAsync<ApiError>(
            () => controller.GetBoxes(null, null, null, sort, CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(BadSort, error.Message);
    }

    // GET /api/boxes: what comes back

    [DatabaseFact]
    public async Task GetBoxes_ResponseIsWrappedInABoxesList() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        await data.BoxAsync(categoryId, await data.BuildingAsync());

        var result = Assert.IsType<OkObjectResult>(
            await Controller().GetBoxes(null, categoryId.ToString(), null, null, CancellationToken.None));
        var json = JsonSerializer.SerializeToElement(result.Value, JsonSerializerOptions.Web);

        Assert.Equal(new[] { "boxes" }, json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Array, json.GetProperty("boxes").ValueKind);
        Assert.Equal(1, json.GetProperty("boxes").GetArrayLength());
    }

    [DatabaseFact]
    public async Task GetBoxes_NoMatch_IsAnEmptyList() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var emptyCategoryId = await data.CategoryAsync();

        Assert.Empty(await SearchAsync(categoryId: emptyCategoryId));
        Assert.Empty(await SearchAsync(search: BoxTestData.RandomName("no-such-category-")));
    }

    [DatabaseFact]
    public async Task GetBoxes_SoftDeletedBoxes_NeverAppear() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var buildingId = await data.BuildingAsync();
        var kept = await data.BoxAsync(categoryId, buildingId);
        var deleted = await data.BoxAsync(categoryId, buildingId);
        await data.SoftDeleteAsync(deleted, db.EmployeeId);

        Assert.Equal(new[] { kept }, Ids(await SearchAsync(categoryId: categoryId)));
        Assert.DoesNotContain(deleted, Ids(await SearchAsync()));
        Assert.DoesNotContain(deleted, Ids(await SearchAsync(search: deleted.ToString())));
        Assert.DoesNotContain(deleted, Ids(await SearchAsync(racked: "true")));
    }

    [DatabaseFact]
    public async Task GetBoxes_NoFilters_ReturnsEveryBoxThatIsntDeleted() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var racked = await data.LoneBoxAsync();
        var unracked = await data.LoneBoxAsync(isRacked: false);
        const string activeSql = "SELECT id FROM BOX WHERE deleted_at IS NULL";
        var before = await db.Connection.QueryAsync<int>(activeSql);

        var ids = Ids(await SearchAsync());
        var after = await db.Connection.QueryAsync<int>(activeSql);

        Assert.Contains(racked, ids);
        Assert.Contains(unracked, ids);
        // Other test classes add and remove boxes while this runs, so only the boxes there both before and
        // after the search are sure to be in it.
        Assert.All(before.Intersect(after), id => Assert.Contains(id, ids));
    }

    [DatabaseFact]
    public async Task GetBoxes_BoxesHaveTheFullShape() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryName = BoxTestData.RandomName("test-category-");
        var categoryId = await data.CategoryAsync(categoryName, "🎄");
        var id = await data.BoxAsync(categoryId, await data.BuildingAsync(), BoxTestData.Today.AddDays(-12));

        var box = Assert.Single(await SearchAsync(categoryId: categoryId));

        Assert.Equal(id, box.Id);
        Assert.Equal(categoryName, box.Category.Name);
        Assert.Equal("🎄", box.Category.Symbol);
        Assert.Equal(12, box.DaysStored);
        Assert.Equal(db.EmployeeId, box.CreatedBy.Id);
    }

    // GET /api/boxes: search by box number

    [DatabaseTheory]
    [InlineData("")]
    [InlineData("#")]
    public async Task GetBoxes_SearchBoxNumber_WithOrWithoutHash(string hash) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        var ids = Ids(await SearchAsync(search: hash + id));

        Assert.Contains(id, ids);
        Assert.All(ids, found => Assert.StartsWith(id.ToString(), found.ToString()));
    }

    [DatabaseFact]
    public async Task GetBoxes_SearchBoxNumber_MatchesNumbersThatStartWithTheDigits() {
        // boxes.md: "67" finds #67, #670, and #6789, but never #167.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var firstDigit = id.ToString()[..1];

        const string matchingSql = "SELECT id FROM BOX WHERE deleted_at IS NULL AND CAST(id AS CHAR) LIKE @pattern";
        var pattern = new { pattern = firstDigit + "%" };
        var before = await db.Connection.QueryAsync<int>(matchingSql, pattern);

        var ids = Ids(await SearchAsync(search: firstDigit));
        var after = await db.Connection.QueryAsync<int>(matchingSql, pattern);

        Assert.Contains(id, ids);
        Assert.All(ids, found => Assert.StartsWith(firstDigit, found.ToString()));
        // As in the no-filter test: only boxes there before and after the search are sure to be in it.
        Assert.All(before.Intersect(after), match => Assert.Contains(match, ids));
    }

    [DatabaseFact]
    public async Task GetBoxes_SearchBoxNumber_DoesntMatchDigitsInTheMiddle() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();
        var lastDigits = id.ToString()[1..];
        if (lastDigits.Length == 0) return; // a one-digit id has no middle to test

        var ids = Ids(await SearchAsync(search: lastDigits));

        Assert.All(ids, found => Assert.StartsWith(lastDigits, found.ToString()));
    }

    [DatabaseFact]
    public async Task GetBoxes_SearchBoxNumber_IgnoresCategoryNames() {
        // Digits always mean a box number (D-172), even when a category's name contains them.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var digits = Random.Shared.Next(100_000_000, 999_999_999).ToString();
        var categoryId = await data.CategoryAsync("test-category-" + digits);
        var id = await data.BoxAsync(categoryId, await data.BuildingAsync());

        var ids = Ids(await SearchAsync(search: digits));

        Assert.DoesNotContain(id, ids);
    }

    // GET /api/boxes: search by category name

    [DatabaseFact]
    public async Task GetBoxes_SearchText_MatchesCategoryNamesThatContainIt() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var unique = Guid.NewGuid().ToString("N")[..12];
        var categoryId = await data.CategoryAsync($"test-start-{unique}-end");
        var buildingId = await data.BuildingAsync();
        var first = await data.BoxAsync(categoryId, buildingId);
        var second = await data.BoxAsync(categoryId, buildingId);

        Assert.Equal(new[] { first, second }, Ids(await SearchAsync(search: $"test-start-{unique}-end")));
        Assert.Equal(new[] { first, second }, Ids(await SearchAsync(search: $"start-{unique}")));
        Assert.Equal(new[] { first, second }, Ids(await SearchAsync(search: $"{unique}-end")));
    }

    [DatabaseTheory]
    [InlineData("UPPER")]
    [InlineData("lower")]
    [InlineData("MiXeD")]
    public async Task GetBoxes_SearchText_IgnoresCapitals(string casing) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var name = BoxTestData.RandomName("test-Mixed-Case-");
        var id = await data.BoxAsync(await data.CategoryAsync(name), await data.BuildingAsync());
        var search = casing switch {
            "UPPER" => name.ToUpperInvariant(),
            "lower" => name.ToLowerInvariant(),
            _ => string.Concat(name.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c))),
        };

        Assert.Equal(new[] { id }, Ids(await SearchAsync(search: search)));
    }

    [DatabaseFact]
    public async Task GetBoxes_SearchText_StartingWithHash_IsACategoryName() {
        // "#shoes" isn't digits after the #, so it searches category names, # included.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var name = "#" + BoxTestData.RandomName("test-shoes-");
        var id = await data.BoxAsync(await data.CategoryAsync(name), await data.BuildingAsync());

        Assert.Equal(new[] { id }, Ids(await SearchAsync(search: name)));
    }

    [DatabaseTheory]
    [InlineData("_")]
    [InlineData("%")]
    public async Task GetBoxes_SearchText_TreatsLikeWildcardsAsPlainCharacters(string character) {
        // In SQL LIKE, _ and % match any character. Search must match them only as themselves.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var withIt = await data.BoxAsync(await data.CategoryAsync("test" + character + Guid.NewGuid().ToString("N")),
            await data.BuildingAsync());
        var withoutIt = await data.LoneBoxAsync();

        var boxes = await SearchAsync(search: character);

        Assert.Contains(withIt, Ids(boxes));
        Assert.DoesNotContain(withoutIt, Ids(boxes));
        Assert.All(boxes, box => Assert.Contains(character, box.Category.Name));
    }

    [DatabaseFact]
    public async Task GetBoxes_SearchJustAHash_MatchesCategoriesWithAHash() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.BoxAsync(await data.CategoryAsync("#" + BoxTestData.RandomName("test-hash-")),
            await data.BuildingAsync());

        var boxes = await SearchAsync(search: "#");

        Assert.Contains(id, Ids(boxes));
        Assert.All(boxes, box => Assert.Contains("#", box.Category.Name));
    }

    [DatabaseFact]
    public async Task GetBoxes_SearchText_DoesntMatchBoxNumbers() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var id = await data.LoneBoxAsync();

        // Letters next to the number make it text, which only searches category names.
        Assert.DoesNotContain(id, Ids(await SearchAsync(search: "box" + id)));
    }

    // GET /api/boxes: filters

    [DatabaseFact]
    public async Task GetBoxes_CategoryId_ReturnsOnlyThatCategory() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var wanted = await data.CategoryAsync();
        var other = await data.CategoryAsync();
        var buildingId = await data.BuildingAsync();
        var a = await data.BoxAsync(wanted, buildingId);
        var b = await data.BoxAsync(wanted, buildingId);
        await data.BoxAsync(other, buildingId);

        var boxes = await SearchAsync(categoryId: wanted);

        Assert.Equal(new[] { a, b }, Ids(boxes));
        Assert.All(boxes, box => Assert.Equal(wanted, box.Category.Id));
    }

    [DatabaseFact]
    public async Task GetBoxes_CategoryIdThatDoesntExist_IsAnEmptyList() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var missing = await db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM CATEGORY");

        Assert.Empty(await SearchAsync(categoryId: missing));
    }

    [DatabaseTheory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task GetBoxes_Racked_ReturnsOnlyRackedOrOnlyUnracked(string racked, bool expected) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var buildingId = await data.BuildingAsync();
        var rackedBox = await data.BoxAsync(categoryId, buildingId, isRacked: true);
        var unrackedBox = await data.BoxAsync(categoryId, buildingId, isRacked: false);

        var mine = await SearchAsync(categoryId: categoryId, racked: racked);
        var everywhere = await SearchAsync(racked: racked);

        Assert.Equal(new[] { expected ? rackedBox : unrackedBox }, Ids(mine));
        Assert.All(everywhere, box => Assert.Equal(expected, box.IsRacked));
    }

    [DatabaseFact]
    public async Task GetBoxes_NoRacked_ReturnsBoth() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var buildingId = await data.BuildingAsync();
        var rackedBox = await data.BoxAsync(categoryId, buildingId, isRacked: true);
        var unrackedBox = await data.BoxAsync(categoryId, buildingId, isRacked: false);

        Assert.Equal(new[] { rackedBox, unrackedBox }, Ids(await SearchAsync(categoryId: categoryId)));
    }

    [DatabaseFact]
    public async Task GetBoxes_FiltersCombine() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var name = BoxTestData.RandomName("test-combined-");
        var categoryId = await data.CategoryAsync(name);
        var otherCategoryId = await data.CategoryAsync(BoxTestData.RandomName("test-combined-other-"));
        var buildingId = await data.BuildingAsync();
        var match = await data.BoxAsync(categoryId, buildingId, isRacked: false);
        await data.BoxAsync(categoryId, buildingId, isRacked: true);       // wrong rack status
        await data.BoxAsync(otherCategoryId, buildingId, isRacked: false); // wrong category

        // Search by category name, plus a category filter and a rack filter.
        Assert.Equal(new[] { match }, Ids(await SearchAsync(search: name, categoryId: categoryId, racked: "false")));
        // Search by box number, plus the same filters.
        Assert.Equal(new[] { match }, Ids(await SearchAsync(search: "#" + match, categoryId: categoryId, racked: "false")));
        // The same number with a filter it doesn't pass finds nothing.
        Assert.Empty(await SearchAsync(search: "#" + match, categoryId: categoryId, racked: "true"));
    }

    // GET /api/boxes: sort

    // Five boxes in one category, racked on these days ago. Two share a day, to check the tie-break.
    private static async Task<Dictionary<string, int>> SortSetAsync(BoxTestData data, int categoryId) {
        var buildingId = await data.BuildingAsync();
        var today = BoxTestData.Today;
        var boxes = new Dictionary<string, int>();
        boxes["10 days"] = await data.BoxAsync(categoryId, buildingId, today.AddDays(-10));
        boxes["30 days"] = await data.BoxAsync(categoryId, buildingId, today.AddDays(-30));
        boxes["15 days, first"] = await data.BoxAsync(categoryId, buildingId, today.AddDays(-15));
        boxes["20 days"] = await data.BoxAsync(categoryId, buildingId, today.AddDays(-20));
        boxes["15 days, second"] = await data.BoxAsync(categoryId, buildingId, today.AddDays(-15));
        return boxes;
    }

    [DatabaseTheory]
    [InlineData(null)]
    [InlineData("oldest")]
    public async Task GetBoxes_Oldest_IsEarliestRackedSinceFirst_AndTheDefault(string? sort) {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var boxes = await SortSetAsync(data, categoryId);

        var ids = Ids(await SearchAsync(categoryId: categoryId, sort: sort));

        Assert.Equal(new[] {
            boxes["30 days"], boxes["20 days"], boxes["15 days, first"], boxes["15 days, second"], boxes["10 days"]
        }, ids);
    }

    [DatabaseFact]
    public async Task GetBoxes_Newest_IsLatestRackedSinceFirst() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var boxes = await SortSetAsync(data, categoryId);

        var ids = Ids(await SearchAsync(categoryId: categoryId, sort: "newest"));

        Assert.Equal(new[] {
            boxes["10 days"], boxes["15 days, first"], boxes["15 days, second"], boxes["20 days"], boxes["30 days"]
        }, ids);
    }

    [DatabaseTheory]
    [InlineData("oldest")]
    [InlineData("newest")]
    public async Task GetBoxes_SortHoldsAcrossEveryBox(string sort) {
        await using var db = await TestDatabase.CreateAsync();

        var boxes = await SearchAsync(sort: sort);

        for (var i = 1; i < boxes.Count; i++) {
            var before = boxes[i - 1];
            var after = boxes[i];
            var inOrder = sort == "oldest"
                ? before.RackedSince < after.RackedSince
                : before.RackedSince > after.RackedSince;
            var tieInOrder = before.RackedSince == after.RackedSince && before.Id < after.Id;
            Assert.True(inOrder || tieInOrder,
                $"#{before.Id} ({before.RackedSince}) came before #{after.Id} ({after.RackedSince}) with sort={sort}.");
        }
    }
}
