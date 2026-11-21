namespace Imp.Api.Models;

// Fields POST and PUT share, checked by BoxController.CheckFields.
public abstract class BoxFields {
    public int? CategoryID { get; set; }
    public int? BuildingID { get; set; }
    public string? ShelfLetter { get; set; }
    public int? ShelfLevel { get; set; }
    public string? Fullness { get; set; }
    public string? Notes { get; set; }
}

// POST /api/boxes
public class NewBoxRequest : BoxFields {
    public string? RackedSince { get; set; }
}

// PUT /api/boxes/{id}
public class SaveBoxRequest : BoxFields {
    public bool? IsRacked { get; set; }
    // only used on re-rack
    public bool? ResetRackedSince { get; set; }
    // as loaded. null = never edited. must be sent, even as null (missing -> 400)
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime? LastUpdatedAt { get; set; }
}
