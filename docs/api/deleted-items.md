# Deleted items

Endpoints for the Deleted Items page (wireframes 13a and 13b): listing soft-deleted boxes, restoring them, and deleting them permanently. Every endpoint here takes the session token (D-164) and needs Master mode (`[RequireMasterMode]`), so each one can also return 403 "Turn on Master mode first." Errors use the shared format in [README.md](README.md).

Only boxes are soft-deleted (D-123). Categories and buildings are deleted permanently from their own pages ([categories.md](categories.md) and [buildings.md](buildings.md)). Permanently deleting a box always takes two steps: a soft delete from the Box page, then a permanent delete here (Story_23).

Boxes use the box shape in [boxes.md](boxes.md) (D-173). These endpoints are separate from `/api/boxes`, so box search never has to return soft-deleted boxes (D-205).

---

## GET /api/deleted-boxes

Every soft-deleted box, most recently deleted first, with the box number breaking ties.

### Success: 200

```json
{ "boxes": [ { "id": 6602, "deletedAt": "2026-11-10T21:13:37Z", "deletedBy": { "id": 2, "name": "Thomas Anos" }, "...": "the box shape" } ] }
```

The page shows each box's number, category, location, and "Deleted 11/10/26 by Thomas Anos".

---

## POST /api/deleted-boxes/{id}/restore

Restores a soft-deleted box. It shows up in search, the dashboard, and alerts again, racked or unracked as it was when deleted, and its storage time still counts from `rackedSince`, so it may come back already stored too long.

### Success: 200

The restored box, in the box shape, with `deletedAt` and `deletedBy` null.

The server, in one transaction:

- sets `deleted_at` and `deleted_by` to null, and sets `last_updated_at` to now and `last_updated_by` from the session token, since a restore is a change, as a soft delete is (D-176, D-201)
- writes one audit entry: BOX RESTORED, with the label `#6602` and null `details`

### Errors

| Status | When | `error` |
|---|---|---|
| 404 | No box has this number | This box doesn't exist. |
| 404 | The box isn't soft-deleted (someone already restored it) | This box isn't in Deleted Items. |

---

## DELETE /api/deleted-boxes/{id}

Deletes a soft-deleted box permanently. The app asks first (wireframe 13b). It can't be undone.

### Success: 204

No body.

The server, in one transaction:

- deletes the `BOX` row
- writes one audit entry: BOX PERMANENTLY_DELETED, with the label `#6602` and null `details`

The box's earlier audit entries stay, since the audit log is never edited (Story_16). They keep showing `#6602` from their labels.

### Errors

| Status | When | `error` |
|---|---|---|
| 404 | No box has this number (it never existed, or it was already permanently deleted) | This box doesn't exist. |
| 404 | The box isn't soft-deleted | This box isn't in Deleted Items. |
