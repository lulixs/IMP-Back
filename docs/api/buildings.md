# Buildings

Endpoints for buildings. Every endpoint here takes the session token (D-164), and errors use the shared format in [README.md](README.md).

Only the Master account can add and remove buildings, with Master mode on (D-041). Removing is permanent: buildings have no soft delete, and a building can't be renamed.

## The building

| Field | Type | Meaning |
|---|---|---|
| `id` | int | Building id |
| `name` | string | 1 to 100 characters after trimming. Unique, ignoring capital letters |
| `activeBoxCount` | int | Boxes in this building that aren't soft-deleted, racked or not. Only in `GET /api/buildings` (D-198) |
| `deletedBoxCount` | int | Soft-deleted boxes in this building. Only in `GET /api/buildings` |

---

## GET /api/buildings

Every building. Any employee can call it. Used by the Building dropdowns on the Box page and New Box, the re-rack popup, and the Building Management page (wireframe 11a).

### Success: 200

```json
{
  "buildings": [
    { "id": 1, "name": "Back Garage", "activeBoxCount": 0, "deletedBoxCount": 0 },
    { "id": 2, "name": "Old Annex", "activeBoxCount": 3, "deletedBoxCount": 2 }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `buildings` | array | Every building, sorted by name, ignoring capital letters |

The dropdowns ignore the two counts. The Building Management page shows "5 boxes (2 deleted)" from them.

### Errors

Only the session token errors in [README.md](README.md).

---

## POST /api/buildings

Adds a building. Master mode only (`[RequireMasterMode]`). Used by the Add building dialog (wireframe 11b).

### Request

```json
{ "name": "Warehouse 2" }
```

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Required. The name rules above |

### Success: 201

The new building, without the two counts:

```json
{ "id": 5, "name": "Warehouse 2" }
```

The server, in one transaction:

- creates the `BUILDING` row
- writes one audit entry: BUILDING ADDED, with the label `Warehouse 2` and null `details`

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | `name` is missing or blank after trimming | Enter a building name. |
| 400 | `name` is longer than 100 characters | Building names can be at most 100 characters. |
| 403 | Master mode is off | Turn on Master mode first. |
| 409 | Another building has this name, ignoring capital letters | A building with this name already exists. Choose another name. |

---

## DELETE /api/buildings/{id}

Removes a building permanently. Master mode only (`[RequireMasterMode]`). The app asks first (wireframe 11c). Unlike categories, the Remove button is always offered, and a blocked removal shows the boxes in the way (wireframe 11a).

### Success: 204

No body.

The server, in one transaction:

- checks that no box, active or soft-deleted, uses the building (D-042)
- deletes the `BUILDING` row
- writes one audit entry: BUILDING PERMANENTLY_DELETED, with the building's name as the label and null `details`

The foreign key on `BOX.building_id` is the last guard: if a box is moved in between the check and the delete, the delete fails and the server answers with the 409 below.

### Errors

| Status | When | `error` |
|---|---|---|
| 403 | Master mode is off | Turn on Master mode first. |
| 404 | No building has this id | This building doesn't exist. |
| 409 | Boxes still use the building | These boxes still use this building. Move the active boxes to another building, and permanently delete or restore and move the deleted ones first. |

The 409 lists the boxes in the way in `details` (D-204), the same way as `DELETE /api/categories/{id}` in [categories.md](categories.md):

```json
{
  "error": "These boxes still use this building. Move the active boxes to another building, and permanently delete or restore and move the deleted ones first.",
  "details": {
    "boxes": [
      { "id": 6377, "isDeleted": true },
      { "id": 6744, "isDeleted": false }
    ]
  }
}
```
