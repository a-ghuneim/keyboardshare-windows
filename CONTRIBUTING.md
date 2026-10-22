# Contributing to KeyboardShare

Small, focused contributions are welcome. Reliable keyboard routing and low latency take priority over additional UI or integrations.

## Bugs and feature requests

Search existing GitHub issues first. For a bug, include Windows versions on both laptops, application version, Host/Client roles, keyboard layout, standard/elevated app status, reproduction steps, expected/actual results, and whether Mouse Without Borders is running. Include sanitized log excerpts if useful. Remove tokens, personal IP addresses, usernames, local paths, and sensitive application content. Never upload `config.json` or real keystroke recordings.

For a feature request, explain the use case and how it fits a two-computer, keyboard-only utility. Report security problems privately as described in [SECURITY.md](SECURITY.md).

## Local build and automated checks

Use Windows and the .NET 8 SDK. From the repository root:

```powershell
dotnet build KeyboardShare/KeyboardShare.csproj -c Release
dotnet run --project KeyboardShare.Tests/KeyboardShare.Tests.csproj -c Release
```

The tests are a dependency-free console executable, not a test-framework project: use `dotnet run`, not `dotnet test`. A failure produces a nonzero exit code. Tests use loopback TCP and do not inject keyboard input. They currently use the same application log location as the tray application, so test failures may add entries to your local log.

See [README.md](README.md#building-from-source) for publish commands and [manual acceptance checks](README.md#manual-acceptance-checks) for keyboard/desktop testing that automation does not cover.

## Pull requests

1. Fork the repository and make a branch for one change.
2. Keep keyboard/network changes small; explain behavior changes and protocol compatibility. Avoid work on the hook callback that can block input.
3. Add meaningful checks for changed behavior where practical and run the commands above. For hook/injection changes, test on two real Windows computers and record layouts, privilege levels, disconnects, and results.
4. Update documentation if settings, shortcuts, wire messages, or setup change.
5. Review your diff for secrets and generated files. Submit a PR describing the problem, resulting behavior, validation, and limitations.

Never add personal settings, shared tokens, binaries, `bin/`, or `obj/`. Source contributions are made under the repository's MIT license; keep any necessary third-party notices.
