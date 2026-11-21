namespace Imp.Api.Models;

// Request bodies for docs/api/auth.md.
//
// Every property is nullable on purpose. A missing field then reaches the controller, which answers with the
// contract's sentence, instead of ASP.NET Core's general "The request body is invalid."
//
// These are classes, not records, so they never print their passwords or PINs. A record's ToString lists
// every field, and one log line with a request in it would write the password to the log files.

// POST /api/setup
public class SetupRequest {
    public string? MasterEmail { get; set; }
    public string? MasterPassword { get; set; }
    public string? TabletName { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeePin { get; set; }
}

// POST /api/devices
public class NewDeviceRequest {
    public string? TabletName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}

// POST /api/devices/renew and POST /api/master-mode
public class MasterCredentialsRequest {
    public string? Email { get; set; }
    public string? Password { get; set; }
}

// POST /api/sessions
public class SignInRequest {
    public int? EmployeeId { get; set; }
    public string? Pin { get; set; }
}
