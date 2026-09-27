# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is [SemVer](https://semver.org/).

## [Unreleased]

### Added

- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)


## [0.2.0] - 2026-08-22

### Added

- `ZapAutomationPlan.Active(...)` — spider + AJAX spider + full active scan, **no credentials**.

### Notes

- The three original profiles covered anonymous-passive, token-authenticated-active and session-authenticated-active, and left out the most common open-source DAST scenario entirely: an unauthenticated active scan. That is the surface a public-facing app is actually judged on, and there was no way to express it.
- `Active` **writes**. Active scan rules submit forms and fuzz every parameter they find, so they create, modify and delete data through whatever endpoints answer. Disposable targets only. A test asserts the profile emits no `replacer` rule — a stray credential would silently change which surface is being measured.

## [0.1.2] - 2026-08-21

### Added

- `excludePaths` on all three plan profiles, and `ZapAutomationPlan.DefaultAssetExcludes` covering fingerprinted bundles (Vite `/assets/`, webpack `/static/`, hashed `js|css|map`).

### Notes

- Excluding fingerprinted assets matters for more than noise. A bundler rewrites `/assets/index-A1b2C3.js` on every build, so a finding against one is a **new** finding every deploy — it can never be triaged, aged, or trended, it only churns. The underlying issue (a missing header, a caching directive) is almost always reported against the document root too, where it is stable. Regexes are tested against filenames observed in a real scan of a Vite-built SPA, and against the real routes that must stay in scope.

## [0.1.1] - 2026-08-21

### Added

- `ZapAutomationPlan.SarifReportFileOnDisk(reportFile)` — resolves the filename ZAP actually writes.

### Notes

- ZAP's report job appends the template's extension when the configured name lacks it: `reportFile: zap.sarif` produces **`zap.sarif.json`** on disk. Verified against ZAP 2.17.0 running the `sarif-json` template. A caller that goes looking for the name it configured finds nothing — and if it doesn't check, silently ingests an empty scan. Resolve through the helper rather than assuming.

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
