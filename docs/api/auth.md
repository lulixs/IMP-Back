# Sign-in and tablets

Endpoints for first-time setup, setting up and signing back in tablets, employee sign-in and sign-out, and Master mode.

## Tokens

The app holds two tokens, both JWTs signed by the server (D-163). Nothing about them is stored in the database. The app saves them with `flutter_secure_storage` and never reads inside them.

| Token | Issued by | Lasts | Holds (for Server) |
|---|---|---|---|
| Device token | `POST /api/setup`, `POST /api/devices`, `POST /api/devices/renew` | 90 days (D-147) | `kind: "device"`, `deviceId`, `iat`, `exp` |
| Session token | `POST /api/setup`, `POST /api/sessions` | 12 hours (D-147) | `kind: "session"`, `sub` (the employee id), `deviceId`, `iat`, `exp` |

A decoded session token, for example:

```json
{ "kind": "session", "sub": "1", "deviceId": 3, "iat": 1794200000, "exp": 1794243200 }
```

`iat` and `exp` are seconds since January 1, 1970. `kind` stops one token from being used in place of the other. Master mode is not in any token. It can change during a session, so it lives in `DEVICE.is_elevated` (D-128). The 90 and 12 are placeholders until Thrift on Purpose confirms them (deferred item 4).

Each request carries one token, as `Authorization: Bearer <token>` (D-164):

| Token | Endpoints |
|---|---|
| None | `GET /api/setup/status`, `POST /api/setup`, `POST /api/devices`, and the health checks `GET /api/health` and `GET /api/health/database` |
| Device token | `POST /api/devices/renew` (expired is fine, see that endpoint), `GET /api/employees/active`, `POST /api/sessions` |
| Session token | Everything else, in this file and every other contract |

## Errors

Errors use the shared format in [README.md](README.md) (D-165). Each endpoint below lists its own errors. These can come back from any request that carries a token, and have a `code` because the app has to leave the current screen (D-168). `ApiClient` handles them for every request.

| Status | When | `error` | `code` | What the app does |
|---|---|---|---|---|
| 401 | Device token missing, not valid, or the tablet's row doesn't exist | This tablet isn't set up. Set it up again. | `deviceNotSetUp` | Clear the device token and open tablet setup |
| 401 | Device token expired | Sign this tablet back in. | `deviceExpired` | Open the Master sign-in screen in sign-back-in mode |
| 401 | The tablet was removed (`DEVICE.removed_at` is set) | This tablet was removed. Set it up again. | `deviceRemoved` | Clear both tokens and open tablet setup |
| 401 | Session token missing, not valid, or expired, the tablet no longer names this employee (signed out, Switch user, idle sign-out, or someone else signed in), or the employee is deactivated | You've been signed out. Sign in again. | `sessionEnded` | Clear the session token and open employee sign-in |

On a request with a session token, the server checks, in order: the signature and `exp`, that the tablet isn't removed, that `DEVICE.active_employee_id` equals `sub`, and that the employee is activated. A session token never gets `deviceExpired`. The 90-day approval is checked at sign-in (D-164).

Every request with a valid token sets `DEVICE.last_activity_at` to now.

None of these endpoints write audit entries. Sign-ins and tablet setup are not logged (ERD v2.6, D-142), except the first employee's ADDED entry from `POST /api/setup` (D-118).

---

## GET /api/setup/status

Tells a tablet with no device token whether the store has been set up, so it opens first-time setup or tablet setup.

**Token:** none.

### Success: 200

```json
{ "isSetUp": true }
```

| Field | Type | Meaning |
|---|---|---|
| `isSetUp` | bool | True once the `MASTER_ACCOUNT` row exists |

### Errors

None of its own.

---

## POST /api/setup

First-time setup. Creates the Master account, this tablet, and the first employee, and signs that employee in. Used by the setup confirmation screen.

**Token:** none.

### Request

```json
{
  "masterEmail": "manager@example.com",
  "masterPassword": "password123",
  "tabletName": "Front desk tablet",
  "employeeName": "Daniel Finkle",
  "employeePin": "111111"
}
```

| Field | Type | Meaning |
|---|---|---|
| `masterEmail` | string | The Master account's email. Trimmed. Must look like `x@y.z` with no spaces |
| `masterPassword` | string | At least 8 characters |
| `tabletName` | string | 1 to 100 characters after trimming |
| `employeeName` | string | 1 to 100 characters after trimming |
| `employeePin` | string | Exactly 6 digits |

### Success: 201

```json
{
  "deviceToken": "eyJhbGciOi...",
  "sessionToken": "eyJhbGciOi...",
  "employee": { "id": 1, "name": "Daniel Finkle" }
}
```

| Field | Type | Meaning |
|---|---|---|
| `deviceToken` | string | Save it. Sent on `GET /api/employees/active` and `POST /api/sessions` |
| `sessionToken` | string | Save it. Sent on every other request |
| `employee.id` | int | The first employee, now signed in |
| `employee.name` | string | Shown in the sidebar |

The server, in one transaction:

- creates the `MASTER_ACCOUNT` row with the password hashed (bcrypt cost 12, D-162)
- creates the `DEVICE` row for this tablet
- creates the `EMPLOYEE_ACCOUNT` row with the PIN hashed, and `last_sign_in_at` set to now
- sets `DEVICE.active_employee_id` to the new employee
- writes one audit entry: EMPLOYEE ADDED, by that same employee, on this tablet (D-118)

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | A field is missing or breaks its rule | A sentence naming the field, for example: Enter a valid email. |
| 409 | The store is already set up | This store is already set up. Set up this tablet instead. |

---

## POST /api/devices

Sets up another tablet with the Master email and password. Used by the Master sign-in screen in set-up-tablet mode (D-146).

**Token:** none.

### Request

```json
{ "tabletName": "Stockroom tablet", "email": "manager@example.com", "password": "password123" }
```

| Field | Type | Meaning |
|---|---|---|
| `tabletName` | string | 1 to 100 characters after trimming |
| `email` | string | The Master account's email |
| `password` | string | The Master account's password |

### Success: 201

```json
{ "deviceToken": "eyJhbGciOi...", "device": { "id": 2, "name": "Stockroom tablet" } }
```

| Field | Type | Meaning |
|---|---|---|
| `deviceToken` | string | Save it. The app then opens employee sign-in |
| `device.id` | int | The tablet's id |
| `device.name` | string | The tablet's name |

The server creates a `DEVICE` row with no employee signed in and Master mode off. If the name belongs to a removed tablet, it reuses that row instead and clears `removed_at`, so the id and audit history carry on (D-166).

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | `tabletName` is blank or too long, or a field is missing | Enter a tablet name. |
| 401 | Wrong email or password | Incorrect email or password. Try again. |
| 409 | The name belongs to a tablet that isn't removed | A tablet with this name already exists. Choose another name. |
| 409 | The store isn't set up yet | This store isn't set up yet. |

---

## POST /api/devices/renew

Signs a tablet back in after its 90-day approval ends, with the Master email and password (D-167). Used by the Master sign-in screen in sign-back-in mode, which the app opens on a `deviceExpired` error.

**Token:** the device token, expired or not. For this endpoint only, the server checks the signature but not `exp`.

### Request

```json
{ "email": "manager@example.com", "password": "password123" }
```

| Field | Type | Meaning |
|---|---|---|
| `email` | string | The Master account's email |
| `password` | string | The Master account's password |

### Success: 200

```json
{ "deviceToken": "eyJhbGciOi..." }
```

| Field | Type | Meaning |
|---|---|---|
| `deviceToken` | string | Replaces the old one. A new 90 days for the same tablet. The app then opens employee sign-in |

Nothing changes in the database apart from `last_activity_at`.

### Errors

| Status | When | `error` | `code` |
|---|---|---|---|
| 401 | Wrong email or password | Incorrect email or password. Try again. | |
| 401 | Device token missing or not valid | This tablet isn't set up. Set it up again. | `deviceNotSetUp` |
| 401 | The tablet was removed | This tablet was removed. Set it up again. | `deviceRemoved` |

---

## GET /api/employees/active

The names on the employee sign-in screen.

**Token:** the device token.

### Success: 200

```json
{
  "employees": [
    { "id": 1, "name": "Daniel Finkle" },
    { "id": 4, "name": "Grace Kim" }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `employees` | array | Every activated employee, sorted by name. Deactivated employees are left out |
| `employees[].id` | int | Sent as `employeeId` to `POST /api/sessions` |
| `employees[].name` | string | Shown in the list |

### Errors

Only the device token errors under Errors above.

---

## POST /api/sessions

Signs an employee in on this tablet with their PIN. Used by the employee sign-in screen.

**Token:** the device token.

### Request

```json
{ "employeeId": 1, "pin": "111111" }
```

| Field | Type | Meaning |
|---|---|---|
| `employeeId` | int | The employee picked from `GET /api/employees/active` |
| `pin` | string | Exactly 6 digits. A string, so a PIN like `012345` keeps its leading zero |

### Success: 200

```json
{
  "sessionToken": "eyJhbGciOi...",
  "employee": { "id": 1, "name": "Daniel Finkle" }
}
```

| Field | Type | Meaning |
|---|---|---|
| `sessionToken` | string | Save it. Sent on every later request |
| `employee.id` | int | The signed-in employee |
| `employee.name` | string | Shown in the sidebar |

The server also:

- sets `DEVICE.active_employee_id` to this employee and `DEVICE.is_elevated` to false (Master mode starts off)
- sets `EMPLOYEE_ACCOUNT.last_sign_in_at` to now

If another employee was signed in on this tablet, their session ends, since the tablet now names this employee. Sessions on other tablets are not affected (D-151).

### Errors

| Status | When | `error` | `code` |
|---|---|---|---|
| 400 | `pin` is not exactly 6 digits, or a field is missing | Enter your 6-digit PIN. | |
| 401 | Wrong PIN | Incorrect PIN. Try again. | |
| 401 | The employee is deactivated, or `employeeId` doesn't exist | This account can't sign in. Choose your name again. | |
| 401 | Device token missing or not valid | This tablet isn't set up. Set it up again. | `deviceNotSetUp` |
| 401 | Device token expired | Sign this tablet back in. | `deviceExpired` |
| 401 | The tablet was removed | This tablet was removed. Set it up again. | `deviceRemoved` |

---

## DELETE /api/sessions/current

Signs the employee out of this tablet. Used by Switch user, and by idle sign-out when the tablet goes untouched for the idle timeout (D-152).

**Token:** the session token.

### Success: 204

No body. The app clears the session token and opens employee sign-in.

The server sets `DEVICE.active_employee_id` to null and `DEVICE.is_elevated` to false, so Master mode turns off too (D-117).

### Errors

Only the session token errors under Errors above. If the session had already ended, the app gets `sessionEnded`, which ends in the same place.

---

## POST /api/master-mode

Turns on Master mode on this tablet. Used by the Master sign-in screen in Master mode (D-146), opened from the sidebar's Master tag.

**Token:** the session token.

### Request

```json
{ "email": "manager@example.com", "password": "password123" }
```

| Field | Type | Meaning |
|---|---|---|
| `email` | string | The Master account's email |
| `password` | string | The Master account's password |

### Success: 204

No body. The server sets `DEVICE.is_elevated` to true. It stays on until Master mode is turned off, the employee signs out, or the session ends (D-117).

### Errors

| Status | When | `error` |
|---|---|---|
| 401 | Wrong email or password | Incorrect email or password. Try again. |

Plus the session token errors under Errors above.

---

## DELETE /api/master-mode

Turns off Master mode on this tablet. Used by "Turn off Master mode?" from the sidebar's Master tag.

**Token:** the session token.

### Success: 204

No body. The server sets `DEVICE.is_elevated` to false. The employee stays signed in.

### Errors

Only the session token errors under Errors above.
