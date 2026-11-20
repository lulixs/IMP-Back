using System.Text.Json;
using Imp.Api.Models;
using Xunit;

namespace Imp.Api.Tests;

// Tests that Box.FromRow turns one flat row of the box query into the JSON shape in "The box" section of
// docs/api/boxes.md. No database: the rows are made by hand, with dates the way MySQL sends them (no time zone).
// JSON is written with the same settings ASP.NET Core uses for the API (camelCase names).
public class BoxJsonTests {
    // Setup

    // A row for a box that was created, changed, and soft-deleted, so every field has a value.
    private static BoxSummary FullRow() => new() {
        Id = 6789,
        CategoryId = 1, CatName = "Coats", CatSymbol = "🧥",
        BuildingId = 2, BuildingName = "Processing Center",
        ShelfLetter = "A", ShelfLevel = 2, Fullness = "FULL",
        Notes = "Mostly adult sizes.",
        IsRacked = true,
        RackedSince = new DateTime(2026, 1, 14),
        DaysStored = 299, StoredTooLong = true,
        CreatedAt = new DateTime(2025, 12, 3, 14, 32, 0), CreatedBy = 1, CreatedByName = "Daniel Finkle",
        LastUpdatedAt = new DateTime(2026, 10, 7, 18, 5, 0), LastUpdatedBy = 4, LastUpdatedByName = "Grace Kim",
        DeletedAt = new DateTime(2026, 10, 8, 9, 0, 0), DeletedBy = 2, DeletedByName = "Thomas Anos",
    };

    // A row for a box that was never changed or deleted.
    private static BoxSummary NewRow() {
        var row = FullRow();
        row.Notes = null;
        row.CatSymbol = null;
        row.LastUpdatedAt = null; row.LastUpdatedBy = null; row.LastUpdatedByName = null;
        row.DeletedAt = null; row.DeletedBy = null; row.DeletedByName = null;
        return row;
    }

    private static JsonElement Json(BoxSummary row) =>
        JsonSerializer.SerializeToElement(Box.FromRow(row), JsonSerializerOptions.Web);

    // Field names and order

    [Fact]
    public void Fields_AreTheContractsNamesInTheContractsOrder() {
        var names = Json(FullRow()).EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(new[] {
            "id", "category", "building", "shelfLetter", "shelfLevel", "fullness", "notes", "isRacked",
            "rackedSince", "daysStored", "storedTooLong", "createdAt", "createdBy", "lastUpdatedAt",
            "lastUpdatedBy", "deletedAt", "deletedBy"
        }, names);
    }

    [Fact]
    public void NestedObjects_HaveTheContractsFields() {
        var json = Json(FullRow());

        Assert.Equal(new[] { "id", "name", "symbol" }, json.GetProperty("category").EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "id", "name" }, json.GetProperty("building").EnumerateObject().Select(p => p.Name));
        foreach (var employee in new[] { "createdBy", "lastUpdatedBy", "deletedBy" })
            Assert.Equal(new[] { "id", "name" }, json.GetProperty(employee).EnumerateObject().Select(p => p.Name));
    }

    // Values

    [Fact]
    public void PlainFields_CopyTheRow() {
        var json = Json(FullRow());

        Assert.Equal(6789, json.GetProperty("id").GetInt32());
        Assert.Equal("A", json.GetProperty("shelfLetter").GetString());
        Assert.Equal(2, json.GetProperty("shelfLevel").GetInt32());
        Assert.Equal("FULL", json.GetProperty("fullness").GetString());
        Assert.Equal("Mostly adult sizes.", json.GetProperty("notes").GetString());
        Assert.True(json.GetProperty("isRacked").GetBoolean());
        Assert.Equal(299, json.GetProperty("daysStored").GetInt32());
        Assert.True(json.GetProperty("storedTooLong").GetBoolean());
    }

    [Fact]
    public void NestedObjects_AreBuiltFromTheRowsColumns() {
        var json = Json(FullRow());

        var category = json.GetProperty("category");
        Assert.Equal(1, category.GetProperty("id").GetInt32());
        Assert.Equal("Coats", category.GetProperty("name").GetString());
        Assert.Equal("🧥", category.GetProperty("symbol").GetString());

        var building = json.GetProperty("building");
        Assert.Equal(2, building.GetProperty("id").GetInt32());
        Assert.Equal("Processing Center", building.GetProperty("name").GetString());

        Assert.Equal(1, json.GetProperty("createdBy").GetProperty("id").GetInt32());
        Assert.Equal("Daniel Finkle", json.GetProperty("createdBy").GetProperty("name").GetString());
        Assert.Equal(4, json.GetProperty("lastUpdatedBy").GetProperty("id").GetInt32());
        Assert.Equal("Grace Kim", json.GetProperty("lastUpdatedBy").GetProperty("name").GetString());
        Assert.Equal(2, json.GetProperty("deletedBy").GetProperty("id").GetInt32());
        Assert.Equal("Thomas Anos", json.GetProperty("deletedBy").GetProperty("name").GetString());
    }

    // Dates

    [Fact]
    public void RackedSince_IsADateWithNoTime() {
        Assert.Equal("2026-01-14", Json(FullRow()).GetProperty("rackedSince").GetString());
    }

    [Fact]
    public void Timestamps_AreUtcAndEndInZ() {
        var json = Json(FullRow());

        Assert.Equal("2025-12-03T14:32:00Z", json.GetProperty("createdAt").GetString());
        Assert.Equal("2026-10-07T18:05:00Z", json.GetProperty("lastUpdatedAt").GetString());
        Assert.Equal("2026-10-08T09:00:00Z", json.GetProperty("deletedAt").GetString());
    }

    // Nulls

    [Fact]
    public void NeverChangedBox_HasNullForTheChangeAndDeleteFields() {
        var json = Json(NewRow());

        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastUpdatedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastUpdatedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("deletedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("deletedBy").ValueKind);
    }

    [Fact]
    public void MissingNotesAndSymbol_AreNull() {
        var json = Json(NewRow());

        Assert.Equal(JsonValueKind.Null, json.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("category").GetProperty("symbol").ValueKind);
    }

    [Fact]
    public void EmployeeWithNoName_StillHasAnObject() {
        // Can't happen with the real query (an id always joins to a name), but an id with no name must not
        // turn into a null employee.
        var row = FullRow();
        row.LastUpdatedByName = null;

        var lastUpdatedBy = Json(row).GetProperty("lastUpdatedBy");

        Assert.Equal(4, lastUpdatedBy.GetProperty("id").GetInt32());
        Assert.Equal("", lastUpdatedBy.GetProperty("name").GetString());
    }
}
