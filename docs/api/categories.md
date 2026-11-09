# Categories

Endpoints for categories. Every endpoint here takes the session token (D-164), and errors use the shared format in [README.md](README.md).

Adding, editing, and deleting categories are still to be written (Luke's week 11 task).

## GET /api/categories

Every category. Used by the Category dropdowns on the Box page and New Box, the category filter on the Boxes list, and the Categories page.

### Success: 200

```json
{
  "categories": [
    { "id": 1, "name": "Coats", "symbol": "🧥" },
    { "id": 2, "name": "Easter Baskets", "symbol": null }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `categories` | array | Every category, sorted by name |
| `categories[].id` | int | Category id |
| `categories[].name` | string | Category name |
| `categories[].symbol` | string or null | The category's emoji, null if none |

### Errors

Only the session token errors in [README.md](README.md).
