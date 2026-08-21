# Tamp.Zap

Tamp wrappers for the **ZAP** DAST scanner — dynamic application security testing against a deployed target, emitting SARIF straight into the Tamp security pipeline. A Tamp satellite.

| | |
|---|---|
| **Wrapped tool** | [ZAP](https://www.zaproxy.org/) (Zed Attack Proxy) |
| **Tool license** | Apache-2.0 |
| **This package** | MIT |
| **Platform** | Any OS with Docker (default), or any OS with a JRE 17+ and a local `zap.sh` |
| **Target frameworks** | net8.0 · net9.0 · net10.0 |
| **SARIF** | Native — no converter step |

> **Naming.** ZAP left OWASP in September 2023 (moving to the Linux Foundation's Software Security Project); in September 2024 the core maintainers joined Checkmarx and it was rebranded *ZAP by Checkmarx*. It remains open source under Apache-2.0. If a contract or policy document says "OWASP ZAP", that's this tool — worth reconciling explicitly in compliance paperwork.

## Why the Automation Framework is the primary verb

ZAP's packaged scripts (`zap-baseline.py`, `zap-full-scan.py`, `zap-api-scan.py`) are the better-known entry point, but they hardcode report handling and can't express an authentication context.

A real application has **more than one auth surface** — anonymous routes, a token-authenticated API, a session-authenticated UI. A single packaged scan only ever sees the first, which is how teams end up with a green DAST scan that never got past the login page.

The Automation Framework composes contexts, authentication, spec import, spidering, active scanning, and reporting as jobs in one declarative plan. `Tamp.Zap` drives that, and ships a plan generator so you don't hand-author the schema.

## Install

```bash
dotnet add package Tamp.Zap
```

Requires Docker on the runner (default mode). No JRE needed — the official image bundles one.

## Quick start

```csharp
using Tamp.Zap;

Target SecurityScanZap => _ => _
    .Description("ZAP DAST scan against the deployed app; SARIF for the security pipeline.")
    .Executes(() =>
    {
        var workDir = SecurityArtifactsDir;
        var plan = ZapAutomationPlan.Write(
            workDir / "zap-anon.yaml",
            ZapAutomationPlan.Anonymous(TargetUrl, "zap-anon.sarif"));

        var scan = Zap.Automation(s => s
            .SetWorkDirectory(workDir)
            .SetPlanFile(plan));

        var rc = ProcessRunner.Execute(scan, Console.Out, Console.Error);
        if (rc != 0) throw new Exception($"zap exited with {rc}");
    });
```

## The three profiles

`ZapAutomationPlan` generates a plan per auth surface:

| Profile | Credentials | Jobs | Safe against shared environments |
|---|---|---|---|
| `Anonymous(target)` | none | spider → passive | **Yes** — no active scan |
| `Api(target, specUrl)` | bearer token | replacer → openapi/graphql → activeScan | No |
| `Spa(target)` | session cookie | replacer → spider → spiderAjax → activeScan | No |

`Anonymous` is the one to start with. Its job isn't finding injection — it's asserting that **nothing outside your intended public allow-list answers without credentials**. Cheap, fast, safe anywhere, and it catches broken-access-control regressions that unit tests never will.

`Spa` runs the AJAX spider because a client-routed front end is invisible to the classic spider — it only ever sees the shell.

> **Active scanning writes.** `Api` and `Spa` submit forms and fuzz parameters. They *will* create, modify, and delete data through every endpoint they reach. Point them at disposable environments only.

## Secrets never go in the plan file

Plan files are build artifacts — they get archived, sometimes committed. So tokens and cookies are emitted as `${VAR}` placeholders that ZAP substitutes from the environment at run time:

```csharp
var plan = ZapAutomationPlan.Write(
    workDir / "zap-api.yaml",
    ZapAutomationPlan.Api(ApiUrl, $"{ApiUrl}/openapi/v1.json"));

var scan = Zap.Automation(s => s
    .SetWorkDirectory(workDir)
    .SetPlanFile(plan)
    // Forwarded as `docker run -e ZAP_AUTH_TOKEN` — name only. The value is
    // inherited from the runner, so it stays out of the plan file AND out of
    // the OS process table.
    .SetSecretEnvironmentVariable(ZapAutomationPlan.DefaultTokenEnvVar));
```

## Host-to-container path translation

In Docker mode ZAP sees a container filesystem, not yours. Every path you pass is a **host** path; `WorkDirectory` is mounted at `/zap/wrk` and paths beneath it are rewritten automatically.

Passing a path *outside* `WorkDirectory` throws immediately with an explanation, rather than producing a plan that dies inside the container with a bare "file not found".

```csharp
.SetWorkDirectory(@"C:\repo\artifacts\security")   // mounted at /zap/wrk
.SetPlanFile(@"C:\repo\artifacts\security\plans\anon.yaml")
// ZAP receives: /zap/wrk/plans/anon.yaml
```

## Reaching a target on the runner's own localhost

| Host OS | Approach |
|---|---|
| Linux | `.SetNetworkMode("host")`, then target `http://localhost:5080` |
| Windows / macOS | leave the default bridge, target `http://host.docker.internal:5080` |

`--network host` does not behave the same way on Docker Desktop, so the bridge + `host.docker.internal` is the portable choice there.

## Packaged scans

Still available for quick adoption:

```csharp
var scan = Zap.PackagedScan(s => s
    .SetWorkDirectory(workDir)
    .SetKind(ZapPackagedScanKind.Baseline)
    .SetTarget("https://app.example.com")
    .SetJsonReportFile(workDir / "zap.json"));
```

**Exit codes differ from the Automation Framework.** Packaged scans return `0` clean, `1` when a FAIL rule tripped, `2` on warnings, `3` when the scan itself failed — so treat `0`/`1`/`2` as "the scan ran" and fail the target only on `3`. An Automation Framework run returns `0` when the plan completes; non-zero means the plan failed, not that findings exist. Findings live in the report either way.

## Local ZAP instead of Docker

```csharp
.SetRunMode(ZapRunMode.Local)
.SetZapCommand("/opt/zap/zap.sh")   // omit to resolve zap.sh off PATH
```

Requires a JRE 17+ on the runner. No path translation is applied — ZAP sees the real filesystem.

## Images

Default is `ghcr.io/zaproxy/zaproxy:stable`. The Docker Hub mirror `zaproxy/zap-stable` is interchangeable, and `weekly` / `nightly` / `bare` tags exist on both. The legacy `owasp/zap2docker-*` names are retired.

The `bare` image omits the packaged scan scripts — fine for `Zap.Automation`, not for `Zap.PackagedScan`.

## SARIF output

ZAP's reports add-on ships a `sarif-json` template, so no conversion step is needed. The SARIF carries the full DAST payload: `webRequest` / `webResponse`, the attack payload under `properties`, evidence snippets, and CWE taxonomy mappings.

If the report job reports an unknown template, the image's reports add-on predates SARIF support — update the add-on or move to a current `:stable`.

## License

MIT. See [LICENSE](LICENSE). ZAP itself is Apache-2.0 and is not redistributed by this package.
