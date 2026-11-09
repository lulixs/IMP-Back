# Boxes

Endpoints for finding, opening, creating, editing, and soft-deleting boxes. Restoring and permanently deleting boxes are Master-only and belong to the Deleted Items contract (week 14).

Every endpoint here takes the session token (D-164). Errors use the shared format in [README.md](README.md), and the session token errors listed there can come back from any of them.

"Today" means the date in the store's time zone (Eastern), not UTC, so a box saved at 9 PM is dated that day.

## The box

Every endpoint that returns a box uses this shape.

```json
{
  "id": 6789,
  "category": { "id": 1, "name": "Coats", "symbol": "🧥" },
  "building": { "id": 2, "name": "Processing Center" },
  "shelfLetter": "A",
  "shelfLevel": 2,
  "fullness": "FULL",
  "notes": "Mostly adult sizes. Check zippers before putting out.",
  "isRacked": true,
  "rackedSince": "2026-01-14",
  "daysStored": 299,
  "storedTooLong": true,
  "createdAt": "2025-12-03T14:32:00Z",
  "createdBy": { "id": 1, "name": "Daniel Finkle" },
  "lastUpdatedAt": "2026-10-07T18:05:00Z",
  "lastUpdatedBy": { "id": 4, "name": "Grace Kim" },
  "deletedAt": null,
  "deletedBy": null
}
```

| Field | Type | Meaning |
|---|---|---|
| `id` | int | The box number. Shown as `#6789`, and the QR label holds `IMP-6789` |
| `category` | object | `id`, `name`, and `symbol` (an emoji, or null), the same shape as [categories.md](categories.md) |
| `building` | object | `id` and `name`, the same shape as [buildings.md](buildings.md) |
| `shelfLetter` | string | One to three capital letters, A to Z |
| `shelfLevel` | int | 1, 2, or 3 |
| `fullness` | string | `EMPTY`, `HALF`, or `FULL`. The app shows them as Empty, Half, and Full |
| `notes` | string or null | Free text, null if there are none |
| `isRacked` | bool | False while the box is out of storage |
| `rackedSince` | string | A date with no time, `YYYY-MM-DD`. Storage time and alerts count from it. Kept while the box is unracked |
| `daysStored` | int | Whole days from `rackedSince` to today, worked out by the server |
| `storedTooLong` | bool | True when the box is racked and `daysStored` is at least the alert threshold, the same rule as the dashboard. Always false for an unracked box |
| `createdAt` | string | When the box was saved, in UTC (`...Z`). The app shows it in local time |
| `createdBy` | object | `id` and `name` of the employee who created it |
| `lastUpdatedAt` | string or null | When it was last changed, in UTC. Null if it never has been. Sent back on save (D-174) |
| `lastUpdatedBy` | object or null | `id` and `name` of the employee who last changed it |
| `deletedAt` | string or null | When it was soft-deleted, in UTC. Null for a box that isn't deleted (D-173) |
| `deletedBy` | object or null | `id` and `name` of the employee who soft-deleted it |

Employee names are the current names, so a renamed employee shows their new name.

---

## GET /api/boxes

Search and filter boxes. Used by the Boxes list (wireframe 04).

### Request

All parameters are optional, and they combine. With none, every box is returned.

`GET /api/boxes?search=coat&categoryId=1&racked=true&sort=oldest`

| Parameter | If left out | Meaning |
|---|---|---|
| `search` | No text filter | Digits, with or without `#`, match boxes whose number starts with them, so `67` finds #67, #670, and #6789. Any other text matches category names that contain it, ignoring capital letters (D-172) |
| `categoryId` | All categories | Only boxes in this category |
| `racked` | Racked and unracked | `true` for racked boxes only, `false` for unracked only |
| `sort` | `oldest` | `oldest` or `newest`, by `rackedSince`, with the box number breaking ties (D-171) |

### Success: 200

```json
{ "boxes": [ { "id": 6789, "category": { "id": 1, "name": "Coats", "symbol": "🧥" }, "...": "the box shape above" } ] }
```

| Field | Type | Meaning |
|---|---|---|
| `boxes` | array | Every matching box, in the box shape above, in the order asked for. Empty if nothing matches |

Soft-deleted boxes are never included. There is no paging in Fall, so every match comes back at once (D-171).

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | `racked` isn't `true` or `false`, `sort` isn't `oldest` or `newest`, or `categoryId` isn't a number | A sentence naming the parameter, for example: Sort must be oldest or newest. |

---

## GET /api/boxes/{id}

One box. Used by the Box page and by the scanner after it reads `IMP-6789`.

### Success: 200

The box, in the box shape above.

A soft-deleted box is returned too, with `deletedAt` and `deletedBy` filled in (D-173). The app checks `deletedAt` before opening the Box page: if it is set, the scanner shows "Box #6602 was deleted" with who and when (wireframe 05c) instead.

### Errors

| Status | When | `error` |
|---|---|---|
| 404 | No box has this number: it never existed, or it was permanently deleted | This box doesn't exist. |

The scanner shows wireframe 05b ("No box matches this code") on a 404, and on a code that isn't `IMP-` followed by a number, without asking the server.

---

## POST /api/boxes

Creates a box. Used by New Box (wireframe 07). "Save and print label" uses the `id` in the response to print the label.

### Request

```json
{
  "categoryId": 1,
  "buildingId": 2,
  "shelfLetter": "A",
  "shelfLevel": 2,
  "fullness": "FULL",
  "notes": "Mostly adult sizes.",
  "rackedSince": "2026-11-09"
}
```

| Field | Type | Meaning |
|---|---|---|
| `categoryId` | int | Required. An existing category |
| `buildingId` | int | Required. An existing building |
| `shelfLetter` | string | Required. One to three capital letters, A to Z. The app makes the letters capitals before sending |
| `shelfLevel` | int | Required. 1, 2, or 3 |
| `fullness` | string | Required. `EMPTY`, `HALF`, or `FULL` |
| `notes` | string or null | Optional. Trimmed, and blank becomes null |
| `rackedSince` | string | Required. `YYYY-MM-DD`, today or earlier. New Box fills in today, and an earlier date is for a box that was already in storage |

### Success: 201

The new box, in the box shape above.

The server, in one transaction:

- creates the `BOX` row, racked, with `created_by` from the session token and `created_at` now
- writes one audit entry: BOX ADDED, with the label `#6789`

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | A required field is missing, or a value breaks its rule | A sentence naming the field, for example: Shelf letter must be one to three capital letters. |
| 400 | `rackedSince` is after today | The storage date can't be in the future. |
| 400 | The category or building doesn't exist (deleted while the form was open) | That category no longer exists. Choose another. (or building) |

---

## PUT /api/boxes/{id}

Saves every field of a box at once. Used by Save changes on the Box page, including unracking and re-racking (D-170).

### Request

```json
{
  "categoryId": 1,
  "buildingId": 3,
  "shelfLetter": "C",
  "shelfLevel": 1,
  "fullness": "HALF",
  "notes": "Mostly adult sizes. Check zippers before putting out.",
  "isRacked": true,
  "resetRackedSince": false,
  "lastUpdatedAt": "2026-10-07T18:05:00Z"
}
```

| Field | Type | Meaning |
|---|---|---|
| `categoryId`, `buildingId`, `shelfLetter`, `shelfLevel`, `fullness`, `notes` | | The same rules as `POST /api/boxes`. Send every field, changed or not |
| `isRacked` | bool | Required. False to unrack, true to re-rack or keep racked |
| `resetRackedSince` | bool | Required. Only used when this save re-racks the box: true sets `rackedSince` to today ("Reset to today" in wireframe 06f), false keeps it. Ignored otherwise |
| `lastUpdatedAt` | string or null | Required. Exactly the `lastUpdatedAt` the app got when it loaded the box, null included (D-174) |

`rackedSince` can't be set directly. It changes only through `resetRackedSince`.

### Success: 200

The saved box, in the box shape above. If nothing changed, the box comes back as it was, with no audit entries and no change to `lastUpdatedAt`.

When something changed, the server, in one transaction:

- saves the fields and sets `last_updated_at` to now and `last_updated_by` from the session token
- writes one audit entry for each kind of change, with the label `#6789` (D-170):
  - UNRACKED when `isRacked` goes from true to false
  - RACKED when it goes from false to true, with the location and whether the storage timer was reset
  - one EDITED entry listing every other field that changed (D-149)

The exact `details` shape for these entries follows deferred item 41, which Nick is settling.

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | A field is missing, or a value breaks its rule | A sentence naming the field, as for `POST /api/boxes` |
| 400 | The category or building doesn't exist | That category no longer exists. Choose another. (or building) |
| 404 | No box has this number | This box doesn't exist. |
| 404 | The box was soft-deleted | This box was deleted. |
| 409 | The box was saved by anyone since the app loaded it (`lastUpdatedAt` doesn't match) | Someone else changed this box. Reload it and make your change again. |

On 409 the app reloads the box and keeps the person on the Box page. On 404 it goes back to the Boxes list.

---

## DELETE /api/boxes/{id}

Soft-deletes a box. Any employee can do it (Story_29). Used by "Delete box?" on the Box page (wireframe 06e). The box is hidden from search, the dashboard, and alerts until the Master account restores it from Deleted Items.

### Success: 204

No body. The app goes back to the Boxes list.

The server, in one transaction:

- sets `deleted_at` to now and `deleted_by` from the session token
- writes one audit entry: BOX DELETED, with the label `#6789`

### Errors

| Status | When | `error` |
|---|---|---|
| 404 | No box has this number | This box doesn't exist. |
| 404 | The box is already soft-deleted | This box was deleted. |
