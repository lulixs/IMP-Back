using System.Data.Common;
using Dapper;
using Imp.Api.Security;

namespace Imp.Api.Data;

// Reads the single MASTER_ACCOUNT row. Used by setup, tablet setup, renew, and later Master mode.
public static class MasterAccount {
    // The sentence for a wrong email or password, from docs/api/auth.md.
    public const string WrongCredentials = "Incorrect email or password. Try again.";

    // True once first-time setup has run, since that is what creates the row.
    public static async Task<bool> ExistsAsync(DbConnection connection, CancellationToken ct) {
        var count = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT COUNT(*) FROM MASTER_ACCOUNT", cancellationToken: ct));
        return count > 0;
    }

    // True only when both the email and the password match. The email is compared without caring about
    // spaces at the ends or capital letters. bcrypt runs even when the email is wrong, so a wrong email
    // takes as long as a wrong password and doesn't give away which one was wrong.
    public static async Task<bool> CheckCredentialsAsync(DbConnection connection, IPasswordHasher hasher,
        string? email, string? password, CancellationToken ct) {
        var account = await connection.QuerySingleOrDefaultAsync<Credentials>(new CommandDefinition(
            "SELECT email, password_hash FROM MASTER_ACCOUNT WHERE id = 1", cancellationToken: ct));
        if (account is null) return false;

        var passwordMatches = hasher.Verify(password ?? "", account.PasswordHash);
        var emailMatches = string.Equals(email?.Trim(), account.Email, StringComparison.OrdinalIgnoreCase);
        return passwordMatches && emailMatches;
    }

    internal sealed class Credentials {
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }
}
