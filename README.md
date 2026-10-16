# RFID Reader Service

ASP.NET Core (.NET 10) Web API that connects to an Impinj R700 (or any Octane-based)
RFID reader over the network using the official Octane SDK, and exposes the reader's
connection state and tag reads over a small REST API.

## Layout

- `octanesdk.6.0.0.nupkg` — the Octane SDK package as downloaded from Impinj (not on nuget.org).
- `local-packages/` — a local NuGet feed folder containing that package; referenced by `NuGet.Config`.
- `NuGet.Config` — adds `local-packages` as a package source alongside nuget.org, so
  `dotnet restore` works out of the box without any manual `dotnet nuget add source` step.
- `src/RfidReaderService/` — the Web API project.

## Running

```bash
cd src/RfidReaderService
dotnet run
```

The API listens on the URL(s) in `Properties/launchSettings.json` (default profile uses
`http://localhost:5284`). OpenAPI JSON is available at `/openapi/v1.json` in development.

## Configuration

Set the reader's hostname/IP and tuning parameters in `appsettings.json` (or override via
environment variables / `appsettings.Development.json`):

```json
"Reader": {
  "Hostname": "192.168.1.100",
  "AutoConnect": false,
  "ConnectTimeoutMs": 5000,
  "MessageTimeoutMs": 5000,
  "AntennaPorts": [ 1 ],
  "MaxTxPower": true,
  "TxPowerInDbm": null,
  "MaxRxSensitivity": true,
  "RxSensitivityInDbm": null,
  "RfMode": 1002,
  "SearchMode": "DualTarget",
  "Session": 2,
  "MaxRecentTags": 500,
  "TagSnapshotExpirySeconds": 10
}
```

These `Reader:*` values only matter the first time the service runs — they seed
`reader-connection-settings.json` (see below), which is the source of truth for
connection/antenna/inventory settings from then on.

## Mutable connection settings

Unlike the static values above, the reader's address, reconnect policy, enabled antennas,
and inventory tuning are managed at runtime via `GET`/`PUT /api/reader/settings` and
persisted to `reader-connection-settings.json` (path configurable via
`Reader:ConnectionSettingsFilePath`), so they survive a restart. That file is
git-ignored — it's per-environment runtime state, not source.

If `AutoConnectOnStartup` (in that persisted settings, not `appsettings.json`) is `true`,
the service connects and starts reading on startup and disconnects on shutdown. Otherwise,
use the `/api/reader` endpoints to control it.

If `AutoReconnect` is `true`, a dropped connection is retried automatically every
`ReconnectIntervalMs` up to `MaxReconnectAttempts` (or indefinitely if
`UnlimitedReconnectAttempts` is `true`), resuming reading afterward if it was active when
the connection dropped.

## API

| Method | Path                  | Description                                                   |
|--------|-----------------------|-----------------------------------------------------------------|
| GET    | `/api/reader/status`  | Connection/start state, firmware, model, temperature, last error |
| POST   | `/api/reader/connect` | Connect (optional JSON body `{ "hostname": "..." }` to override the stored IP) |
| POST   | `/api/reader/disconnect` | Stop reading (if started) and disconnect |
| POST   | `/api/reader/start`   | Start the configured read operation (RF on) |
| POST   | `/api/reader/stop`    | Stop reading |
| GET    | `/api/reader/settings` | Current connection/antenna/inventory settings (see below) |
| PUT    | `/api/reader/settings` | Replace connection/antenna/inventory settings |
| GET    | `/api/tags`           | Snapshot of tags currently "in view" (seen within `TagSnapshotExpirySeconds`) |
| GET    | `/api/tags/recent?count=100` | Most recent tag reads, oldest first |
| DELETE | `/api/tags`           | Clear the in-memory tag buffers |

Errors from the reader surface as JSON `{ "error": "..." }`:
- `400 Bad Request` for invalid settings (e.g. unrecognized `searchMode`, out-of-range `port`, empty `enabledAntennaPorts`).
- `409 Conflict` for invalid state transitions (e.g. connecting twice, starting while disconnected).
- `502 Bad Gateway` for Octane SDK errors (e.g. connection timeout, reader rejected settings).

### `ReaderConnectionSettingsDto` shape

```json
{
  "readerName": "Main RFID Reader",
  "ipAddress": "192.168.1.10",
  "port": 5084,
  "connectionTimeoutMs": 5000,
  "autoReconnect": false,
  "reconnectIntervalMs": 5000,
  "maxReconnectAttempts": 10,
  "unlimitedReconnectAttempts": false,
  "autoConnectOnStartup": false,
  "availableAntennaPorts": null,
  "enabledAntennaPorts": [1],
  "inventorySettings": {
    "rfMode": 1002,
    "searchMode": "DualTarget",
    "session": 2,
    "powerDbm": null
  }
}
```

Notes:
- `availableAntennaPorts` is **read-only** — it's populated automatically from the reader's
  own feature set after a successful connect (`null` until then), and any value sent in a
  `PUT` body for this field is ignored in favor of the server's own value.
- `inventorySettings` fields are all nullable; a `null` field is left at the Octane SDK's
  own default instead of being applied. `powerDbm` is applied per-antenna (there's no
  reader-wide power setting in the SDK) to every port listed in `enabledAntennaPorts`.
- `searchMode` must match an Octane SDK `SearchMode` enum name (e.g. `"DualTarget"`,
  `"SingleTarget"`); an unrecognized value is rejected with `400`, not silently ignored.

## Notes

- `ImpinjReaderManager` (`src/RfidReaderService/Services/ImpinjReaderManager.cs`) owns the single
  `ImpinjReader` instance for the process and serializes all SDK calls behind a lock, since the
  SDK's reader object is not safe for concurrent use from multiple HTTP requests.
- Tag reports are kept in memory only (a bounded recent-reads queue plus a per-EPC snapshot map).
  There is no persistence layer; add one (e.g. write-behind to a database or message queue) if
  reads need to be durable.
