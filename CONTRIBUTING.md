# Contributing to Uriel.Profiler

Thanks for your interest! Uriel is a small, focused library and contributions
are welcome — bug reports, docs, and code.

## Ground rules

- Be respectful — see the [Code of Conduct](CODE_OF_CONDUCT.md).
- Keep the **performance contract** intact: the hot path only enqueues to a
  bounded lock-free channel, never blocks, never throws. A profiler must never
  crash or stall its host.
- Keep it **release-safe**: no `#if DEBUG` gating, no debugger dependency.
- Prefer small, focused PRs with a clear description of the *why*.

## Project layout

| Path | What |
|---|---|
| `src/Uriel/` | The library (the published NuGet). |
| `samples/UrielDemo/` | MAUI demo app used for on-device validation. |
| `samples/Harness/` | Plain console harness for the platform-agnostic engine. |
| `tools/` | Stream parser + simulator/emulator drive scripts. |
| `docs/` | User wiki. |

## Building

You need the .NET SDK pinned in [`global.json`](global.json) and, for the
mobile heads, the MAUI workload.

```bash
# Engine only (desktop, no workload needed):
dotnet build src/Uriel/Uriel.csproj -f net9.0

# Full multi-target build / package (needs the MAUI workload):
dotnet workload install maui
dotnet pack src/Uriel/Uriel.csproj -c Release
```

## Validating on a device/simulator

The engine is exercised end-to-end through the demo app. See
[docs/Demo-and-Validation.md](docs/Demo-and-Validation.md) and the
`tools/qorvex_drive*.sh` scripts.

```bash
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-ios -c Debug \
  -p:RuntimeIdentifier=iossimulator-x64
tools/qorvex_drive.sh uriel <SIM_UDID> com.companyname.urieldemo /tmp/uriel.log
grep '@@URIEL@@' /tmp/uriel.log | python3 tools/uriel_parse.py
```

## Pull requests

1. Fork and branch from `main`.
2. Make the change; keep the diff minimal and matching the surrounding style
   (see [`.editorconfig`](.editorconfig)).
3. If behavior changes, add a note under **[Unreleased]** in
   [CHANGELOG.md](CHANGELOG.md).
4. Open the PR against `main` and fill in the template.

## Releases (maintainers)

Releases are automated. Tagging `vX.Y.Z` triggers
[`.github/workflows/release.yml`](.github/workflows/release.yml), which packs
and publishes to NuGet.org and attaches the package to a GitHub Release. See
the workflow header for the full checklist.
