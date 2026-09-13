# macOS installation and test

This is an unsigned self-contained build. The target Mac does not need Git, Visual Studio, the .NET SDK, or a separately installed .NET runtime.

1. Copy the release ZIP for your Mac architecture (`macos-arm64` for Apple silicon or `macos-x64` for Intel) to the Mac and unzip it to a local folder such as `~/Applications/CodexUsage`.
2. In Terminal, install the local plugin with one command:

   ```sh
   sh ~/Applications/CodexUsage/install-macos.sh
   ```

   The installer uses `codex` on PATH, or the CLI bundled in `/Applications/Codex.app` (with the older `/Applications/ChatGPT.app` path retained as a compatibility fallback). It safely reinstalls only this local marketplace and plugin.

3. Open a new Codex task and use `@Codex Usage Start!`. The installer prepares the matching bundled `.app`; the launcher selects the correct Mac architecture.
4. Start Codex Usage. No Accessibility or Screen Recording permission is required; the Mac HUD uses CoreGraphics and Codex bundle identity for window tracking and appears lower-left above the account/profile row.
5. If Gatekeeper blocks the unsigned app, use **System Settings → Privacy & Security → Open Anyway**, or remove quarantine only from the copied local app bundle:

   ```sh
   xattr -dr com.apple.quarantine ~/Applications/CodexUsage/plugins/codex-usage/bin/osx-arm64/Codex\ Usage.app
   ```

   Do not disable Gatekeeper globally.

Use `sh ~/Applications/CodexUsage/plugins/codex-usage/scripts/hud.sh Status` or `Stop` to manage the single helper instance.

Real-Mac verification covers Codex process identity, lower-left window geometry above the account/profile row, Retina placement, popovers, Exit behavior, and unsigned-helper launch behavior.
