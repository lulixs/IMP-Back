namespace Imp.Api.Models;

// A building's id and name, the shape in docs/api/buildings.md. Used inside a box. The building list adds box counts (BuildingListItem).
public class BuildingSummary {
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
