# Buildings

Endpoints for buildings. Every endpoint here takes the session token (D-164), and errors use the shared format in [README.md](README.md).

Adding and removing buildings are still to be written (Luke's week 11 task).

## GET /api/buildings

Every building. Used by the Building dropdowns on the Box page and New Box, the re-rack popup, and the Buildings page.

### Success: 200

```json
{
  "buildings": [
    { "id": 1, "name": "Back Garage" },
    { "id": 2, "name": "Processing Center" }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `buildings` | array | Every building, sorted by name |
| `buildings[].id` | int | Building id |
| `buildings[].name` | string | Building name |

### Errors

Only the session token errors in [README.md](README.md).
