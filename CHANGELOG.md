# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is [SemVer](https://semver.org/).

## [0.1.0] - 2026-08-21

Initial release. TAM-278.

### Added

- `Zap.Automation` — run a ZAP Automation Framework plan (`zap.sh -cmd -autorun`). The primary verb: one declarative plan covers contexts, authentication, spec import, spidering, active scanning, and reporting.
- `Zap.PackagedScan` — the packaged `zap-baseline.py` / `zap-full-scan.py` / `zap-api-scan.py` scripts, for quick adoption.
- `ZapAutomationPlan` — plan generator for the three auth surfaces a real app exposes: `Anonymous` (no credentials, passive only), `Api` (bearer token + OpenAPI/GraphQL import + active scan), `Spa` (session cookie + AJAX spider + active scan). Emits `${VAR}` placeholders so secrets stay out of the plan file.
- Docker run mode (default, `ghcr.io/zaproxy/zaproxy:stable`) with host-to-container path translation, and `Local` mode for a native `zap.sh` install.
- Secret forwarding by name (`docker run -e NAME`), keeping tokens and cookies out of both the plan file and the OS process table.

### Notes

- Requires Docker on the runner in the default mode; the image bundles a JRE, so no Java install is needed.
- Paths outside `WorkDirectory` throw at plan-build time rather than failing inside the container.
