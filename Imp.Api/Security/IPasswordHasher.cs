namespace Imp.Api.Security;

// Interface for Password Hash and Verify functionalities
public interface IPasswordHasher {
    // Receives pin and returns a hash string
    string Hash(string pin);

    // Receives pin and stored hash, returns true if they match
    bool Verify(string pin, string storedHash);
}

