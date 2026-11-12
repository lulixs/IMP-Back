# API contracts

Each file in this folder is the contract for one area of the API: the exact JSON each endpoint takes and returns, and its errors. The Flutter app (IMP-Front) codes against these files, and the server builds what they say. If a shape needs to change, change the contract first, then the code on both sides.

| File | Covers |
|---|---|
| [auth.md](auth.md) | First-time setup, tablets, sign-in and sign-out, Master mode, and the two tokens |
| [boxes.md](boxes.md) | Searching, opening, creating, editing, and soft-deleting boxes |
| [buildings.md](buildings.md) | The building list (adding and removing still to be written) |
| [categories.md](categories.md) | The category list (adding, editing, and deleting still to be written) |
| [dashboard.md](dashboard.md) | `GET /api/dashboard` |

This file holds the rules every contract shares.

## JSON

- Request and response bodies are JSON, sent as UTF-8 with `Content-Type: application/json`.
- Field names are camelCase (`alertThresholdDays`). ASP.NET Core writes C# properties this way on its own.
- A field with no value is sent as `null`, not left out, unless the contract says otherwise.

## Tokens

Every request after setup carries one token in the `Authorization: Bearer <token>` header. Which token goes with which endpoint, and what each holds, is in [auth.md](auth.md) (D-163, D-164).

## Errors

Every error, from every endpoint, has the same shape (D-165):

```json
{ "error": "Incorrect PIN. Try again." }
```

| Field | Type | Meaning |
|---|---|---|
| `error` | string | One sentence a person can read, shown as it is on the screen. Ends with a period. Never contains stack traces, SQL, or other server details |
| `code` | string, optional | A fixed word the app acts on. Only sent on the errors below that need the app to leave the current screen (D-168). Left out everywhere else |

### On the server

So every error comes out in this shape, including the ones ASP.NET Core makes on its own:

- Code that hits a known problem throws `ApiError` with a status, a sentence, and an optional code, for example `throw new ApiError(409, "A tablet with this name already exists. Choose another name.")`.
- One exception handler in `Program.cs` turns an `ApiError` into its status and JSON body. Any other exception becomes a 500 with "Something went wrong on the server.", and the exception is written to the console.
- The automatic 400 from `[ApiController]` (its `InvalidModelStateResponseFactory`) is replaced, so bad input and unreadable JSON use this shape too.
- Status codes with no body from the framework (for example 404 for an unknown URL or 405 for the wrong method) are given this shape as well, for example with `UseStatusCodePages`.

#### Errors and logging

Every error leaves the API in the same JSON shape, and every error is written to a log file.

##### Sending an error from your code

To report a problem the caller should know about, throw an `ApiError`:

```csharp
throw ApiError.NotFound("Box not found.");             // 404
throw ApiError.BadRequest("Label cannot be empty.");   // 400
throw new ApiError(409, "That shelf is already taken.");
```

Shortcuts exist for 400, 401, 403, 404 and 409. For any other status, use `new ApiError(statusCode, message)`. The caller gets that status code and this JSON:

```json
{ "error": "Box not found." }
```

**The message is sent to the app, so never put secrets, SQL, or table names in it.**

##### Everything else

Any other exception (a database error, a bug) returns status 500 and a general message. The real error is never sent to the app.

```json
{ "error": "Something went wrong. Please try again later.", "traceId": "0HN7..." }
```

The `traceId` is also written in the log, so if someone reports a 500, search the log files for that id to find the real error and its stack trace.

##### Bad request bodies

If the app sends JSON that does not match what an endpoint expects (malformed, wrong shape, missing body), the API returns `400` with `{ "error": "The request body is invalid." }` instead of ASP.NET's default response. This is set in `Program.cs` with `InvalidModelStateResponseFactory`, so it follows the same format and logging as every other error.

##### Where the logs are

| Where | What it contains |
|---|---|
| The terminal running `dotnet run` | Everything (information, warnings, errors) |
| `Imp.Api/logs/imp-YYYYMMDD.log` | Warnings and errors only, with full exception details |

- One file per day, and the 30 most recent days are kept.
- An `ApiError` below 500 is logged as a **warning**. Status 500 and above, and any other exception, are logged as **errors**.
- Each line has the request method, path, status code and `traceId`, followed by the exception and its stack trace.
- `logs/` is listed in `.gitignore`. **Never commit log files or put them on a public web path**, because they contain internal details such as SQL and table names.
- On the real server, the account running the API needs permission to write to `Imp.Api/logs/`.

##### How it is built

- `Imp.Api/Errors/ApiError.cs` is the exception class described above.
- The error handler is the first `app.Use(...)` block in `Program.cs`. It must stay first so it catches errors from everything after it.
- Logging uses [Serilog](https://serilog.net/) (the `Serilog.AspNetCore` package), set up at the top of `Program.cs`.
- If the caller disconnects mid-request, nothing is logged as an error.

##### Status codes

| Status | Meaning | Example `error` |
|---|---|---|
| 400 | Bad input: a missing field, a value that breaks a rule, or a body that isn't valid JSON | Enter your 6-digit PIN. |
| 401 | Not signed in, wrong credentials, or a token that is expired or no longer valid | Incorrect email or password. Try again. |
| 403 | Signed in, but the endpoint needs Master mode | Turn on Master mode first. |
| 404 | The thing asked for doesn't exist | This box doesn't exist. |
| 409 | Conflict: a duplicate name, or a delete that is blocked | A tablet with this name already exists. Choose another name. |
| 500 | Something broke on the server | Something went wrong on the server. |

Each contract lists its own errors with their exact sentences. A 500 always uses the sentence above, and the details go to the server's console log, never to the app.

##### Codes

These four can come back from any request that carries a token. The app's `ApiClient` handles them in one place, before any screen sees the error. Details are in [auth.md](auth.md).

| `code` | Status | What the app does |
|---|---|---|
| `deviceNotSetUp` | 401 | Clear the device token and open tablet setup |
| `deviceExpired` | 401 | Open the Master sign-in screen in sign-back-in mode |
| `deviceRemoved` | 401 | Clear both tokens and open tablet setup |
| `sessionEnded` | 401 | Clear the session token and open employee sign-in |

Only add a new code when the app has to do something other than show the sentence. Add it to this table and to the contract that returns it.

##### In the app

`ApiClient` turns every error into an `ApiException`:

1. If the response has a `code` from the table above, handle it there and don't pass it to the screen.
2. Otherwise, use the `error` sentence as the `ApiException` message, and the screen shows it.
3. If the response isn't JSON, or has no `error` (for example a proxy error page), or there is no response at all (no connection or a timeout), use a fixed sentence such as "Can't reach the server. Check the connection and try again."
