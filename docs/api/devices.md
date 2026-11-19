# Devices

The endpoint for the Signed-in Devices page (wireframe 12, Story_26). It takes the session token (D-164) and needs Master mode (`[RequireMasterMode]`), so it can also return 403 "Turn on Master mode first." Errors use the shared format in [README.md](README.md).

The page is view only. Removing a tablet is Spring 2027 (D-187, deferred item 43).

---

## GET /api/devices

Every tablet that has been set up and isn't removed, sorted by tablet name, whether or not anyone is signed in on it, so the Master account can see every tablet that holds a device token (D-211). Removed tablets are left out.

### Success: 200

```json
{
  "devices": [
    {
      "id": 1,
      "name": "Processing tablet",
      "isThisTablet": true,
      "activeEmployee": { "id": 4, "name": "Grace Kim" },
      "isElevated": true,
      "lastActivityAt": "2026-11-19T19:44:00Z"
    },
    {
      "id": 2,
      "name": "Storage trailer tablet",
      "isThisTablet": false,
      "activeEmployee": null,
      "isElevated": false,
      "lastActivityAt": "2026-11-12T15:10:00Z"
    }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `devices` | array | Every tablet that isn't removed, sorted by name, ignoring capital letters |
| `devices[].id` | int | Tablet id |
| `devices[].name` | string | Tablet name |
| `devices[].isThisTablet` | bool | True for the tablet making the request, from the session token. The app shows the "This tablet" tag |
| `devices[].activeEmployee` | object or null | `id` and `name` of the employee signed in on it. Null when no one is, and the app shows "No one signed in" |
| `devices[].isElevated` | bool | Whether Master mode is on there. The app shows "Master on" or "Master off". Always false with no one signed in |
| `devices[].lastActivityAt` | string | The tablet's last request to the server, in UTC (`...Z`). The app shows it in local time |

### Errors

Only the 403 above and the session token errors in [README.md](README.md).
