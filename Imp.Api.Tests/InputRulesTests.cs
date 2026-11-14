using Imp.Api.Security;

namespace Imp.Api.Tests;

// The input rules from docs/api/auth.md, used by setup, tablet setup, and sign-in.
public class InputRulesTests {
    // Email: looks like x@y.z with no spaces, after trimming

    [Theory]
    [InlineData("manager@example.com")]
    [InlineData("a@b.c")]
    [InlineData("first.last+store@thrift.example.org")]
    public void Email_ThatLooksLikeXAtYDotZ_IsValid(string email) {
        Assert.True(InputRules.IsValidEmail(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("manager")]
    [InlineData("manager@example")]
    [InlineData("@example.com")]
    [InlineData("manager@.com")]
    [InlineData("manager@example.")]
    [InlineData("man ager@example.com")]
    [InlineData("manager@exa mple.com")]
    [InlineData("manager@@example.com")]
    [InlineData("manager@example.com\n")]
    public void Email_ThatDoesNotLookLikeXAtYDotZ_IsNotValid(string? email) {
        Assert.False(InputRules.IsValidEmail(email));
    }

    // The column is VARCHAR(255).
    [Fact]
    public void Email_Over255Characters_IsNotValid() {
        var email = new string('a', 250) + "@b.com"; // 256 characters

        Assert.False(InputRules.IsValidEmail(email));
    }

    // Password: at least 8 characters, not trimmed

    [Theory]
    [InlineData("password")]
    [InlineData("master123")]
    [InlineData(" pass word ")] // Spaces inside or at the ends are kept. Passwords are never trimmed.
    public void Password_OfAtLeast8Characters_IsValid(string password) {
        Assert.True(InputRules.IsValidPassword(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("        ")] // 8 spaces: the hasher would never accept it at sign-in
    public void Password_Under8CharactersOrBlank_IsNotValid(string? password) {
        Assert.False(InputRules.IsValidPassword(password));
    }

    // PIN: exactly 6 digits

    [Theory]
    [InlineData("111111")]
    [InlineData("012345")]
    [InlineData("000000")]
    public void Pin_OfExactly6Digits_IsValid(string pin) {
        Assert.True(InputRules.IsValidPin(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("11111")]
    [InlineData("1111111")]
    [InlineData("11111a")]
    [InlineData(" 111111")]
    [InlineData("111111 ")]
    [InlineData("111111\n")]
    [InlineData("١٢٣٤٥٦")] // Arabic-Indic digits are digits to .NET, but not a PIN
    public void Pin_ThatIsNotExactly6Digits_IsNotValid(string? pin) {
        Assert.False(InputRules.IsValidPin(pin));
    }

    // Names: 1 to 100 characters after trimming

    [Theory]
    [InlineData("Front desk tablet")]
    [InlineData("A")]
    public void Name_Of1To100Characters_IsValid(string name) {
        Assert.True(InputRules.IsValidName(name));
    }

    [Fact]
    public void Name_OfExactly100Characters_IsValid() {
        Assert.True(InputRules.IsValidName(new string('a', 100)));
        Assert.False(InputRules.IsValidName(new string('a', 101)));
    }

    // MySQL counts each emoji as one character, so 100 emoji fit even though C# counts 200.
    [Fact]
    public void Name_Of100Emoji_IsValid() {
        var emoji = string.Concat(Enumerable.Repeat("🎄", 100));

        Assert.True(InputRules.IsValidName(emoji));
        Assert.False(InputRules.IsValidName(emoji + "🎄"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Name_ThatIsEmpty_IsNotValid(string? name) {
        Assert.False(InputRules.IsValidName(name));
    }
}
