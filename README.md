# Codex Usage title-bar plugin

By Jay Hilwig — [jhilwig.com](https://jhilwig.com)

A Codex plugin that manages a separate companion window placing live Codex usage and public reset status left of the caption buttons on Windows and above the lower-left account row on macOS. It does not patch, inject into, or alter the installed Codex app.

The plugin manifest is `.codex-plugin/plugin.json`. Its `codex-usage-hud` skill starts, stops, or checks the native companion through `scripts/hud.ps1` on Windows or `scripts/hud.sh` on macOS. The operating-system overlay remains a separate process because Codex plugin UI cannot occupy these native app regions.

Both implementations are live-verified. The Windows implementation remains isolated and unchanged by the Mac host. macOS uses CoreGraphics window metadata and the Codex bundle identifier (`com.openai.codex`) without requesting Accessibility or Screen Recording access. Packaged builds contain self-contained helpers for all supported architectures.

## What the POC includes

- Finds the packaged Codex desktop window (`OpenAI.Codex_*\\app\\ChatGPT.exe`) or an unpackaged `OpenAI\\Codex\\Codex.exe` window.
- Renders `5h -- · W -- ↺`, then updates it with real remaining percentages.
- Uses a borderless, no-activate window kept directly above Codex in Windows z-order and a floating auxiliary panel on macOS.
- Polls window geometry every 100 ms on Windows and 200 ms on macOS, follows moves/resizes, handles platform display coordinates, and hides when Codex is not visible.
- Opens compact usage and reset cards from the two control regions.
- Uses the platform's native system UI font and compact white, softly shadowed popovers that dismiss when focus moves outside them.
- Reads Codex usage over app-server stdio and reacts to `account/rateLimits/updated` (plus a one-minute fallback refresh).
- Reads the documented anonymous reset endpoint every five minutes, with ETag support and last-success caching.
- Persists only sanitized usage snapshots, public reset data, locale preferences, and a confirmed-event marker under the platform's local application-data directory.
- Keeps Codex credentials local. The only third-party request is `GET https://codex-resets.com/api/v1/status`.

The three V1 preference controls (launch with Windows, show reset indicator, show usage HUD) are deliberately not in this first POC. There is no installer, updater, tray app, analytics, telemetry, account system, or dashboard. Right-click the HUD to exit it.

## Stack

- **.NET 10 / C#** for a small native process, async stdio JSON handling, and direct platform interop without a browser runtime.
- **Avalonia 12.1.1** for one UI implementation that can run on Windows and macOS. Only the platform window tracker is OS-specific.
- **Win32 + DWM APIs** for top-level window enumeration, package/process identification, caption-button bounds, minimized/cloaked state, dark-mode hint, z-order, and physical-pixel positioning.
- **CoreGraphics + AppKit metadata** for permission-free macOS window discovery, frontmost-app checks, and auxiliary-panel behavior.

This keeps the cross-platform boundary explicit:

```text
src/CodexUsage.Core/
  usage/  Codex app-server client + normalized usage models
  reset/  public reset client + resolver + minimal local state
  window/ platform-neutral window snapshot/tracker contract
  ui/     platform-neutral HUD view model and time formatting

src/CodexUsage.Desktop/
  ui/       Avalonia HUD and both popovers
  Platform/ isolated Windows and macOS trackers/hosts/interops
```

On macOS, `CGWindowListCopyWindowInfo` provides owner PID, geometry, ordering, and visibility, then `NSRunningApplication.bundleIdentifier` verifies Codex. It does not inspect pixels or the accessibility tree. A backed, left-aligned strip follows the lower-left account area, and its popovers remain clamped inside the Codex window.

## Exact data interfaces

### Codex

The app starts the locally installed Codex process as:

```text
codex app-server --stdio
```

It performs the documented initialize handshake and sends:

```json
{ "method": "account/rateLimits/read", "id": 2 }
```

It also listens for:

```text
account/rateLimits/updated
```

The schema was verified against the installed `codex-cli 0.150.0-alpha.8` using:

```powershell
codex app-server generate-json-schema --out .tmp/app-server-schema --experimental
```

The live account response uses the `codex` bucket with:

- `primary.windowDurationMins = 300`
- `secondary.windowDurationMins = 10080`
- `usedPercent`
- `resetsAt` as Unix seconds

The client selects windows by duration rather than assuming primary/secondary ordering. Displayed remaining usage is `clamp(100 - usedPercent, 0, 100)`.

Official reference: [Codex App Server](https://developers.openai.com/codex/app-server).

### codex-resets.com

The docs page points to `/api/openapi.json`. The POC uses the documented status operation:

```text
GET https://codex-resets.com/api/v1/status
```

Relevant fields are `data.latest_reset`, `data.active_watch`, reset/watch timestamps, and `source.url`. No rendered HTML is scraped.

Reference: [codex-resets.com API docs](https://codex-resets.com/api/docs).

## Reset-state resolution

- **Gray:** no fresh, recent regular announcement and no active strong/elevated watch.
- **Amber:** a regular or banked announcement is less than eight hours old but local quota has not met the confirmation threshold; also used for an unexpired `strong` or `elevated` watch.
- **Green:** a recent regular public announcement exists, both five-hour and weekly remaining percentages rose by at least 20 points between local snapshots, at least one is now 90% or higher, the announcement falls within the comparison interval, and neither prior natural reset timestamp fell between those snapshots.
- **Unknown gray:** the public API request failed. Cached public data remains on disk but is not used to show stale certainty.

A confirmed event remains green for six hours. The resolver never treats the public site alone as account confirmation, never sends local usage to that site, and never auto-confirms banked-reset announcements.

## Run

Source requirements: Windows 10/11 or macOS 13+, .NET 10 SDK, a current signed-in Codex CLI on `PATH`, and the Codex desktop window running. Packaged self-contained helpers do not require .NET on the target machine.

```powershell
dotnet restore src\CodexUsage.Desktop\CodexUsage.Desktop.csproj
dotnet run --project src\CodexUsage.Desktop\CodexUsage.Desktop.csproj
```

After a build, you can also launch the companion directly while Codex is open:

```powershell
.\src\CodexUsage.Desktop\bin\Debug\net10.0\CodexUsage.Desktop.exe
```

On macOS, use the development launcher from a source checkout:

```sh
sh scripts/hud.sh Start
sh scripts/hud.sh Status
sh scripts/hud.sh Stop
```

`scripts/publish-package.ps1` produces self-contained Windows and macOS helpers plus the USB-test package.

If `codex` is not on `PATH`, set `CODEX_HUD_CODEX_PATH` to the executable path before launching. Right-click the HUD and choose **Exit Codex Usage**, or stop the foreground `dotnet run` process with Ctrl+C.

Run the focused resolver checks:

```powershell
dotnet run --project tests\CodexUsage.Core.Tests\CodexUsage.Core.Tests.csproj
```

## Verification completed

- Debug build: clean, zero warnings/errors.
- Live Codex window detection: packaged Windows app found by executable path.
- Live macOS window detection: Codex found by bundle identifier using permission-free CoreGraphics metadata.
- Live usage: real percentages and reset times displayed from app-server.
- Live public status: current `/api/v1/status` response parsed successfully.
- Move following: a temporary `160,80` Codex move produced the same `160,80` HUD delta, then the original placement was restored.
- Minimize behavior: zero visible HUD windows while Codex was minimized; one returned after restore.
- Baseline visual capture: the original HUD and both popovers rendered in the intended title-bar location at the active display scale. The current typography/popover refinement builds cleanly; a fresh interactive capture was unavailable from the non-interactive validation session.
- macOS visual verification: the lower-left backed strip, click-anchored popovers, hidden Dock icon, outside-click dismissal, and persistent Exit lifecycle were tested on a real Mac.
- Resolver harness: six cases pass (offline, announced, confirmed, natural-reset exclusion, stale event, strong watch).

## Documented vs. packaging-specific behavior

The app-server method, notification, and rate-limit fields are documented by OpenAI. The codex-resets.com endpoint and payload are documented by its OpenAPI file. Windows positioning uses documented Win32/DWM APIs.

The one packaging-specific dependency is Windows Codex window identification. The tracker prefers the installed package-family identity beginning with `OpenAI.Codex_`, with the current `ChatGPT.exe` package path and unpackaged `Codex.exe` path retained as fallbacks. Those names are not an app-server contract and may change in a future Codex release. Identification remains isolated to `WindowsCodexWindowTracker`, so updating it does not affect shared code or either data source.
