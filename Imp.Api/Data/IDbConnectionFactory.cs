using System.Data.Common;

namespace Imp.Api.Data;

// Creates database connections
public interface IDbConnectionFactory {
    // Opens a new connection
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
