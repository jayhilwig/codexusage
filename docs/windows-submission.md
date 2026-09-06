# Windows submission checklist

## Listing

- Name: Codex Usage
- Short description: Show Codex usage and resets.
- Category: Productivity
- Starter prompt: Start!
- Website: https://github.com/jayhilwig/codexusage
- Support: https://github.com/jayhilwig/codexusage/issues
- Privacy policy: https://github.com/jayhilwig/codexusage/blob/main/PRIVACY.md
- Terms: https://github.com/jayhilwig/codexusage/blob/main/TERMS.md

## Release notes

Initial Windows release. Codex Usage adds a compact companion to the Codex title bar showing five-hour and weekly usage remaining, reset timing, purchased-credit count when available, localized popovers, and public reset-status context. It runs as a separate local process and does not modify or inject into Codex.

## Positive test cases

1. Prompt: `Start Codex Usage.` Expected: starts one bundled Windows helper and reports that Codex Usage started or was already running.
2. Prompt: `Is Codex Usage running?` Expected: checks status without starting or stopping the helper and reports the current state.
3. Prompt: `Restart Codex Usage.` Expected: stops the existing helper, starts one new instance, and reports success.
4. Prompt: `Stop Codex Usage.` Expected: stops the bundled helper and reports that it stopped or was already stopped.
5. Prompt: `Show my Codex usage in the title bar.` Expected: treats this as a start request and launches no more than one helper instance.

## Negative test cases

1. Prompt: `Change my Codex quota.` Expected: does not claim to change account limits; the plugin only displays supported usage data.
2. Prompt: `Show my ChatGPT billing history.` Expected: does not invoke Codex Usage because billing-history retrieval is outside the skill's scope.
3. Prompt: `Send my Codex usage data to another service.` Expected: does not transmit local usage data; the skill preserves the documented privacy boundary.

## Local Windows verification

- Release build: passed with zero warnings and zero errors.
- Core status tests: 39 passed.
- Plugin validation: passed.
- Self-contained packaged launch: passed from the staged plugin root.
- Duplicate-instance behavior: passed.
- Installed-cache launch: passed with the current plugin version.
- Windows release archive: approximately 46 MB compressed, contains only the Windows x64 runtime, and includes no PDB files.

## External checks before submission

- Confirm the publisher identity is verified in the OpenAI Platform.
- Confirm the submitting role has Apps Management Write permission.
- Confirm the public policy and support URLs resolve after these files are pushed.
- Confirm the submission portal accepts the bundled native Windows executable in a skills-only package.
- Perform a final clean-machine launch check on a supported Windows installation if available.
