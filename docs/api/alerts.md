# Alerts

The endpoint for the Alerts page (wireframe 09) and the count badge on the bottom bar's Alerts button (D-159). It takes the session token (D-164). Any employee can call it. Errors use the shared format in [README.md](README.md).

A box raises an alert when it is racked, not soft-deleted, and its `daysStored` is at least the alert threshold: the same rule as `storedTooLong` in [boxes.md](boxes.md) and the dashboard's counts (Story_13). Alerts are in the app only. Push notifications are Spring 2027.

---

## GET /api/alerts

Every box stored too long, oldest first.

### Success: 200

```json
{
  "alertThresholdDays": 180,
  "boxes": [ { "id": 6377, "daysStored": 412, "storedTooLong": true, "...": "the box shape" } ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `alertThresholdDays` | int | The threshold in days, for the page's subtitle ("6 boxes stored longer than the 180-day alert threshold") |
| `boxes` | array | Every box with `storedTooLong` true, in the box shape from [boxes.md](boxes.md), oldest `rackedSince` first, with the box number breaking ties. Empty if there are none |

The badge shows the number of boxes. The app calls this endpoint when the dashboard or the Alerts page opens, and the badge keeps that number until the next call. There is no separate count endpoint (D-203).

### Errors

Only the session token errors in [README.md](README.md).
