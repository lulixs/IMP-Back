namespace Imp.Api.Models;

public class DashboardCategory {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Symbol { get; set; }
    public int Full { get; set; }
    public int Half { get; set; }
    public int Empty { get; set; }
    public int StoredTooLong { get; set; }
}