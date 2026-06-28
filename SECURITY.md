# Security Policy

## Supported versions

Uriel.Profiler is pre-1.0. Security fixes land on the latest released `0.x`
version only. Please upgrade to the newest release before reporting.

## Reporting a vulnerability

**Do not open a public issue for security problems.**

Report privately via GitHub's [Security Advisories](https://github.com/simonspoon/uriel/security/advisories/new)
("Report a vulnerability"), or email **simon@sjspoon.com** with:

- a description and impact assessment,
- steps to reproduce (or a proof of concept),
- affected version(s).

You'll get an acknowledgement within **7 days**. Once a fix is ready we'll
coordinate a release and credit you in the advisory unless you'd prefer to
remain anonymous.

## Scope note

Uriel streams telemetry (memory/GC counters, exception and hang stack traces)
to the host's console/logcat. Stack traces can contain method and parameter
names from your app. Treat profiler output as you would any diagnostic log —
the wire format is **not** encrypted and is meant for local capture during
development and testing.
