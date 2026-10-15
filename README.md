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

If `AutoConnect` is `true`, the service connects and starts reading on startup and
disconnects on shutdown. Otherwise, use the `/api/reader` endpoints to control it.

## API

| Method | Path                  | Description                                                   |
|--------|-----------------------|-----------------------------------------------------------------|
| GET    | `/api/reader/status`  | Connection/start state, firmware, model, temperature, last error |
| POST   | `/api/reader/connect` | Connect (optional JSON body `{ "hostname": "..." }` to override config) |
| POST   | `/api/reader/disconnect` | Stop reading (if started) and disconnect |
| POST   | `/api/reader/start`   | Start the configured read operation (RF on) |
| POST   | `/api/reader/stop`    | Stop reading |
| GET    | `/api/tags`           | Snapshot of tags currently "in view" (seen within `TagSnapshotExpirySeconds`) |
| GET    | `/api/tags/recent?count=100` | Most recent tag reads, oldest first |
| DELETE | `/api/tags`           | Clear the in-memory tag buffers |

Errors from the reader surface as JSON `{ "error": "..." }`:
- `409 Conflict` for invalid state transitions (e.g. connecting twice, starting while disconnected).
- `502 Bad Gateway` for Octane SDK errors (e.g. connection timeout, reader rejected settings).

## Notes

- `ImpinjReaderManager` (`src/RfidReaderService/Services/ImpinjReaderManager.cs`) owns the single
  `ImpinjReader` instance for the process and serializes all SDK calls behind a lock, since the
  SDK's reader object is not safe for concurrent use from multiple HTTP requests.
- Tag reports are kept in memory only (a bounded recent-reads queue plus a per-EPC snapshot map).
  There is no persistence layer; add one (e.g. write-behind to a database or message queue) if
  reads need to be durable.
