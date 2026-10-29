# IMP-Back

The backend for IMP (Inventory Management Platform). It is a web API built with ASP.NET Core on .NET 10. The Flutter app ([ToP-Front](https://github.com/lulixs/ToP-Front)) calls it over HTTP and gets JSON back.

**Status:** skeleton only. The one working endpoint is `GET /api/health`. There is no database yet.

## Who does what

| Area | Owner | Where |
|---|---|---|
| Database: MySQL in Docker, tables from ERD v2.5.1, sample data | Noah | `docker-compose.yml`, `db/` (to be added) |
| Query functions: C# code that reads the database | Nick | `Imp.Api/` |
| REST connection: Flutter code that calls this API | Edwin | ToP-Front repo |

## Setup (once per computer)

1. **Install the .NET 10 SDK.** Any 10.0.x version works.
   - Windows or Mac: download the SDK from https://dotnet.microsoft.com/download
   - Arch Linux: `sudo pacman -S dotnet-sdk aspnet-runtime aspnet-targeting-pack`
   - Check it: `dotnet --list-sdks` should show a 10.0 version.
2. **Install VS Code** with the **C# Dev Kit** extension.
3. **Install Docker** (needed once the database is added).
4. **Clone the repo:**
   ```
   git clone git@github.com:lulixs/IMP-Back.git
   cd IMP-Back
   ```

## Run it

From the repo folder:

```
dotnet run --project Imp.Api
```

Wait for `Now listening on: http://localhost:5000`, then open:

- http://localhost:5000/api/health shows `{"status":"ok"}` if the server is running.
- http://localhost:5000/swagger lists every endpoint and lets you send test requests from the browser.

Stop the server with `Ctrl+C`. After changing code, stop and run it again.

## Project layout

```
IMP-Back/
├── README.md
├── .gitignore
└── Imp.Api/                          the web API project
    ├── Program.cs                    startup: registers services and sets up request handling
    ├── Controllers/                  one class per area, each method handles one URL
    │   └── HealthController.cs       GET /api/health
    ├── appsettings.json              settings used everywhere
    ├── appsettings.Development.json  settings used only on your computer (database login)
    ├── Properties/launchSettings.json  port 5000, Development mode
    └── Imp.Api.csproj                .NET version and packages
```

## How a request works

1. The app sends a request, for example `GET /api/health`.
2. ASP.NET Core matches the URL against the `[Route]` and `[HttpGet]` attributes on the controllers. URLs are not file paths.
3. The matching method runs and returns a result, for example `Ok(...)` for status 200.
4. ASP.NET Core turns the result into JSON and sends it back.

To add an endpoint, add a method to a controller (or a new class in `Controllers/` whose name ends in `Controller`). It shows up in Swagger automatically.

## Calling the API from the Flutter app

Use the address that matches where the app is running:

| App running on | Backend address |
|---|---|
| Chrome or Linux desktop | `http://localhost:5000` |
| Android emulator | `http://10.0.2.2:5000` |
| A real tablet on the same Wi-Fi | `http://<this computer's IP>:5000` (also change `localhost` to `0.0.0.0` in `launchSettings.json`) |

Keep the address in one setting in the app, because it changes when the real server is set up.

**Android blocks plain HTTP by default.** Add `android:usesCleartextTraffic="true"` to the `<application>` tag in ToP-Front's `android/app/src/debug/AndroidManifest.xml`.

**Chrome:** the API allows requests from any web page while in Development mode (CORS), so `flutter run -d chrome` works. Other platforms do not need this.

## Database connection

The connection details live in `Imp.Api/appsettings.Development.json` under the name `IMP`:

```json
"ConnectionStrings": {
  "IMP": "Server=localhost;Port=3307;Database=IMP;User=imp;Password=imp"
}
```

- These match the MySQL container in `Database/docker-compose.yml`. If you change the port or login there, change them here too.
- C# code reads it with `builder.Configuration.GetConnectionString("IMP")`. Never write the login into the code itself.
- These values only work against the MySQL container on your own computer, so committing them is fine.
- **Never commit the real server's password.** On the server, the connection string is set with an environment variable named `ConnectionStrings__IMP`, and this file is not used.

## Database setup

*To be written by the database owner:* how to start MySQL with Docker, where the table and sample data scripts are, and how to reset the database. Keep the table scripts and the sample data scripts in separate files, so the table scripts can be reused on the real server without the sample data.

## HTTP now, HTTPS later

The API runs on plain HTTP while we develop on our own computers (decision D-136). Before the beta, the real server will use HTTPS through Nginx, which sits in front of the API on the server and handles the encryption. The API itself stays on HTTP behind Nginx, so **do not add HTTPS redirects or certificates to this project.** The Flutter app's address will change to an `https://` address at that point.

## Common problems

| Problem | Fix |
|---|---|
| `Specify a project or solution file` | Add `--project Imp.Api` to the command, or run it inside `Imp.Api/`. |
| `NETSDK1226: Prune Package data not found` (Arch Linux) | Install `aspnet-runtime` and `aspnet-targeting-pack` (see Setup). |
| `address already in use` on port 5000 (Mac) | Turn off AirPlay Receiver in System Settings, General, AirDrop & Handoff. |
| Flutter in Chrome shows a CORS error | Check the API is running in Development mode (`dotnet run` does this). |
| Android app cannot reach the API | Use `10.0.2.2` instead of `localhost` on the emulator, and add the cleartext setting above. |
