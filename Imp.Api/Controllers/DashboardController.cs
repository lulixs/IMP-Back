using Dapper;
using Imp.Api.Data;
using Imp.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase {
    private readonly IDbConnectionFactory db;

    public DashboardController(IDbConnectionFactory db) {
        this.db = db;
    }

    // GET /api/dashboard
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);

        var alertThresholdDays = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT alert_threshold_days FROM MASTER_ACCOUNT",
                cancellationToken: ct));

        var categories = await connection.QueryAsync<DashboardCategory>(
            // I Blame Luke for my pain and suffering
            new CommandDefinition("""
                SELECT 
                    c.*, 
                    COUNT(CASE WHEN b.fullness = 'FULL' THEN 1 END) AS 'full', 
                    COUNT(CASE WHEN b.fullness = 'HALF' THEN 1 END) AS 'half', 
                    COUNT(CASE WHEN b.fullness = 'EMPTY' THEN 1 END) AS 'empty', 
                    COUNT(CASE WHEN DATEDIFF(CURDATE(), b.racked_since) >= @alertThresholdDays THEN 1 END) AS 'storedTooLong' 
                FROM CATEGORY AS c 
                LEFT JOIN BOX AS b ON b.category_id = c.id AND b.is_racked = TRUE AND b.deleted_at is NULL 
                GROUP BY c.id 
                ORDER BY c.name;
            """, new { alertThresholdDays }, cancellationToken: ct));
        return Ok(new {alertThresholdDays, categories});
    }
}