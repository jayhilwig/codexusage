Codex Usage

Manage the native companion through the platform launcher resolved relative to this skill directory.

Windows: run ../../scripts/hud.ps1 -Action Start|Stop|Status directly in the current PowerShell command host. Do not start a second PowerShell process or assume a fixed PowerShell 7 path. If direct invocation is unavailable, resolve the shell once in this order: the current PowerShell executable from (Get-Process -Id $PID).Path, Get-Command pwsh.exe, Get-Command powershell.exe, then %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe when present. Windows PowerShell 5.1 is sufficient; use the first resolved executable immediately.

macOS: run sh ../../scripts/hud.sh Start|Stop|Status.

Restart: stop, then start.

Run Start and Restart only from a local Codex desktop host on Windows or macOS while the Codex desktop app is open. If the current environment is web, mobile, cloud, remote, or cannot launch a native process on the user's interactive desktop, explain that Codex Usage is unavailable there and do not run the launcher.

Starting a native desktop window may require host approval. On Windows, request the required GUI/external-process approval before the first Start or Restart attempt, and launch directly in the user's interactive desktop context. Do not first launch inside a restricted sandbox because the process may run without a visible overlay.

For Start, invoke the launcher immediately. Do not search for the launcher first, inspect the environment first, narrate intermediate startup steps, or run a separate Status command after a successful Start. Run Status or perform diagnostic discovery only if Start returns an error or its result is ambiguous.

For Restart, stop and then start immediately. Only run Status afterward if the restart result is ambiguous or reports an error.

Packaged installs use the bundled self-contained helper for the matching OS/architecture. A macOS source checkout falls back to its local .NET SDK. Keep the response concise; do not narrate implementation details unless an error occurs.

Do not read, display, or transmit Codex credentials. Do not send local usage data to any third party. The overlay itself makes only the existing anonymous request to the public codex-resets.com API.

The plugin does not modify or inject into the Codex desktop app. Its visible interface remains a separate native overlay because plugin UI cannot occupy the operating system caption area.