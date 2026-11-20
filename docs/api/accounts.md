# Employee accounts

Endpoints for the Account Management page (wireframes 10a and 10b): listing, adding, editing, deactivating, and reactivating employees. Every endpoint here takes the session token (D-164) and needs Master mode (`[RequireMasterMode]`), so each one can also return 403 "Turn on Master mode first." Errors use the shared format in [README.md](README.md).

Only the Master account manages employees (Story_18). Every employee has the same access (D-015). Accounts are deactivated, never deleted, so the audit log always has a name to show.

The alert threshold and idle sign-out cards on the same page belong to the settings contract, which is a next goal (D-189). Until it is built, the page shows the placeholders, 180 days and 10 minutes.

## The employee

```json
{ "id": 4, "name": "Grace Kim", "isActivated": true, "lastSignInAt": "2026-11-17T13:02:00Z" }
```

| Field | Type | Meaning |
|---|---|---|
| `id` | int | Employee id |
| `name` | string | 1 to 100 characters after trimming, the same rule as `employeeName` in [auth.md](auth.md). Unique, ignoring capital letters |
| `isActivated` | bool | False for a deactivated employee, who can't sign in |
| `lastSignInAt` | string or null | When the employee last signed in, in UTC (`...Z`). Null if they never have |

The PIN is never sent back.

---

## GET /api/employees

Every employee, activated or not, sorted by name. (The sign-in screen uses `GET /api/employees/active` in [auth.md](auth.md) instead, which lists only activated employees and takes the device token.)

### Success: 200

```json
{ "employees": [ { "id": 4, "name": "Grace Kim", "isActivated": true, "lastSignInAt": "2026-11-17T13:02:00Z" } ] }
```

---

## POST /api/employees

Adds an employee. Used by the Add employee dialog (wireframe 10b). The dialog's Confirm PIN field is checked in the app and isn't sent.

### Request

```json
{ "name": "Candy", "pin": "482913" }
```

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Required. The name rule above |
| `pin` | string | Required. Exactly 6 digits, the same rule as sign-in. A string, so a leading zero is kept |

### Success: 201

The new employee, activated, with `lastSignInAt` null.

The server, in one transaction:

- creates the `EMPLOYEE_ACCOUNT` row with the PIN hashed by bcrypt at cost 12 (D-162)
- writes one audit entry: EMPLOYEE ADDED, with the employee's name as the label and null `details`

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | A field is missing or breaks its rule | The same sentences as `employeeName` and `pin` in `POST /api/setup` |
| 409 | Another employee has this name, ignoring capital letters | An employee with this name already exists. Choose another name. |

---

## PUT /api/employees/{id}

Changes an employee's name, PIN, or both. Used by Edit on the Account Management page. A deactivated employee can be edited too.

### Request

```json
{ "name": "Candace", "pin": null }
```

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Required. Send it even if unchanged |
| `pin` | string or null | Null keeps the current PIN. A new PIN follows the 6-digit rule |

Changing the PIN signs the employee out of every tablet, the same way as deactivating (D-206), in case someone else knew the old PIN. They sign in again with the new one.

### Success: 200

The saved employee. If nothing changed (same name, and `pin` null), it comes back as it was, with no audit entry.

When something changed, the server, in one transaction:

- saves the name and the new PIN's hash
- for a new PIN, signs the employee out everywhere, as `POST /api/employees/{id}/deactivate` does
- writes one audit entry: EMPLOYEE EDITED, with the new name as the label, and `details` listing a `name` change and, for a new PIN, `{"field": "pin", "note": "PIN changed"}` with no values (D-197)

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | A field breaks its rule | The same sentences as `POST /api/employees` |
| 404 | No employee has this id | This employee doesn't exist. |
| 409 | Another employee has this name, ignoring capital letters | An employee with this name already exists. Choose another name. |

---

## POST /api/employees/{id}/deactivate

Deactivates an employee. They disappear from the sign-in screen and are signed out of every tablet at once (Story_18, D-206). Each tablet stays set up and shows employee sign-in.

### Success: 200

The employee, with `isActivated` false. Deactivating an employee who is already deactivated returns them as they are, with no audit entry.

The server, in one transaction:

- sets `is_activated` to false
- signs them out everywhere: every `DEVICE` row with them as `active_employee_id` gets `active_employee_id` null and `is_elevated` false, so Signed-in Devices stops listing them. Their session tokens already stop working, because each request checks that the employee is activated ([auth.md](auth.md))
- writes one audit entry: EMPLOYEE DEACTIVATED, with the employee's name as the label and null `details`

### Errors

| Status | When | `error` |
|---|---|---|
| 404 | No employee has this id | This employee doesn't exist. |
| 409 | The employee is the one signed in on this tablet | You can't deactivate the account you're signed in with. |

The 409 (D-202) also means the last activated employee can never be deactivated, since someone has to be signed in to use Master mode (D-117). Without it, the store could lock itself out.

---

## POST /api/employees/{id}/reactivate

Reactivates an employee, who can sign in again with their old PIN.

### Success: 200

The employee, with `isActivated` true. Reactivating an employee who is already activated returns them as they are, with no audit entry.

The server, in one transaction:

- sets `is_activated` to true
- writes one audit entry: EMPLOYEE ACTIVATED, with the employee's name as the label and null `details`

### Errors

| Status | When | `error` |
|---|---|---|
| 404 | No employee has this id | This employee doesn't exist. |
