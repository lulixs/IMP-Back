namespace Imp.Api.Models;

// One category in GET /api/categories (docs/api/categories.md): its id, name, and symbol, plus how many boxes use it.
// Active boxes are the ones that aren't soft-deleted, racked or not. Active plus deleted is every box (D-198).
public class CategoryListItem {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Symbol { get; set; }
    public int ActiveBoxCount { get; set; }
    public int DeletedBoxCount { get; set; }
}
