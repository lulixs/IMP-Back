namespace Imp.Api.Models;

// A category's id, name, and symbol, the shape in docs/api/categories.md. Used inside a box. The category list adds box counts (CategoryListItem).
public class CategorySummary {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Symbol { get; set; }
}
