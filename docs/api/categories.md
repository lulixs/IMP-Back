# Categories

Endpoints for categories. Every endpoint here takes the session token (D-164), and errors use the shared format in [README.md](README.md).

Any employee can add a category and edit its name and symbol (Story_24). Only the Master account can delete one, with Master mode on. Deleting is permanent: categories have no soft delete (D-123).

## The category

| Field | Type | Meaning |
|---|---|---|
| `id` | int | Category id |
| `name` | string | 1 to 100 characters after trimming, with at least one letter. Unique, ignoring capital letters, so `Coats` and `coats` can't both exist |
| `symbol` | string or null | One emoji, chosen with the app's emoji picker, or null. At most 16 characters. The server doesn't check that it is an emoji |
| `activeBoxCount` | int | Boxes in this category that aren't soft-deleted, racked or not. Only in `GET /api/categories` (D-198) |
| `deletedBoxCount` | int | Soft-deleted boxes in this category. Only in `GET /api/categories` |

A category name needs a letter (D-199) because box search treats text made only of digits as a box number (D-172), so a category named `2024` could never be found by searching.

---

## GET /api/categories

Every category. Used by the Category dropdowns on the Box page and New Box, the category filter on the Boxes list, and the Categories page (wireframe 08a).

### Success: 200

```json
{
  "categories": [
    { "id": 1, "name": "Coats", "symbol": "🧥", "activeBoxCount": 12, "deletedBoxCount": 1 },
    { "id": 2, "name": "Easter Baskets", "symbol": null, "activeBoxCount": 0, "deletedBoxCount": 0 }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `categories` | array | Every category, sorted by name, ignoring capital letters |

The dropdowns and the filter ignore the two counts. The Categories page shows "12 active boxes" from `activeBoxCount`. With Master mode on, a category with any boxes (`activeBoxCount + deletedBoxCount > 0`) shows "in use, cannot be deleted" and its Delete button is grayed out.

### Errors

Only the session token errors in [README.md](README.md).

---

## POST /api/categories

Adds a category. Any employee can do it. Used by the New category dialog (wireframe 08b).

### Request

```json
{ "name": "Spring Decor", "symbol": "🌷" }
```

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Required. The name rules above |
| `symbol` | string or null | Optional. Trimmed, and blank becomes null |

### Success: 201

The new category, without the two counts:

```json
{ "id": 10, "name": "Spring Decor", "symbol": "🌷" }
```

The server, in one transaction:

- creates the `CATEGORY` row
- writes one audit entry: CATEGORY ADDED, with the label `Spring Decor` and null `details`

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | `name` is missing or blank after trimming | Enter a category name. |
| 400 | `name` is longer than 100 characters | Category names can be at most 100 characters. |
| 400 | `name` has no letter | A category name needs at least one letter. |
| 400 | `symbol` is longer than 16 characters | Choose one emoji for the symbol. |
| 409 | Another category has this name, ignoring capital letters | A category with this name already exists. Choose another name. |

---

## PUT /api/categories/{id}

Changes a category's name and symbol. Any employee can do it. Used by Edit on the Categories page. Boxes store the category's id, so every box shows the new name at once.

### Request

```json
{ "name": "Spring Decorations", "symbol": null }
```

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Required. The name rules above |
| `symbol` | string or null | Required. Send the symbol even if unchanged. Null removes it |

There is no check for someone else's change in between, unlike boxes (D-174), because `CATEGORY` has no `last_updated_at`. The last save wins (D-200).

### Success: 200

The saved category, without the two counts. If nothing changed, it comes back as it was, with no audit entry.

When something changed, the server, in one transaction:

- saves the name and symbol
- writes one audit entry: CATEGORY EDITED, with the new name as the label, and `details` listing the changes to `name` and `symbol`, as in the Audit details section of [boxes.md](boxes.md)

### Errors

| Status | When | `error` |
|---|---|---|
| 400 | A field breaks its rule | The same sentences as `POST /api/categories` |
| 404 | No category has this id | This category doesn't exist. |
| 409 | Another category has this name, ignoring capital letters | A category with this name already exists. Choose another name. |

---

## DELETE /api/categories/{id}

Deletes a category permanently. Master mode only (`[RequireMasterMode]`). The app asks first (wireframe 08c), and only offers Delete for a category with no boxes, but the server checks again.

### Success: 204

No body.

The server, in one transaction:

- checks that no box, active or soft-deleted, uses the category (D-042)
- deletes the `CATEGORY` row
- writes one audit entry: CATEGORY PERMANENTLY_DELETED, with the category's name as the label and null `details`

The foreign key on `BOX.category_id` is the last guard: if a box is added between the check and the delete, the delete fails and the server answers with the 409 below.

### Errors

| Status | When | `error` |
|---|---|---|
| 403 | Master mode is off | Turn on Master mode first. |
| 404 | No category has this id | This category doesn't exist. |
| 409 | Boxes still use the category | These boxes still use this category. Move the active boxes to another category, and permanently delete or restore and move the deleted ones first. |

The 409 also lists the boxes in the way in `details` (D-204), sorted by box number, so the app can show them as wireframe 11a does for buildings:

```json
{
  "error": "These boxes still use this category. Move the active boxes to another category, and permanently delete or restore and move the deleted ones first.",
  "details": {
    "boxes": [
      { "id": 6377, "isDeleted": true },
      { "id": 6744, "isDeleted": false }
    ]
  }
}
```

| Field | Type | Meaning |
|---|---|---|
| `details.boxes` | array | Every box that uses the category |
| `details.boxes[].id` | int | The box number |
| `details.boxes[].isDeleted` | bool | True for a soft-deleted box. The app shows "soft-deleted" or "active" |
