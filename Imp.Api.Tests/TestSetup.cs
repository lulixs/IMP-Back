using System.Runtime.CompilerServices;

namespace Imp.Api.Tests;

// Runs once, when the test project loads, before any test.
// Program.cs turns this on for the real API, and much of the code relies on it: for example
// MasterAccount reads password_hash into PasswordHash, SessionsController reads pin_hash into PinHash,
// and DevicesController reads removed_at into RemovedAt. The tests never run Program.cs, so without this
// those columns stay empty and, for example, no Master password ever matches.
// If you change the line in Program.cs, change it here too.
internal static class TestSetup {
    [ModuleInitializer]
    internal static void MatchColumnNamesWithUnderscores() {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
