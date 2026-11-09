namespace Imp.Api.Data;

using System.Data.Common;
using MySqlConnector;

public class MySqlConnectionFactory : IDbConnectionFactory {
    private readonly string _connectionString;

    public MySqlConnectionFactory(string connectionString) {
        // Fail fast if connection string is missing
        if (string.IsNullOrWhiteSpace(connectionString)) {
            throw new ArgumentException("The connection string is empty.", nameof(connectionString));
        }

        // Use the string from appsettings
        var builder = new MySqlConnectionStringBuilder(connectionString) {
            ConnectionTimeout = 5
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) {
        var connection = new MySqlConnection(_connectionString);
        try {
            await connection.OpenAsync(cancellationToken);
            return connection;
        } catch {
            await connection.DisposeAsync();
            throw;
        }
    }
}
