using System.Text.RegularExpressions;

namespace Imp.Api.Security;

// The input rules from docs/api/auth.md, in one place so every endpoint checks them the same way.
// Names and emails are trimmed by the caller before they are checked. PINs and passwords are never trimmed.
public static class InputRules {
    // Matches VARCHAR(100) in schema.sql.
    public const int MaxNameLength = 100;
    // Matches MASTER_ACCOUNT.email VARCHAR(255).
    public const int MaxEmailLength = 255;
    public const int MinPasswordLength = 8;

    // x@y.z with no spaces. \z instead of $ so a trailing line break doesn't slip through.
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+\z", RegexOptions.CultureInvariant);
    // Exactly 6 digits, 0 to 9 only. A string, so a PIN like 012345 keeps its leading zero.
    private static readonly Regex PinPattern = new(@"^[0-9]{6}\z", RegexOptions.CultureInvariant);

    public static bool IsValidEmail(string? trimmedEmail) =>
        trimmedEmail is not null && CharacterCount(trimmedEmail) <= MaxEmailLength && EmailPattern.IsMatch(trimmedEmail);

    // All spaces is refused too, because BcryptPasswordHasher.Verify never accepts a blank password,
    // so the Master could never sign in with it.
    public static bool IsValidPassword(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length >= MinPasswordLength;

    public static bool IsValidPin(string? pin) =>
        pin is not null && PinPattern.IsMatch(pin);

    // 1 to 100 characters after trimming. Used for tablet and employee names.
    public static bool IsValidName(string? trimmedName) =>
        !string.IsNullOrEmpty(trimmedName) && CharacterCount(trimmedName) <= MaxNameLength;

    // MySQL counts characters, not C# string length, so an emoji counts as 1 here, not 2.
    private static int CharacterCount(string text) => text.EnumerateRunes().Count();
}
