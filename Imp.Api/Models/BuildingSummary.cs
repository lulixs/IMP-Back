namespace Imp.Api.Models;

// A building's id and name, the shape in docs/api/buildings.md. Used by GET /api/buildings and inside a box.
public class BuildingSummary {
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
