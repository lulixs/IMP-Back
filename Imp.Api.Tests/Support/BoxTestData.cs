using Dapper;
using Imp.Api.Data;
using Xunit;

namespace Imp.Api.Tests;

// Makes categories, buildings, and boxes for the box, category, and building tests, and removes them afterward.
//
// The controllers open their own database connection, so these rows are saved for real (no transaction) and
// other connections can see them. Every name is random, so a leftover never collides with anything, and the
// sample data is never touched.
//
// Declare it after the TestDatabase, so it is disposed first: boxes point at the test's employees, so they
// have to go before TestDatabase removes those employees.
//
// Usage:  await using var db = await TestDatabase.CreateAsync();
//         await using var data = new BoxTestData(db);
public sealed class BoxTestData : IAsyncDisposable {
    private readonly TestDatabase _db;
    private readonly List<int> _boxIds = new();
    private readonly List<int> _categoryIds = new();
    private readonly List<int> _buildingIds = new();

    public BoxTestData(TestDatabase db) {
        _db = db;
    }

    // A random name with a prefix, for example "test-category-3f2a...".
    public static string RandomName(string prefix) => prefix + Guid.NewGuid().ToString("N");

    // The store's date, the same one the controllers use.
    public static DateTime Today => StoreClock.Today();

    public async Task<int> CategoryAsync(string? name = null, string? symbol = null) {
        var id = await _db.ScalarAsync<int>(
            "INSERT INTO CATEGORY (name, symbol) VALUES (@name, @symbol); SELECT LAST_INSERT_ID();",
            new { name = name ?? RandomName("test-category-"), symbol });
        _categoryIds.Add(id);
        return id;
    }

    public async Task<int> BuildingAsync(string? name = null) {
        var id = await _db.ScalarAsync<int>(
            "INSERT INTO BUILDING (name) VALUES (@name); SELECT LAST_INSERT_ID();",
            new { name = name ?? RandomName("test-building-") });
        _buildingIds.Add(id);
        return id;
    }

    // A box created by this test's employee. rackedSince defaults to today.
    public async Task<int> BoxAsync(int categoryId, int buildingId, DateTime? rackedSince = null, bool isRacked = true,
            string shelfLetter = "A", int shelfLevel = 1, string fullness = "FULL", string? notes = null) {
        var id = await _db.ScalarAsync<int>("""
            INSERT INTO BOX (category_id, building_id, shelf_letter, shelf_level, fullness, notes, is_racked,
                             racked_since, created_by)
            VALUES (@categoryId, @buildingId, @shelfLetter, @shelfLevel, @fullness, @notes, @isRacked,
                    @rackedSince, @createdBy);
            SELECT LAST_INSERT_ID();
            """,
            new {
                categoryId, buildingId, shelfLetter, shelfLevel, fullness, notes, isRacked,
                rackedSince = (rackedSince ?? Today).Date, createdBy = _db.EmployeeId
            });
        _boxIds.Add(id);
        return id;
    }

    // A box in a new category and building of its own.
    public async Task<int> LoneBoxAsync(DateTime? rackedSince = null, bool isRacked = true) =>
        await BoxAsync(await CategoryAsync(), await BuildingAsync(), rackedSince, isRacked);

    // Marks a box as last changed by an employee, now.
    public Task EditAsync(int boxId, int employeeId) =>
        _db.ExecuteAsync(
            "UPDATE BOX SET last_updated_at = UTC_TIMESTAMP(), last_updated_by = @employeeId WHERE id = @boxId",
            new { boxId, employeeId });

    // Soft-deletes a box, the way DELETE /api/boxes/{id} will.
    public Task SoftDeleteAsync(int boxId, int employeeId) =>
        _db.ExecuteAsync("""
            UPDATE BOX SET deleted_at = UTC_TIMESTAMP(), deleted_by = @employeeId,
                           last_updated_at = UTC_TIMESTAMP(), last_updated_by = @employeeId
            WHERE id = @boxId
            """,
            new { boxId, employeeId });

    // The alert threshold from MASTER_ACCOUNT. Every box query joins that row, so without it no box is ever found.
    public async Task<int> AlertThresholdDaysAsync() {
        var days = await _db.ScalarAsync<int?>("SELECT alert_threshold_days FROM MASTER_ACCOUNT WHERE id = 1");
        Assert.True(days is not null,
            "These tests need a Master account. Run first-time setup, or reset the database to load the sample data.");
        return days!.Value;
    }

    // A box number that doesn't exist.
    public async Task<int> MissingBoxIdAsync() =>
        await _db.ScalarAsync<int>("SELECT COALESCE(MAX(id), 0) + 100000 FROM BOX");

    public async ValueTask DisposeAsync() {
        try {
            await using var cleanup = await _db.NewFactory().OpenConnectionAsync();
            var boxes = _boxIds.ToArray();
            var categories = _categoryIds.ToArray();
            var buildings = _buildingIds.ToArray();
            if (boxes.Length > 0) await cleanup.ExecuteAsync("DELETE FROM BOX WHERE id IN @boxes", new { boxes });
            if (categories.Length > 0)
                await cleanup.ExecuteAsync("DELETE FROM CATEGORY WHERE id IN @categories", new { categories });
            if (buildings.Length > 0)
                await cleanup.ExecuteAsync("DELETE FROM BUILDING WHERE id IN @buildings", new { buildings });
        } catch {
            // Best effort. Rows have random names, so a leftover never collides with another test.
        }
    }
}
