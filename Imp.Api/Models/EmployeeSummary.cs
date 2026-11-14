namespace Imp.Api.Models;

// One name on the employee sign-in screen, from GET /api/employees/active.
public class EmployeeSummary {
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
