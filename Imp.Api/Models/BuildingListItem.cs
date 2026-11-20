namespace Imp.Api.Models;

// One building in GET /api/buildings (docs/api/buildings.md): its id and name, plus how many boxes use it.
// Active boxes are the ones that aren't soft-deleted, racked or not. Active plus deleted is every box (D-198).
public class BuildingListItem {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ActiveBoxCount { get; set; }
    public int DeletedBoxCount { get; set; }
}
