# Audit log

The endpoint for the Audit Log page (wireframe 14). It takes the session token (D-164) and needs Master mode (`[RequireMasterMode]`), so it can also return 403 "Turn on Master mode first." Errors use the shared format in [README.md](README.md).

The log is read only: nothing in the API edits or deletes an entry (Story_16). Entries are written by the endpoints that make changes, as each contract says, and their `details` follow the Audit details section of [boxes.md](boxes.md).

Search and filter (D-210) are a next goal (D-189). The parameters are part of the contract now, built when the next goal is scheduled.

---

## GET /api/audit-log

Entries, newest first, one page at a time, optionally searched and filtered.

### Request

`GET /api/audit-log?before=1234&limit=50`

| Parameter | If left out | Meaning |
|---|---|---|
| `before` | Start from the newest entry | Only entries with an id below this one. To get the next page, send the `nextBefore` from the last page |
| `limit` | 50 | How many entries to return, 1 to 200 |
| `search` | No text filter | Entries whose employee name, tablet name, or target label contains this text, ignoring capital letters. Box numbers are found with or without `#`, since labels are stored as `#6789` |
| `action` | Every action | One of the nine actions, for example `DELETED` |
| `targetType` | Every target | `BOX`, `BUILDING`, `CATEGORY`, `EMPLOYEE`, or `MASTER` |
| `masterMode` | Both | `true` for entries made with Master mode on, `false` for the rest |
| `from` | No start | `YYYY-MM-DD`. Entries on or after this day, in the store's time zone (D-175) |
| `to` | No end | `YYYY-MM-DD`. Entries on or before this day, in the store's time zone |

The parameters combine with each other and with `before` and `limit`. Searching by an action's name ("deleted") is the `action` filter's job, so `search` doesn't look at actions.

Pages go by id rather than page number, so new entries written while someone scrolls don't shift the pages and show an entry twice (D-207). Nothing updates live: reopening or refreshing the page starts from the newest entry again.

### Success: 200

```json
{
  "entries": [
    {
      "id": 1234,
      "timestamp": "2026-10-07T18:05:00Z",
      "employee": { "id": 4, "name": "Grace Kim" },
      "isElevated": false,
      "device": { "id": 1, "name": "Processing tablet" },
      "action": "EDITED",
      "targetType": "BOX",
      "targetId": 6789,
      "targetLabel": "#6789",
      "details": { "changes": [ { "field": "fullness", "from": "HALF", "to": "FULL" } ] }
    }
  ],
  "nextBefore": 1185
}
```

| Field | Type | Meaning |
|---|---|---|
| `entries` | array | Up to `limit` entries, newest first (highest id first) |
| `entries[].id` | int | Entry id |
| `entries[].timestamp` | string | When it happened, in UTC (`...Z`). The app shows it in local time |
| `entries[].employee` | object | `id` and `name` of the employee signed in on the tablet, with their current name |
| `entries[].isElevated` | bool | Whether Master mode was on. The Master column |
| `entries[].device` | object | `id` and `name` of the tablet, with its current name |
| `entries[].action` | string | `ADDED`, `EDITED`, `DELETED`, `RESTORED`, `PERMANENTLY_DELETED`, `RACKED`, `UNRACKED`, `ACTIVATED`, or `DEACTIVATED` |
| `entries[].targetType` | string | `BOX`, `BUILDING`, `CATEGORY`, `EMPLOYEE`, or `MASTER` |
| `entries[].targetId` | int | The id of what changed. It may no longer exist, after a permanent delete |
| `entries[].targetLabel` | string | The name or box number at the time, so it stays readable after a rename or a permanent delete. The app shows a box as `Box #6789` |
| `entries[].details` | object or null | The `changes` list, as stored. Null for actions that have none |
| `nextBefore` | int or null | The `before` to send for the next page. Null when there are no older entries |

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | `before` isn't a number | Before must be a number. |
| 400 | `limit` isn't a number from 1 to 200 | Limit must be a number from 1 to 200. |
| 400 | `action` isn't one of the nine | Action must be one of the audit actions. |
| 400 | `targetType` isn't one of the five | Target type must be BOX, BUILDING, CATEGORY, EMPLOYEE, or MASTER. |
| 400 | `masterMode` isn't `true` or `false` | Master mode must be true or false. |
| 400 | `from` or `to` isn't a `YYYY-MM-DD` date | From must be a date. (or To) |
| 400 | `from` is after `to` | From must be on or before To. |

"Entry details" (tapping an entry to see what changed, also a next goal) needs nothing new: every entry already has its `details`.
