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

// Tests for GET /api/buildings (docs/api/buildings.md). They call the controller directly, against the Docker
// database, with buildings each test makes for itself. Other buildings (the sample data, and other tests running
// at the same time) are in the list too, so the tests check their own buildings and what is true of the whole list.
public class BuildingsControllerTests {
    // Setup

    private static BuildingsController Controller() => new(new MySqlConnectionFactory(TestDatabase.ConnectionString));

    private static async Task<object> ResponseAsync() =>
        Assert.IsType<OkObjectResult>(await Controller().GetAll(CancellationToken.None)).Value!;

    private static async Task<List<BuildingListItem>> BuildingsAsync() {
        var response = await ResponseAsync();
        var buildings = response.GetType().GetProperty("buildings")!.GetValue(response);
        return Assert.IsAssignableFrom<IEnumerable<BuildingListItem>>(buildings).ToList();
    }

    // Tests

    [Fact]
    public void Controller_NeedsTheSessionToken() {
        var authorize = typeof(BuildingsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthPolicies.Session, authorize!.Policy);
    }

    [DatabaseFact]
    public async Task Response_IsTheContractsShape() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        await data.BuildingAsync();

        var json = JsonSerializer.SerializeToElement(await ResponseAsync(), JsonSerializerOptions.Web);

        Assert.Equal(new[] { "buildings" }, json.EnumerateObject().Select(p => p.Name));
        var first = json.GetProperty("buildings")[0];
        Assert.Equal(new[] { "id", "name", "activeBoxCount", "deletedBoxCount" },
            first.EnumerateObject().Select(p => p.Name));
    }

    // Box counts (D-198)

    [DatabaseFact]
    public async Task Counts_SplitActiveAndSoftDeletedBoxes() {
        // Active means not soft-deleted, racked or not.
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var buildingId = await data.BuildingAsync();
        var categoryId = await data.CategoryAsync();
        await data.BoxAsync(categoryId, buildingId, isRacked: true);
        await data.BoxAsync(categoryId, buildingId, isRacked: false);
        await data.SoftDeleteAsync(await data.BoxAsync(categoryId, buildingId, isRacked: true), db.EmployeeId);
        await data.SoftDeleteAsync(await data.BoxAsync(categoryId, buildingId, isRacked: false), db.EmployeeId);
        await data.SoftDeleteAsync(await data.BoxAsync(categoryId, buildingId), db.EmployeeId);

        var building = Assert.Single(await BuildingsAsync(), b => b.Id == buildingId);

        Assert.Equal(2, building.ActiveBoxCount);
        Assert.Equal(3, building.DeletedBoxCount);
    }

    [DatabaseFact]
    public async Task Counts_AreZeroForABuildingWithNoBoxes() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var emptyId = await data.BuildingAsync();
        await data.BoxAsync(await data.CategoryAsync(), await data.BuildingAsync()); // a box somewhere else

        var building = Assert.Single(await BuildingsAsync(), b => b.Id == emptyId);

        Assert.Equal(0, building.ActiveBoxCount);
        Assert.Equal(0, building.DeletedBoxCount);
    }

    [DatabaseFact]
    public async Task Counts_IncludeBoxesInEveryCategory() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var buildingId = await data.BuildingAsync();
        await data.BoxAsync(await data.CategoryAsync(), buildingId);
        await data.BoxAsync(await data.CategoryAsync(), buildingId);

        var building = Assert.Single(await BuildingsAsync(), b => b.Id == buildingId);

        Assert.Equal(2, building.ActiveBoxCount);
    }

    [DatabaseFact]
    public async Task Counts_SoftDeletingABoxMovesItFromActiveToDeleted() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var buildingId = await data.BuildingAsync();
        var boxId = await data.BoxAsync(await data.CategoryAsync(), buildingId);

        var before = Assert.Single(await BuildingsAsync(), b => b.Id == buildingId);
        await data.SoftDeleteAsync(boxId, db.EmployeeId);
        var after = Assert.Single(await BuildingsAsync(), b => b.Id == buildingId);

        Assert.Equal((1, 0), (before.ActiveBoxCount, before.DeletedBoxCount));
        Assert.Equal((0, 1), (after.ActiveBoxCount, after.DeletedBoxCount));
    }

    [DatabaseFact]
    public async Task EachBuilding_HasItsIdAndName() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var name = BoxTestData.RandomName("test-building-");
        var id = await data.BuildingAsync(name);

        var building = Assert.Single(await BuildingsAsync(), b => b.Id == id);

        Assert.Equal(name, building.Name);
    }

    [DatabaseFact]
    public async Task EveryBuilding_IsListed_IncludingOnesWithNoBoxes() {
        await using var db = await TestDatabase.CreateAsync();
        await using var data = new BoxTestData(db);
        var emptyId = await data.BuildingAsync();
        var before = await db.Connection.QueryAsync<int>("SELECT id FROM BUILDING");

        var ids = (await BuildingsAsync()).Select(b => b.Id).ToList();
        var after = await db.Connection.QueryAsync<int>("SELECT id FROM BUILDING");
        // Other test classes add and remove rows while this runs, so only the ones there both before and after
        // the call are sure to be listed.
        var allIds = before.Intersect(after).ToList();

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
        var c = await data.BuildingAsync(prefix + "c");
        var a = await data.BuildingAsync(prefix + "a");
        var b = await data.BuildingAsync(prefix + "B");

        var mine = (await BuildingsAsync()).Where(building => building.Name.StartsWith(prefix)).Select(building => building.Id);

        Assert.Equal(new[] { a, b, c }, mine);
    }

    [DatabaseFact]
    public async Task WholeList_IsInMySqlsNameOrder() {
        await using var db = await TestDatabase.CreateAsync();

        var ids = (await BuildingsAsync()).Select(b => b.Id).ToArray();
        var expected = (await db.Connection.QueryAsync<int>(
            "SELECT id FROM BUILDING WHERE id IN @ids ORDER BY name", new { ids })).ToList();

        // A row another test removes in between is missing from expected, so compare only the rows still there.
        Assert.Equal(expected, ids.Where(expected.Contains));
    }
}
