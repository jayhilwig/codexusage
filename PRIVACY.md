# Codex Usage Privacy Policy

Last updated: September 6, 2026

Codex Usage is a local desktop companion for Codex. It does not require a separate account and does not collect analytics, advertising identifiers, or telemetry for the developer.

## Data processed locally

Codex Usage reads usage-limit and purchased-credit information from the locally installed Codex app server. It stores only the information needed to display usage and reset state, along with the selected locale, in the operating system's local application-data directory.

Codex credentials and authentication data remain managed by Codex. Codex Usage does not read, store, or transmit authentication tokens, account identifiers, prompts, source code, or conversation content.

## Network requests

Codex Usage requests public reset-status information from `https://codex-resets.com/api/v1/status`. Local Codex usage values are not sent to that service. As with any web request, the service may receive ordinary connection metadata such as the requesting IP address.

When a user selects the Credits link, Codex Usage opens the official OpenAI Codex Usage and Billing page in the default browser. Codex Usage does not process payment information.

## Data retention and deletion

Locally cached state remains on the device until it is overwritten or deleted by the user. On Windows, it can be removed by deleting the `CodexUsageHud` and `Codex Usage` folders under the user's local application-data directory.

## Contact

For questions or privacy requests, open an issue at https://github.com/jayhilwig/codexusage/issues.
