namespace Imp.Api.Models;

// One row from the box query in BoxController, one property per column. Flat, not the JSON shape in
// docs/api/boxes.md: the nested category, building, and employee objects are built from it afterward.
public class BoxSummary {
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int BuildingId { get; set; }
    public string ShelfLetter { get; set; } = "";
    public int ShelfLevel { get; set; }
    public string Fullness { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsRacked { get; set; }
    public DateTime RackedSince { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public int? LastUpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
    public string CatName { get; set; } = "";
    public string? CatSymbol { get; set; }
    public string BuildingName { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public string? LastUpdatedByName { get; set; }
    public string? DeletedByName { get; set; }
    public int DaysStored { get; set; }
    public bool StoredTooLong { get; set; }
}
