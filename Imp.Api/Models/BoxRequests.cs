namespace Imp.Api.Models;

public class NewBoxRequest {
    public int? CategoryID { get; set; }
    public int? BuildingID { get; set; }
    public string? ShelfLetter { get; set; }
    public int? ShelfLevel { get; set; }
    public string? Fullness { get; set; }
    public string? Notes { get; set; }
    public string? RackedSince { get; set; }
}

