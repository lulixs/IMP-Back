namespace Imp.Api.Security;

// Uses bcrypt 12 to create randomized salt for hash
public class BcryptPasswordHasher : IPasswordHasher {
    private const int cost = 12;

    // Hashes pin with cost 12
    public string Hash(string pin) {
        return BCrypt.Net.BCrypt.HashPassword(pin, cost);
    }

    // Verifies whether pin matches stored hash
    public bool Verify(string pin, string storedHash) {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(storedHash)) {
            return false;
        }
        try {
            return BCrypt.Net.BCrypt.Verify(pin, storedHash);
        }
        catch {
            return false;
        }

    }

}