#!/bin/sh

set -eu

action=${1:-}
case "$action" in
    start|Start|stop|Stop|status|Status) ;;
    *) echo 'Usage: scripts/hud.sh start|stop|status' >&2; exit 2 ;;
esac

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
plugin_root=$(dirname -- "$script_directory")
case "$(uname -m)" in
    arm64) runtime_id=osx-arm64 ;;
    x86_64) runtime_id=osx-x64 ;;
    *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 1 ;;
esac

app_bundle="$plugin_root/bin/$runtime_id/Codex Usage.app"
packaged_helper="$app_bundle/Contents/MacOS/CodexUsage.Desktop"
project_path="$plugin_root/src/CodexUsage.Desktop/CodexUsage.Desktop.csproj"
output_directory="$plugin_root/src/CodexUsage.Desktop/bin/Debug/net10.0"
development_helper="$output_directory/CodexUsage.Desktop"
runner_source_path="$script_directory/hud-runner.sh"
state_directory=${TMPDIR:-/tmp}
user_id=$(id -u)
runner_path="$state_directory/codex-usage-hud-runner-$user_id.sh"
launch_label="com.jayhilwig.codexusagehud"
service_target="gui/$user_id/$launch_label"
log_file="$state_directory/codex-usage-hud-$user_id.log"
error_log_file="$state_directory/codex-usage-hud-$user_id.error.log"

find_packaged_pids() {
    pgrep -f "$packaged_helper" 2>/dev/null || true
}

read_development_pid() {
    service_description=$(/bin/launchctl print "$service_target" 2>/dev/null) || return 1
    running_pid=$(printf '%s\n' "$service_description" |
        sed -n 's/^[[:space:]]*pid = \([0-9][0-9]*\).*$/\1/p' |
        sed -n '1p')
    case "$running_pid" in
        ''|*[!0-9]*) return 1 ;;
    esac
    return 0
}

report_status() {
    packaged_pids=$(find_packaged_pids)
    if [ -n "$packaged_pids" ]; then
        echo "Codex Usage is running (PID $(printf '%s\n' "$packaged_pids" | sed -n '1p'))."
    elif read_development_pid; then
        echo "Codex Usage is running (PID $running_pid)."
    else
        echo 'Codex Usage is not running.'
    fi
}

case "$action" in
    status|Status)
        report_status
        ;;

    stop|Stop)
        stopped=false
        packaged_pids=$(find_packaged_pids)
        if [ -n "$packaged_pids" ]; then
            kill $packaged_pids
            stopped=true
        fi
        if read_development_pid; then
            /bin/launchctl remove "$launch_label"
            stopped=true
        fi
        if [ "$stopped" = true ]; then
            echo 'Codex Usage stopped.'
        else
            echo 'Codex Usage is already stopped.'
        fi
        ;;

    start|Start)
        packaged_pids=$(find_packaged_pids)
        if [ -n "$packaged_pids" ]; then
            echo "Codex Usage is already running (PID $(printf '%s\n' "$packaged_pids" | sed -n '1p'))."
            exit 0
        fi
        if read_development_pid; then
            echo "Codex Usage is already running (PID $running_pid)."
            exit 0
        fi

        # Packaged installs use the self-contained app and need neither launchd nor .NET.
        if [ -d "$app_bundle" ] && [ -x "$packaged_helper" ]; then
            open "$app_bundle"
            attempts=0
            while [ "$attempts" -lt 25 ]; do
                packaged_pids=$(find_packaged_pids)
                if [ -n "$packaged_pids" ]; then
                    echo "Codex Usage started (PID $(printf '%s\n' "$packaged_pids" | sed -n '1p'))."
                    exit 0
                fi
                attempts=$((attempts + 1))
                sleep 0.2
            done

            echo "Codex Usage did not stay running after opening $app_bundle." >&2
            exit 1
        elif [ -f "$packaged_helper" ]; then
            echo "Codex Usage helper is not executable: $packaged_helper" >&2
            exit 1
        fi

        # A source checkout falls back to the local SDK and a transient development job.
        if command -v dotnet >/dev/null 2>&1; then
            dotnet_command=$(command -v dotnet)
        elif [ -x "$HOME/.dotnet/dotnet" ]; then
            dotnet_command="$HOME/.dotnet/dotnet"
            DOTNET_ROOT="$HOME/.dotnet"
            PATH="$DOTNET_ROOT:$PATH"
            export DOTNET_ROOT PATH
        else
            echo 'Bundled app not found and .NET 10 SDK is unavailable.' >&2
            exit 1
        fi

        "$dotnet_command" build "$project_path" --nologo --verbosity quiet
        if [ ! -x "$development_helper" ]; then
            echo "HUD executable was not produced at $development_helper" >&2
            exit 1
        fi
        if [ ! -f "$runner_source_path" ]; then
            echo "HUD runner was not found at $runner_source_path" >&2
            exit 1
        fi

        /bin/cp "$runner_source_path" "$runner_path"
        /bin/chmod 700 "$runner_path"
        /bin/launchctl remove "$launch_label" 2>/dev/null || true
        if [ -n "${DOTNET_ROOT:-}" ]; then
            /bin/launchctl submit -l "$launch_label" -o "$log_file" -e "$error_log_file" -- \
                /usr/bin/env "DOTNET_ROOT=$DOTNET_ROOT" "PATH=$PATH" \
                /bin/sh "$runner_path" "$launch_label" "$development_helper"
        else
            /bin/launchctl submit -l "$launch_label" -o "$log_file" -e "$error_log_file" -- \
                /usr/bin/env "PATH=$PATH" \
                /bin/sh "$runner_path" "$launch_label" "$development_helper"
        fi

        sleep 0.2
        if ! read_development_pid; then
            echo "Codex Usage did not stay running. See $log_file and $error_log_file" >&2
            exit 1
        fi
        echo "Codex Usage started (PID $running_pid)."
        ;;
esac
