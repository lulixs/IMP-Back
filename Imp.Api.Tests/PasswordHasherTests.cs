using Imp.Api.Security;

namespace Imp.Api.Tests;

// Tests for BcryptPasswordHasher. The sample hashes below are copies of the ones in
// db/sample_data.sql, so these tests run without Docker or MySQL.
public class PasswordHasherTests {
    private readonly BcryptPasswordHasher _hasher = new();

    // Copied from db/sample_data.sql. If those hashes change, update these too.
    private const string DanielHash = "$2a$12$8H1SlyOc0K7GZs3iAuhUweZZ2FuuFtfV5CvYzAHWJp2oQW.FzU.Aq"; // PIN 111111
    private const string ThomasHash = "$2a$12$lCUr71Ae9moYJlBjMUPgju5b37m6i1pZ8OSmQVpVW95Jljb7OlYDu"; // PIN 222222
    private const string MariaHash = "$2a$12$V2121bK9ObX.ahWDSVwmKuQ4pyNjtrGaI13FUXoeSGgmGBL6.7KAK"; // PIN 333333
    private const string GraceHash = "$2a$12$IpoiVCvcsVwIgmyUgey5s.i.stJGpZ8pXgpo52Tz8fK/96SW5vfGG"; // PIN 444444
    private const string BenHash = "$2a$12$MK9tF/6fEQO5sRt/BblaPOGUpDoaWmffK4LjGzU/NyyxcirrJ.wlK"; // PIN 555555, deactivated
    private const string SamHash = "$2a$12$Xsrg4juLa9YQS26l1r.lWOs3vulkavhB.QbH2qgqSQ92nMxLcUdza"; // PIN 666666
    private const string MasterHash = "$2a$12$rdV81udFbtuzUn29Zovvt.Bhls8RKy2vZ.HEH52VDMPWtR8PWJbHO"; // password master123

    // Every sample account as (secret, hash), shared by the Theory tests below.
    public static TheoryData<string, string> SampleAccounts => new() {
        { "111111", DanielHash },
        { "222222", ThomasHash },
        { "333333", MariaHash },
        { "444444", GraceHash },
        { "555555", BenHash },
        { "666666", SamHash },
        { "master123", MasterHash },
    };

    // Each PIN paired with a different person's hash, so none of them should match.
    public static TheoryData<string, string> MismatchedAccounts => new() {
        { "111111", ThomasHash },
        { "222222", DanielHash },
        { "333333", GraceHash },
        { "444444", MariaHash },
        { "555555", SamHash },
        { "666666", BenHash },
        { "111111", MasterHash },
        { "master123", DanielHash },
    };

    // Hash output

    // A bcrypt hash is always 60 characters and starts with a version like $2a$ or $2b$.
    // The third part is the cost, which BcryptPasswordHasher sets to 12.
    [Fact]
    public void Hash_ReturnsBcryptStringWithCost12() {
        var hash = _hasher.Hash("111111");

        Assert.Equal(60, hash.Length);
        Assert.StartsWith("$2", hash);
        Assert.Equal("12", hash.Split('$')[2]);
    }

    // bcrypt adds a random salt each time, so the same PIN never gives the same hash twice.
    // Both hashes must still verify, since the salt is stored inside the hash.
    [Fact]
    public void Hash_SamePinTwice_GivesDifferentHashesThatBothVerify() {
        var first = _hasher.Hash("222222");
        var second = _hasher.Hash("222222");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("222222", first));
        Assert.True(_hasher.Verify("222222", second));
    }

    // Right and wrong PIN

    // A hash made by the hasher accepts the PIN it came from.
    [Fact]
    public void Verify_RightPin_ReturnsTrue() {
        var hash = _hasher.Hash("333333");

        Assert.True(_hasher.Verify("333333", hash));
    }

    // A hash made by the hasher rejects a different PIN.
    [Fact]
    public void Verify_WrongPin_ReturnsFalse() {
        var hash = _hasher.Hash("333333");

        Assert.False(_hasher.Verify("444444", hash));
    }

    // Sample data hashes

    // Every hash in sample_data.sql matches the PIN or password listed next to it.
    [Theory]
    [MemberData(nameof(SampleAccounts))]
    public void Verify_SampleHash_MatchesItsOwnSecret(string secret, string hash) {
        Assert.True(_hasher.Verify(secret, hash));
    }

    // The sample hashes use cost 12, the same cost BcryptPasswordHasher uses, so test data
    // matches what the API stores. Catches anyone pasting in a hash made at another cost.
    [Theory]
    [MemberData(nameof(SampleAccounts))]
    public void SampleHash_IsBcryptWithCost12(string secret, string hash) {
        _ = secret;
        Assert.Equal(60, hash.Length);
        Assert.StartsWith("$2", hash);
        Assert.Equal("12", hash.Split('$')[2]);
    }

    // One person's PIN must not unlock another person's account.
    [Theory]
    [MemberData(nameof(MismatchedAccounts))]
    public void Verify_PinAgainstSomeoneElsesHash_ReturnsFalse(string secret, string hash) {
        Assert.False(_hasher.Verify(secret, hash));
    }

    // Ben is deactivated, but the hasher only compares a PIN to a hash.
    // Blocking deactivated accounts is the sign-in code's job, not the hasher's.
    [Fact]
    public void Verify_DeactivatedAccountHash_StillVerifies() {
        Assert.True(_hasher.Verify("555555", BenHash));
    }

    // Near misses

    // Inputs that are almost right must still fail: one digit off, wrong letter case,
    // and a space before or after. The hasher must not trim or change case.
    [Theory]
    [InlineData("111112", DanielHash)]
    [InlineData("011111", DanielHash)]
    [InlineData("11111", DanielHash)]
    [InlineData("1111111", DanielHash)]
    [InlineData(" 111111", DanielHash)]
    [InlineData("111111 ", DanielHash)]
    [InlineData("Master123", MasterHash)]
    [InlineData("MASTER123", MasterHash)]
    [InlineData("master12", MasterHash)]
    [InlineData(" master123", MasterHash)]
    [InlineData("master123 ", MasterHash)]
    public void Verify_NearMiss_ReturnsFalse(string secret, string hash) {
        Assert.False(_hasher.Verify(secret, hash));
    }

    // Empty and missing input

    // An empty, null, or whitespace PIN is rejected before bcrypt runs,
    // even against a real hash.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("      ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(null)]
    public void Verify_EmptyOrNullPin_ReturnsFalse(string? pin) {
        Assert.False(_hasher.Verify(pin!, DanielHash));
    }

    // An empty, null, or whitespace stored hash is rejected, for example an account
    // whose hash column was never filled in.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void Verify_EmptyOrNullHash_ReturnsFalse(string? hash) {
        Assert.False(_hasher.Verify("111111", hash!));
    }

    // Broken hashes

    // A stored value that is not a valid bcrypt hash makes the bcrypt library throw.
    // The hasher catches that and returns false, so a bad database row cannot crash sign-in.
    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("111111")]
    [InlineData("$2b$")]
    [InlineData("$2a$12$")]
    [InlineData("$2a$12$8H1SlyOc0K7GZ")]
    [InlineData("$9z$12$8H1SlyOc0K7GZs3iAuhUweZZ2FuuFtfV5CvYzAHWJp2oQW.FzU.Aq")]
    [InlineData("$2a$xx$8H1SlyOc0K7GZs3iAuhUweZZ2FuuFtfV5CvYzAHWJp2oQW.FzU.Aq")]
    [InlineData("2a$12$8H1SlyOc0K7GZs3iAuhUweZZ2FuuFtfV5CvYzAHWJp2oQW.FzU.Aq")]
    public void Verify_BrokenHash_ReturnsFalseWithoutThrowing(string brokenHash) {
        var exception = Record.Exception(() => _hasher.Verify("111111", brokenHash));

        Assert.Null(exception);
        Assert.False(_hasher.Verify("111111", brokenHash));
    }

    // 72 byte limit

    // bcrypt only reads the first 72 bytes of a password and ignores the rest.
    // Two passwords that share their first 72 bytes match the same hash.
    // PINs are 6 digits, so this never matters for them, but it is a known bcrypt limit.
    [Fact]
    public void Verify_OnlyFirst72BytesCount() {
        var first72 = new string('a', 72);
        var hash = _hasher.Hash(first72 + "first-ending");

        Assert.True(_hasher.Verify(first72 + "different-ending", hash));
        Assert.True(_hasher.Verify(first72, hash));
    }

    // A difference inside the first 72 bytes still counts, so the limit only cuts off the end.
    [Fact]
    public void Verify_DifferenceWithinFirst72Bytes_ReturnsFalse() {
        var hash = _hasher.Hash(new string('a', 72) + "tail");
        var changedAtByte72 = new string('a', 71) + "b" + "tail";

        Assert.False(_hasher.Verify(changedAtByte72, hash));
    }
}
