namespace Imp.Api.Models;

// A box in the shape every box endpoint returns ("The box" in docs/api/boxes.md). The properties are in the
// contract's order, so the JSON fields come out in that order too.
public class Box {
    public int Id { get; set; }
    public CategorySummary Category { get; set; } = new();
    public BuildingSummary Building { get; set; } = new();
    public string ShelfLetter { get; set; } = "";
    public int ShelfLevel { get; set; }
    public string Fullness { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsRacked { get; set; }
    public DateOnly RackedSince { get; set; }
    public int DaysStored { get; set; }
    public bool StoredTooLong { get; set; }
    public DateTime CreatedAt { get; set; }
    public EmployeeSummary CreatedBy { get; set; } = new();
    public DateTime? LastUpdatedAt { get; set; }
    public EmployeeSummary? LastUpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public EmployeeSummary? DeletedBy { get; set; }

    // Builds the nested shape from one flat row of the box query.
    public static Box FromRow(BoxSummary row) {
        return new Box {
            Id = row.Id,
            Category = new CategorySummary { Id = row.CategoryId, Name = row.CatName, Symbol = row.CatSymbol },
            Building = new BuildingSummary { Id = row.BuildingId, Name = row.BuildingName },
            ShelfLetter = row.ShelfLetter,
            ShelfLevel = row.ShelfLevel,
            Fullness = row.Fullness,
            Notes = row.Notes,
            IsRacked = row.IsRacked,
            RackedSince = DateOnly.FromDateTime(row.RackedSince),
            DaysStored = row.DaysStored,
            StoredTooLong = row.StoredTooLong,
            CreatedAt = AsUtc(row.CreatedAt),
            CreatedBy = new EmployeeSummary { Id = row.CreatedBy, Name = row.CreatedByName },
            LastUpdatedAt = row.LastUpdatedAt == null ? null : AsUtc(row.LastUpdatedAt.Value),
            LastUpdatedBy = Employee(row.LastUpdatedBy, row.LastUpdatedByName),
            DeletedAt = row.DeletedAt == null ? null : AsUtc(row.DeletedAt.Value),
            DeletedBy = Employee(row.DeletedBy, row.DeletedByName),
        };
    }

    // The DATETIME columns hold UTC (written with UTC_TIMESTAMP()), but MySQL sends them without a time zone.
    // Marking them UTC makes the JSON end in Z, as the contract asks.
    private static DateTime AsUtc(DateTime time) {
        return DateTime.SpecifyKind(time, DateTimeKind.Utc);
    }

    // Null when the column is null (never edited, or not deleted), so the JSON has null instead of an empty employee.
    private static EmployeeSummary? Employee(int? id, string? name) {
        return id == null ? null : new EmployeeSummary { Id = id.Value, Name = name ?? "" };
    }
}
