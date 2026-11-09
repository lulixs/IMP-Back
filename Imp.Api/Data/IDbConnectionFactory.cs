namespace Imp.Api.Data;

using System.Data.Common;

// Creates database connections
public interface IDbConnectionFactory {
    // Opens a new connection
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
