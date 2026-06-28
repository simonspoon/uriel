<!-- Thanks for contributing to Uriel.Profiler! -->

## What & why

<!-- What does this change and why? Link any related issue: Closes #123 -->

## Checklist

- [ ] Hot path still only enqueues — never blocks, never throws (performance contract intact).
- [ ] No `#if DEBUG` gating / no debugger dependency (release-safe).
- [ ] `dotnet build src/Uriel/Uriel.csproj -f net9.0 -c Release` passes locally.
- [ ] CHANGELOG.md updated under **[Unreleased]** (if behavior changed).
- [ ] Docs updated (if user-facing).
