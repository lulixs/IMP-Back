using Dapper;
using Imp.Api.Data;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

// Employees, from docs/api/auth.md. Adding and editing employees (Master pages) come later.
[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase {
    private readonly IDbConnectionFactory db;

    public EmployeesController(IDbConnectionFactory db) {
        this.db = db;
    }

    // GET /api/employees/active. The names on the employee sign-in screen: every activated employee,
    // sorted by name. Deactivated employees are left out.
    [HttpGet("active")]
    [Authorize(Policy = AuthPolicies.Device)]
    public async Task<IActionResult> GetActive(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var employees = await connection.QueryAsync<EmployeeSummary>(new CommandDefinition(
            "SELECT id, name FROM EMPLOYEE_ACCOUNT WHERE is_activated = TRUE ORDER BY name",
            cancellationToken: ct));
        return Ok(new { employees });
    }
}
