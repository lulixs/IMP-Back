using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using Imp.Api.Data;
using Imp.Api.Models;
using MySqlConnector;
using Xunit;

namespace Imp.Api.Tests;

// Use [DatabaseFact] and [DatabaseTheory] instead of [Fact] and [Theory] for tests that need MySQL.
// If the database is not running, those tests are skipped with a message instead of failing.
public sealed class DatabaseFactAttribute : FactAttribute {
    public DatabaseFactAttribute() {
        if (!TestDatabase.IsAvailable) Skip = TestDatabase.UnavailableMessage;
    }
}

public sealed class DatabaseTheoryAttribute : TheoryAttribute {
    public DatabaseTheoryAttribute() {
        if (!TestDatabase.IsAvailable) Skip = TestDatabase.UnavailableMessage;
    }
}

// One row of AUDIT_LOG, read back with plain column values so the tests check exactly what is stored.
public class AuditRow {
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int EmployeeId { get; set; }
    public bool IsElevated { get; set; }
    public int DeviceId { get; set; }
    public string ActionType { get; set; } = "";
    public string TargetType { get; set; } = "";
    public int TargetId { get; set; }
    public string TargetLabel { get; set; } = "";
    public string? Details { get; set; }
}

// Gives each test its own employee and tablet (device), a connection, and a cleanup at the end.
//
// Safe to run against the Docker database: every row a test creates has a random name and is
// removed in DisposeAsync, and the sample data is never touched. Most tests also run inside a
// transaction that is rolled back, so they leave nothing behind even if cleanup were skipped.
//
// Usage:  await using var db = await TestDatabase.CreateAsync();
public sealed class TestDatabase : IAsyncDisposable {
    // Matches db/docker-compose.yml. Set IMP_TEST_CONNECTION to point the tests somewhere else.
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("IMP_TEST_CONNECTION")
        ?? "Server=localhost;Port=3307;Database=IMP;User=imp;Password=imp";

    public static bool IsAvailable { get; private set; }
    public static string UnavailableMessage { get; private set; } = "";

    static TestDatabase() {
        try {
            var builder = new MySqlConnectionStringBuilder(ConnectionString) { ConnectionTimeout = 2 };
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM AUDIT_LOG LIMIT 1";
            command.ExecuteScalar();
            IsAvailable = true;
        } catch (Exception ex) {
            IsAvailable = false;
            UnavailableMessage =
                "MySQL is not reachable or the IMP tables are missing. " +
                "Start it with: cd db && docker compose up -d, and wait for (healthy). " +
                "(" + ex.Message + ")";
        }
    }

    private readonly MySqlConnectionFactory _factory = new(ConnectionString);
    private readonly List<DbConnection> _connections = new();
    private readonly List<DbTransaction> _transactions = new();
    private readonly List<int> _employeeIds = new();
    private readonly List<int> _deviceIds = new();

    // The connection the test works on, the open transaction on it (if any), and the
    // employee and tablet that audit entries are written for.
    public DbConnection Connection { get; private set; } = null!;
    public DbTransaction? Transaction { get; private set; }
    public int EmployeeId { get; private set; }
    public int DeviceId { get; private set; }

    private TestDatabase() { }

    public static async Task<TestDatabase> CreateAsync() {
        var db = new TestDatabase();
        db.Connection = await db._factory.OpenConnectionAsync();
        db._connections.Add(db.Connection);
        db.EmployeeId = await db.CreateEmployeeAsync();
        db.DeviceId = await db.CreateDeviceAsync();
        return db;
    }

    // A factory for tests that need many connections at once.
    public MySqlConnectionFactory NewFactory() => new(ConnectionString);

    // A second connection, to check what other users of the database can see.
    public async Task<DbConnection> OpenAnotherAsync() {
        var connection = await _factory.OpenConnectionAsync();
        _connections.Add(connection);
        return connection;
    }

    // Transactions

    public async Task<DbTransaction> BeginAsync() {
        Transaction = await Connection.BeginTransactionAsync();
        _transactions.Add(Transaction);
        return Transaction;
    }

    // Starts a transaction on some other connection (and rolls it back at the end of the test).
    public async Task<DbTransaction> BeginOnAsync(DbConnection connection) {
        var transaction = await connection.BeginTransactionAsync();
        _transactions.Add(transaction);
        return transaction;
    }

    public async Task CommitAsync() {
        await Transaction!.CommitAsync();
        Transaction = null;
    }

    public async Task RollbackAsync() {
        await Transaction!.RollbackAsync();
        Transaction = null;
    }

    // Setup rows

    public async Task<int> CreateEmployeeAsync() {
        var id = await ScalarAsync<int>(
            "INSERT INTO EMPLOYEE_ACCOUNT (name, pin_hash) VALUES (@name, 'not-a-real-hash'); SELECT LAST_INSERT_ID();",
            new { name = "test-employee-" + Guid.NewGuid().ToString("N") });
        _employeeIds.Add(id);
        return id;
    }

    public async Task<int> CreateDeviceAsync() {
        var id = await ScalarAsync<int>(
            "INSERT INTO DEVICE (device_name) VALUES (@name); SELECT LAST_INSERT_ID();",
            new { name = "test-device-" + Guid.NewGuid().ToString("N") });
        _deviceIds.Add(id);
        return id;
    }

    // A valid entry (ADDED on a BOX) for this test's employee and tablet. Change what the test cares about.
    public AuditEntry NewEntry() => new() {
        EmployeeId = EmployeeId,
        IsElevated = false,
        DeviceId = DeviceId,
        ActionType = AuditAction.ADDED,
        TargetType = AuditTarget.BOX,
        TargetId = 1234,
        TargetLabel = "#1234",
        Details = null
    };

    // Shorthand for AuditLog.WriteAsync on this test's connection and transaction. Call BeginAsync first.
    public Task WriteAsync(AuditEntry entry) =>
        AuditLog.WriteAsync(Connection, entry, Transaction ?? throw new InvalidOperationException("Call BeginAsync first."));

    // Reading

    private const string RowSql = """
        SELECT id AS Id, `timestamp` AS Timestamp, employee_id AS EmployeeId, is_elevated AS IsElevated,
               device_id AS DeviceId, action_type AS ActionType, target_type AS TargetType,
               target_id AS TargetId, target_label AS TargetLabel, details AS Details
        FROM AUDIT_LOG
        WHERE employee_id = @employeeId
        ORDER BY id
        """;

    // All audit rows for an employee (this test's by default), oldest first.
    // Pass another connection to see what outside readers see. Without one, the test's own
    // connection and open transaction are used.
    public async Task<List<AuditRow>> RowsAsync(DbConnection? on = null, int? employeeId = null) {
        var connection = on ?? Connection;
        var transaction = on is null ? Transaction : null;
        var rows = await connection.QueryAsync<AuditRow>(
            new CommandDefinition(RowSql, new { employeeId = employeeId ?? EmployeeId }, transaction));
        return rows.ToList();
    }

    public Task<int> ExecuteAsync(string sql, object? parameters = null) =>
        Connection.ExecuteAsync(new CommandDefinition(sql, parameters, Transaction));

    public async Task<T?> ScalarAsync<T>(string sql, object? parameters = null, DbConnection? on = null) {
        var connection = on ?? Connection;
        var transaction = on is null ? Transaction : null;
        return await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, transaction));
    }

    // The values an ENUM column of AUDIT_LOG allows, read from the database itself.
    public async Task<List<string>> EnumValuesAsync(string column) {
        var columnType = await ScalarAsync<string>(
            "SELECT COLUMN_TYPE FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AUDIT_LOG' AND COLUMN_NAME = @column",
            new { column });
        return Regex.Matches(columnType ?? "", "'([^']+)'").Select(m => m.Groups[1].Value).ToList();
    }

    // Cleanup

    public async ValueTask DisposeAsync() {
        // Roll back and close everything first, so no open transaction is holding locks
        // that would make the deletes below wait.
        foreach (var transaction in _transactions) {
            try { await transaction.RollbackAsync(); } catch { /* already finished */ }
            try { await transaction.DisposeAsync(); } catch { /* already finished */ }
        }
        foreach (var connection in _connections) {
            try { await connection.DisposeAsync(); } catch { /* already closed */ }
        }

        // Then remove what this test created, children before parents.
        try {
            await using var cleanup = await _factory.OpenConnectionAsync();
            var employees = _employeeIds.ToArray();
            var devices = _deviceIds.ToArray();
            if (employees.Length > 0 || devices.Length > 0) {
                await cleanup.ExecuteAsync(
                    "DELETE FROM AUDIT_LOG WHERE employee_id IN @employees OR device_id IN @devices",
                    new { employees, devices });
            }
            if (devices.Length > 0)
                await cleanup.ExecuteAsync("DELETE FROM DEVICE WHERE id IN @devices", new { devices });
            if (employees.Length > 0)
                await cleanup.ExecuteAsync("DELETE FROM EMPLOYEE_ACCOUNT WHERE id IN @employees", new { employees });
        } catch {
            // Best effort. Rows have random names, so a leftover never collides with another test.
        }
    }
}
