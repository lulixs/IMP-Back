# IMP-Back

The backend for IMP (Inventory Management Platform). It is a web API built with ASP.NET Core on .NET 10. The Flutter app ([IMP-Front](https://github.com/lulixs/IMP-Front)) calls it over HTTP and gets JSON back.

**Status:** early. The working endpoints are `GET /api/health`, `GET /api/health/database`, and `GET /api/dashboard`, the first endpoint that reads real data. Its JSON shape is in [docs/api/dashboard.md](docs/api/dashboard.md). The MySQL database runs in Docker (see [Database setup](#database-setup)), and the API connects to it (see [Database connection](#database-connection)). Contracts for sign-in ([docs/api/auth.md](docs/api/auth.md)), boxes ([docs/api/boxes.md](docs/api/boxes.md)), and the category and building lists are written but not built yet. The shared error format in [docs/api/README.md](docs/api/README.md) isn't built yet either.

## Who does what

Roles rotate weekly (D-143). The Server role owns `db/` and `Imp.Api/`, and the Connect role owns the app code in IMP-Front that calls this API. Who holds each role this week is on the team roadmap.

## Setup (once per computer)

1. **Install the .NET 10 SDK.** Any 10.0.x version works.
   - Windows or Mac: download the SDK from https://dotnet.microsoft.com/download
   - Arch Linux: `sudo pacman -S dotnet-sdk aspnet-runtime aspnet-targeting-pack`
   - Check it: `dotnet --list-sdks` should show a 10.0 version.
2. **Install VS Code** with the **C# Dev Kit** extension.
3. **Install Docker,** which runs the database.
   - Windows or Mac: install Docker Desktop from https://www.docker.com/products/docker-desktop/ and leave it open while you work. Windows details are in [db/DOCKER_GUIDE.md](db/DOCKER_GUIDE.md).
   - Arch Linux: `sudo pacman -S docker docker-compose`, then `sudo systemctl enable --now docker` and `sudo usermod -aG docker $USER`. Log out and back in afterward.
   - Check it: `docker ps` should print an empty table, not an error.
4. **Clone the repo:**
   ```
   git clone git@github.com:lulixs/IMP-Back.git
   cd IMP-Back
   ```

## Run it

Start the database first (see [Database setup](#database-setup)) and wait for `(healthy)`. Then, from the repo folder:

```
dotnet run --project Imp.Api
```

Wait for `Now listening on: http://localhost:5000`, then open:

- http://localhost:5000/api/health shows `{"status":"ok"}` if the server is running.
- http://localhost:5000/api/health/database shows `{"database":"ok"}` if the API can reach MySQL
- http://localhost:5000/api/dashboard shows the box counts for every category (needs the database running).
- http://localhost:5000/swagger lists every endpoint and lets you send test requests from the browser.

Stop the server with `Ctrl+C`. After changing code, stop and run it again.

## Project layout

```
IMP-Back/
├── README.md
├── .gitignore
├── IMP-Back.slnx                     the solution file, which lists both projects so dotnet test finds them
├── db/                               the MySQL database, run with Docker
│   ├── docker-compose.yml            how Docker runs MySQL (port 3307, login imp / imp)
│   ├── schema.sql                    creates the tables, from ERD v2.6
│   ├── sample_data.sql               test data for your computer only
│   └── DOCKER_GUIDE.md               step-by-step Docker help for Windows
├── docs/api/                         the JSON each endpoint returns, agreed with the Flutter side
│   ├── README.md                     rules every contract shares: JSON, tokens, and the error format
│   ├── auth.md                       first-time setup, tablets, sign-in and sign-out, Master mode
│   ├── boxes.md                      search, open, create, edit, and soft-delete boxes
│   ├── buildings.md                  the building list
│   ├── categories.md                 the category list
│   └── dashboard.md                  GET /api/dashboard
├── Imp.Api/                          the web API project
│   ├── Program.cs                    startup: registers services and sets up request handling
│   ├── Controllers/                  one class per area, each method handles one URL
│   │   ├── HealthController.cs       GET /api/health and GET /api/health/database
│   │   └── DashboardController.cs    GET /api/dashboard
│   ├── Data/                         database access code
│   │   ├── IDbConnectionFactory.cs   what controllers ask for to get a database connection
│   │   └── MySqlConnectionFactory.cs reads the connection string and opens MySQL connections
│   ├── Models/                       C# classes that query results are turned into
│   │   └── DashboardCategory.cs      one category's row on the dashboard
│   ├── Security/                     PIN and password hashing
│   │   ├── IPasswordHasher.cs        what controllers ask for to hash and check a PIN
│   │   └── BcryptPasswordHasher.cs   hashes with bcrypt at cost 12 and checks PINs against stored hashes
│   ├── appsettings.json              settings used everywhere
│   ├── appsettings.Development.json  settings used only on your computer (database login)
│   ├── Properties/launchSettings.json  port 5000, Development mode
│   └── Imp.Api.csproj                .NET version and packages
└── Imp.Api.Tests/                    xUnit tests for Imp.Api
    ├── PasswordHasherTests.cs        tests for BcryptPasswordHasher, using copies of the sample data hashes
    └── Imp.Api.Tests.csproj          test packages and a reference to Imp.Api
```

## Running tests

From the repo folder:

```
dotnet test
```

This builds both projects and runs every test in `Imp.Api.Tests/`. The tests do not need Docker or the database. To run only one class, add a filter, for example `dotnet test --filter PasswordHasherTests`.

The hashes in `PasswordHasherTests.cs` are copies of the ones in `db/sample_data.sql`. If the sample PINs or hashes change, update the test file too.

## API contracts

Each endpoint's JSON shape is written in `docs/api/` before the endpoint is built, and the Flutter app (IMP-Front) codes against that file. If a shape needs to change, change the contract file first, then the code on both sides.

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
| Chrome | `http://localhost:5000` |
| Android emulator | `http://10.0.2.2:5000` |
| A real tablet on the same Wi-Fi | `http://<this computer's IP>:5000` (also change `localhost` to `0.0.0.0` in `launchSettings.json`) |
| An Android phone plugged in by USB | `http://localhost:5000`, after running `adb reverse tcp:5000 tcp:5000` |

The app chooses the address when it starts, with `--dart-define=API_BASE_URL=...` (D-158). The commands are in IMP-Front's README.

**Plain HTTP works on Android.** The app uses Dart's `http` package, which doesn't go through Android's block on plain HTTP, so no `usesCleartextTraffic` setting is needed. The release build uses HTTPS.

**Chrome:** the API allows requests from any web page while in Development mode (CORS), so `flutter run -d chrome` works. Other platforms do not need this.

## Database connection

The connection details live in `Imp.Api/appsettings.Development.json` under the name `IMP`:

```json
"ConnectionStrings": {
  "IMP": "Server=localhost;Port=3307;Database=IMP;User=imp;Password=imp"
}
```

- These match the MySQL container in `db/docker-compose.yml`. If you change the port or login there, change them here too.
- C# code reads it with `builder.Configuration.GetConnectionString("IMP")`. Never write the login into the code itself.
- These values only work against the MySQL container on your own computer, so committing them is fine.
- **Never commit the real server's password.** On the server, the connection string is set with an environment variable named `ConnectionStrings__IMP`, and this file is not used.

### How the API talks to MySQL

- **Libraries:** [MySqlConnector](https://mysqlconnector.net/) opens the connection to MySQL, and [Dapper](https://github.com/DapperLib/Dapper) runs SQL and turns the rows into C# objects. We chose MySqlConnector over Oracle's `MySql.Data` because it has proper async support. Both are listed in `Imp.Api.csproj` and install by themselves on the first `dotnet run` or `dotnet restore`.
- **Connection factory:** `Program.cs` reads the `IMP` connection string once at startup and registers a `MySqlConnectionFactory`. If the string is missing, the API refuses to start and says why. The factory also sets a 5 second connection timeout, so a stopped database fails quickly instead of hanging.
- **Using it in a controller:** ask for `IDbConnectionFactory` in the constructor, open a connection, and run a query with Dapper:

  ```csharp
  await using var connection = await _db.OpenConnectionAsync(cancellationToken);
  var boxes = await connection.QueryAsync<Box>("SELECT * FROM BOX LIMIT 5");
  ```

  `await using` closes the connection when the method ends. Always pass values as parameters (`new { id }` with `@id` in the SQL) and never build SQL by joining strings, so users cannot inject SQL.
- **Column names:** `Program.cs` turns on Dapper's `MatchNamesWithUnderscores`, so a snake_case column such as `alert_threshold_days` fills a PascalCase property such as `AlertThresholdDays`. ASP.NET Core then writes properties to JSON in camelCase (`alertThresholdDays`).

### Database health check

`GET /api/health/database` opens a connection and runs `SELECT 1`.

| Situation | Status | Body |
|---|---|---|
| MySQL container running and login works | 200 | `{"database":"ok"}` |
| Container stopped, wrong port, wrong login, or no answer within 5 seconds | 503 | `{"database":"error"}` |

The reason for a failure is written to the API's console output (not sent to the caller). To try it:

```
cd db
docker compose stop     # then open /api/health/database: shows "error"
docker compose start    # wait for (healthy), then reload: shows "ok"
```

## Database setup

MySQL runs in a Docker container, so everyone gets the same version, tables, and test data. Install Docker first (see Setup). Run every command below from the `db/` folder.

### Start it

```
cd db
docker compose up -d
docker compose ps
```

The first start downloads MySQL and loads the tables and sample data, which takes about a minute. Wait until `docker compose ps` shows `(healthy)` before running the API.

### Look inside

Open a MySQL prompt in the container:

```
docker exec -it imp-mysql mysql --default-character-set=utf8mb4 -uimp -pimp IMP
```

Try `SHOW TABLES;` or `SELECT * FROM BOX LIMIT 5;`, and type `exit` to leave. Without `--default-character-set=utf8mb4`, category emoji show as `?`, but they are stored correctly.

To use a program such as DBeaver or MySQL Workbench instead, connect to host `localhost`, port `3307`, database `IMP`, user `imp`, password `imp`.

### Stop, start, and reset

| What you want | Command |
|---|---|
| Stop it (keeps the data) | `docker compose stop` |
| Start it again | `docker compose start` |
| Reset to fresh tables and sample data (deletes everything) | `docker compose down -v` then `docker compose up -d` |
| See errors | `docker compose logs db` |

The `.sql` files only run when the database is empty. After pulling changes to `schema.sql` or `sample_data.sql`, reset to see them.

### Files

| File | What it does | Runs on |
|---|---|---|
| `schema.sql` | Creates the `IMP` database and its 7 tables, following ERD v2.6 | Your computer and the real server |
| `sample_data.sql` | Clears the tables and fills them with test data | Your computer only, never the real server |
| `docker-compose.yml` | Runs MySQL 8.0 on port 3307 and loads both files above on first start | Your computer only |

Keep the table scripts and the sample data in separate files, so `schema.sql` can be run on the real server without the test data. Port 3307 is used instead of the usual 3306, so it does not clash with a MySQL already installed on your computer.

### Test logins

The sample data comes with these accounts. PINs and passwords are stored as bcrypt hashes, never as plain text. New hashes use cost 12 (D-162). Pass it explicitly, for example `BCrypt.Net.BCrypt.HashPassword(pin, 12)`, because BCrypt.Net-Next uses 11 when no cost is given. The hashes in `sample_data.sql` were regenerated at cost 12 to match.

| Account | Sign in with | Notes |
|---|---|---|
| Daniel Finkle | PIN 111111 | |
| Thomas Anos | PIN 222222 | |
| Maria Lopez | PIN 333333 | |
| Grace Kim | PIN 444444 | |
| Ben Carter | PIN 555555 | Deactivated, so sign-in should fail |
| Sam Ortiz | PIN 666666 | Has never signed in |
| Master account | master@example.com, password master123 | |

These only exist on your own computer. The real server gets its own accounts.

### What the sample data covers

The sample data has 20 boxes across 4 buildings and 9 categories, with all three fullness levels, shelf letters of 1 to 3 letters, and shelf levels 1 to 3. It includes boxes past the 180-day alert threshold, unracked boxes, soft-deleted boxes, a building and a category with no boxes (to test deleting them), and an audit log with an entry for every kind of change the ERD allows.

## HTTP now, HTTPS later

The API runs on plain HTTP while we develop on our own computers (decision D-136). Before the beta, the real server will use HTTPS through Nginx, which sits in front of the API on the server and handles the encryption. The API itself stays on HTTP behind Nginx, so **do not add HTTPS redirects or certificates to this project.** The Flutter app's address will change to an `https://` address at that point.

## Common problems

| Problem | Fix |
|---|---|
| `Specify a project or solution file` | Add `--project Imp.Api` to the command, or run it inside `Imp.Api/`. |
| `NETSDK1226: Prune Package data not found` (Arch Linux) | Install `aspnet-runtime` and `aspnet-targeting-pack` (see Setup). |
| `address already in use` on port 5000 (Mac) | Turn off AirPlay Receiver in System Settings, General, AirDrop & Handoff. |
| Flutter in Chrome shows a CORS error | Check the API is running in Development mode (`dotnet run` does this). |
| Android app cannot reach the API | On the emulator, start the app with `--dart-define=API_BASE_URL=http://10.0.2.2:5000`. On a real device, see the address table above. |

## Claude prompt logs

Every prompt sent to Claude Code in this repo is saved word for word, with a timestamp, in `claude_logs/`, with a folder for each person (named from your git `user.name`) and one markdown file per session, named by date and session. A hook does the logging: `.claude/settings.json` runs `.claude/hooks/log_prompt.py` each time a prompt is sent, so it needs Python 3 (`python3` or `python` on your PATH). Commit the log files along with your other changes. Nobody edits or deletes them, Claude included, and Claude doesn't read them for context.

## Writing style

In comments, docs, and this README: no semicolons between list items, no em dashes, and write "tablet" rather than the device's brand name.
