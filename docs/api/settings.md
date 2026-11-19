# Settings

Endpoints for the store-wide settings: the alert threshold and idle sign-out, shown as two cards on the Account Management page (wireframe 10a) and changed in a dialog (wireframe 10c). Every endpoint here takes the session token (D-164). Errors use the shared format in [README.md](README.md).

Both values are stored on the Master account row (`MASTER_ACCOUNT.alert_threshold_days` and `idle_timeout_minutes`) and apply to every tablet. Changing the Master email or password isn't here: it moves to Spring 2027 (D-209).

## The settings

```json
{ "alertThresholdDays": 180, "idleTimeoutMinutes": 10 }
```

| Field | Type | Meaning |
|---|---|---|
| `alertThresholdDays` | int | The number of days a racked box can stay in storage, counted from its `rackedSince`, before it raises a storage alert (D-131). 1 to 3650 (D-208) |
| `idleTimeoutMinutes` | int | How long a tablet can go untouched before the employee is signed out and the tablet returns to the PIN screen (D-152). 1 to 240 |

---

## GET /api/settings

The current settings. Any signed-in employee can call it (D-208), because every tablet needs `idleTimeoutMinutes` to run its idle timer. The app calls it after sign-in and when Account Management opens.

### Success: 200

The settings, as above.

### Errors

Only the session token errors in [README.md](README.md).

---

## PUT /api/settings

Changes the settings. Master mode only (`[RequireMasterMode]`). Each card's Change button opens its own dialog, and the app sends both values, the unchanged one as it was.

### Request

```json
{ "alertThresholdDays": 200, "idleTimeoutMinutes": 10 }
```

Both fields are required.

### Success: 200

The saved settings. If nothing changed, they come back as they were, with no audit entry.

When something changed, the server, in one transaction:

- saves the values
- writes one audit entry: MASTER EDITED, with the label `Master account`, and `details` listing the changes to `alertThresholdDays` and `idleTimeoutMinutes` (D-197)

A new alert threshold applies at once: the dashboard, box pages, search, and alerts all read it on every request. A new idle timeout reaches each tablet the next time it calls this endpoint.

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | `alertThresholdDays` is missing or not 1 to 3650 | The alert threshold must be 1 to 3650 days. |
| 400 | `idleTimeoutMinutes` is missing or not 1 to 240 | Idle sign-out must be 1 to 240 minutes. |
| 403 | Master mode is off | Turn on Master mode first. |
