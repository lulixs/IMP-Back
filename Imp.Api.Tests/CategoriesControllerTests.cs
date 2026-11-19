using System.Reflection;
using System.Text.Json;
using Dapper;
using Imp.Api.Controllers;
using Imp.Api.Data;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Imp.Api.Tests;

// Tests for GET /api/categories (docs/api/categories.md). They call the controller directly, against the Docker
// database, with categories each test makes for itself. Other categories (the sample data, and other tests running
// at the same time) are in the list too, so the tests check their own categories and what is true of the whole list.
public class CategoriesControllerTests {
    // Setup

    private static CategoriesController Controller() => new(new MySqlConnectionFactory(TestDatabase.ConnectionString));

    private static async Task<object> ResponseAsync() =>
        Assert.IsType<OkObjectResult>(await Controller().GetAll(CancellationToken.None)).Value!;

    private static async Task<List<CategoryListItem>> CategoriesAsync() {
        var response = await ResponseAsync();
        var categories = response.GetType().GetProperty("categories")!.GetValue(response);
        return Assert.IsAssignableFrom<IEnumerable<CategoryListItem>>(categories).ToList();
    }

    // Tests

    [Fact]
    public void Controller_NeedsTheSessionToken() {
        var authorize = typeof(CategoriesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthPolicies.Session, authorize!.Policy);
    }

    [DatabaseFact]
    public async Task Response_IsTheContractsShape() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        await data.CategoryAsync(symbol: "🧥");

        var json = JsonSerializer.SerializeToElement(await ResponseAsync(), JsonSerializerOptions.Web);

        Assert.Equal(new[] { "categories" }, json.EnumerateObject().Select(p => p.Name));
        var first = json.GetProperty("categories")[0];
        Assert.Equal(new[] { "id", "name", "symbol", "activeBoxCount", "deletedBoxCount" },
            first.EnumerateObject().Select(p => p.Name));
    }

    // Box counts (D-198)

    [DatabaseFact]
    public async Task Counts_SplitActiveAndSoftDeletedBoxes() {
        // Active means not soft-deleted, racked or not.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var buildingId = await data.BuildingAsync();
        await data.BoxAsync(categoryId, buildingId, isRacked: true);
        await data.BoxAsync(categoryId, buildingId, isRacked: false);
        await data.SoftDeleteAsync(await data.BoxAsync(categoryId, buildingId, isRacked: true), db.EmployeeId);
        await data.SoftDeleteAsync(await data.BoxAsync(categoryId, buildingId, isRacked: false), db.EmployeeId);
        await data.SoftDeleteAsync(await data.BoxAsync(categoryId, buildingId), db.EmployeeId);

        var category = Assert.Single(await CategoriesAsync(), c => c.Id == categoryId);

        Assert.Equal(2, category.ActiveBoxCount);
        Assert.Equal(3, category.DeletedBoxCount);
    }

    [DatabaseFact]
    public async Task Counts_AreZeroForACategoryWithNoBoxes() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var emptyId = await data.CategoryAsync();
        await data.BoxAsync(await data.CategoryAsync(), await data.BuildingAsync()); // a box somewhere else

        var category = Assert.Single(await CategoriesAsync(), c => c.Id == emptyId);

        Assert.Equal(0, category.ActiveBoxCount);
        Assert.Equal(0, category.DeletedBoxCount);
    }

    [DatabaseFact]
    public async Task Counts_IncludeBoxesInEveryBuilding() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        await data.BoxAsync(categoryId, await data.BuildingAsync());
        await data.BoxAsync(categoryId, await data.BuildingAsync());

        var category = Assert.Single(await CategoriesAsync(), c => c.Id == categoryId);

        Assert.Equal(2, category.ActiveBoxCount);
    }

    [DatabaseFact]
    public async Task Counts_SoftDeletingABoxMovesItFromActiveToDeleted() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var categoryId = await data.CategoryAsync();
        var boxId = await data.BoxAsync(categoryId, await data.BuildingAsync());

        var before = Assert.Single(await CategoriesAsync(), c => c.Id == categoryId);
        await data.SoftDeleteAsync(boxId, db.EmployeeId);
        var after = Assert.Single(await CategoriesAsync(), c => c.Id == categoryId);

        Assert.Equal((1, 0), (before.ActiveBoxCount, before.DeletedBoxCount));
        Assert.Equal((0, 1), (after.ActiveBoxCount, after.DeletedBoxCount));
    }

    [DatabaseFact]
    public async Task EachCategory_HasItsIdNameAndSymbol() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var withName = BoxTestData.RandomName("test-category-");
        var withoutName = BoxTestData.RandomName("test-category-");
        var withId = await data.CategoryAsync(withName, "🎃");
        var withoutId = await data.CategoryAsync(withoutName, null);

        var categories = await CategoriesAsync();

        var with = Assert.Single(categories, c => c.Id == withId);
        Assert.Equal(withName, with.Name);
        Assert.Equal("🎃", with.Symbol);
        var without = Assert.Single(categories, c => c.Id == withoutId);
        Assert.Equal(withoutName, without.Name);
        Assert.Null(without.Symbol);
    }

    [DatabaseFact]
    public async Task EveryCategory_IsListed_IncludingOnesWithNoBoxes() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var emptyId = await data.CategoryAsync();
        var allIds = (await db.Connection.QueryAsync<int>("SELECT id FROM CATEGORY")).ToList();

        var ids = (await CategoriesAsync()).Select(c => c.Id).ToList();

        Assert.Contains(emptyId, ids);
        Assert.All(allIds, id => Assert.Contains(id, ids));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [DatabaseFact]
    public async Task List_IsSortedByName_IgnoringCapitals() {
        // Made in the wrong order on purpose. Sorted by name: a, B, c (a plain character sort would put B first).
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var prefix = BoxTestData.RandomName("test-sort-") + "-";
        var c = await data.CategoryAsync(prefix + "c");
        var a = await data.CategoryAsync(prefix + "a");
        var b = await data.CategoryAsync(prefix + "B");

        var mine = (await CategoriesAsync()).Where(cat => cat.Name.StartsWith(prefix)).Select(cat => cat.Id);

        Assert.Equal(new[] { a, b, c }, mine);
    }

    [DatabaseFact]
    public async Task WholeList_IsInMySqlsNameOrder() {
        await using var db = await TestDatabase.CreateAsync();

        var ids = (await CategoriesAsync()).Select(c => c.Id).ToArray();
        var expected = await db.Connection.QueryAsync<int>(
            "SELECT id FROM CATEGORY WHERE id IN @ids ORDER BY name", new { ids });

        Assert.Equal(expected, ids);
    }
}
