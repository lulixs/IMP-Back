using Dapper;
using Imp.Api.Data;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

// Categories, from docs/api/categories.md. Takes the session token. Adding, editing, and deleting come later.
[ApiController]
[Route("api/categories")]
[Authorize(Policy = AuthPolicies.Session)]
public class CategoriesController : ControllerBase {
    private readonly IDbConnectionFactory db;

    public CategoriesController(IDbConnectionFactory db) {
        this.db = db;
    }

    // GET /api/categories. Every category, sorted by name.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var categories = await connection.QueryAsync<CategorySummary>(new CommandDefinition(
            "SELECT id, name, symbol FROM CATEGORY ORDER BY name",
            cancellationToken: ct));
        return Ok(new { categories });
    }
}
