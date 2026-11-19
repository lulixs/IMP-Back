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

    private static async Task<List<BuildingSummary>> BuildingsAsync() {
        var response = await ResponseAsync();
        var buildings = response.GetType().GetProperty("buildings")!.GetValue(response);
        return Assert.IsAssignableFrom<IEnumerable<BuildingSummary>>(buildings).ToList();
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
        Assert.Equal(new[] { "id", "name" }, first.EnumerateObject().Select(p => p.Name));
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
        var allIds = (await db.Connection.QueryAsync<int>("SELECT id FROM BUILDING")).ToList();

        var ids = (await BuildingsAsync()).Select(b => b.Id).ToList();

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
        var expected = await db.Connection.QueryAsync<int>(
            "SELECT id FROM BUILDING WHERE id IN @ids ORDER BY name", new { ids });

        Assert.Equal(expected, ids);
    }
}
