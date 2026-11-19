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

    // GET /api/categories. Every category, sorted by name, with how many active and soft-deleted boxes use it.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var categories = await connection.QueryAsync<CategoryListItem>(new CommandDefinition("""
            SELECT
                c.id, c.name, c.symbol,
                COUNT(CASE WHEN b.id IS NOT NULL AND b.deleted_at IS NULL THEN 1 END) AS active_box_count,
                COUNT(b.deleted_at) AS deleted_box_count
            FROM CATEGORY AS c
            LEFT JOIN BOX AS b ON b.category_id = c.id
            GROUP BY c.id
            ORDER BY c.name
            """,
            cancellationToken: ct));
        return Ok(new { categories });
    }
}
