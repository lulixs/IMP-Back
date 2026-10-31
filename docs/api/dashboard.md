# GET /api/dashboard

Returns one JSON object: the alert threshold, then one entry per category.

```json
{
  "alertThresholdDays": 180,
  "categories": [
    { "id": 1, "name": "Coats", "symbol": "🧥", "full": 3, "half": 2, "empty": 1, "storedTooLong": 3 },
    { "id": 2, "name": "Easter Baskets", "symbol": null, "full": 0, "half": 0, "empty": 0, "storedTooLong": 0 }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `alertThresholdDays` | int | `MASTER_ACCOUNT.alert_threshold_days` |
| `categories` | array | One object per category, below |

Each category:

| Field | Type | Meaning |
|---|---|---|
| `id` | int | Category id |
| `name` | string | Category name |
| `symbol` | string or null | Category emoji, null if none |
| `full`, `half`, `empty` | int | Racked, not-deleted boxes with that fullness |
| `storedTooLong` | int | Racked, not-deleted boxes with `racked_since` older than `alertThresholdDays` |

Every category is included, even with 0 boxes. Sorted by name.
