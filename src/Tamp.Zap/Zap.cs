namespace Tamp.Zap;

/// <summary>
/// Tamp wrappers for the ZAP DAST scanner. <c>Automation</c> runs an
/// Automation Framework plan (the primary path); <c>PackagedScan</c> runs
/// the packaged baseline / full / api scripts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the Automation Framework is the primary verb.</strong> The packaged
/// scripts (<c>zap-baseline.py</c> and friends) are the better-known entry point,
/// but they hardcode report handling and have no way to express an authentication
/// context. A real app has more than one auth surface — anonymous routes, a
/// token-authenticated API, a session-authenticated UI — and a single packaged scan
/// can only ever see the first. The Automation Framework composes contexts, auth,
/// spec import, spidering, active scanning, and reporting as jobs in one declarative
/// plan, which is what those profiles actually require. Use
/// <see cref="ZapAutomationPlan"/> to generate the plans.
/// </para>
/// <para>
/// <strong>SARIF is native.</strong> ZAP's reports add-on ships a <c>sarif-json</c>
/// template, so no converter step is needed (unlike <c>Tamp.AxeCore</c>). The SARIF
/// carries the full DAST payload — <c>webRequest</c> / <c>webResponse</c>, the attack
/// payload under <c>properties</c>, evidence snippets, and CWE taxonomy mappings.
/// Verify the add-on is current in your image if the report job reports an unknown
/// template.
/// </para>
/// <para>
/// <strong>Docker by default.</strong> The official image bundles a JRE, so the
/// runner needs Docker but not Java. Switch to <see cref="ZapRunMode.Local"/> for a
/// native <c>zap.sh</c> install.
/// </para>
/// <code>
/// Target SecurityScanZap => _ => _.Executes(() =>
/// {
///     var workDir = SecurityArtifactsDir;
///     var plan    = ZapAutomationPlan.Write(
///         workDir / "zap-anon.yaml",
///         ZapAutomationPlan.Anonymous(TargetUrl, "zap-anon.sarif"));
///
///     var scan = Zap.Automation(s => s
///         .SetWorkDirectory(workDir)
///         .SetPlanFile(plan)
///         .SetSecretEnvironmentVariable(ZapAutomationPlan.DefaultTokenEnvVar));
///     var rc = ProcessRunner.Execute(scan, Console.Out, Console.Error);
///     if (rc != 0) throw new Exception($"zap exited with {rc}");
/// });
/// </code>
/// <para>
/// <strong>Exit-code semantics.</strong> An Automation Framework run exits <c>0</c>
/// when the plan completes; a non-zero exit means the plan itself failed (bad YAML,
/// unreachable target, unknown report template) rather than "findings were reported".
/// Findings live in the report. Packaged scans differ — see
/// <see cref="ZapPackagedScanSettings"/>.
/// </para>
/// <para>
/// <strong>Active scanning is destructive.</strong> Active scan rules submit forms
/// and fuzz parameters, so they create, modify, and delete data through every
/// endpoint they reach. Only the baseline / anonymous profiles are safe against an
/// environment whose data you care about.
/// </para>
/// </remarks>
public static class Zap
{
    /// <summary><c>zap.sh -cmd -autorun &lt;plan.yaml&gt;</c> — run an Automation Framework plan.</summary>
    public static CommandPlan Automation(Action<ZapAutomationSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var settings = new ZapAutomationSettings();
        configure(settings);
        return settings.ToCommandPlan();
    }

    /// <summary>Object-init overload. Identical CommandPlan to the fluent path.</summary>
    public static CommandPlan Automation(ZapAutomationSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan();
    }

    /// <summary><c>zap-baseline.py</c> / <c>zap-full-scan.py</c> / <c>zap-api-scan.py</c> — packaged scan scripts.</summary>
    public static CommandPlan PackagedScan(Action<ZapPackagedScanSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var settings = new ZapPackagedScanSettings();
        configure(settings);
        return settings.ToCommandPlan();
    }

    /// <summary>Object-init overload. Identical CommandPlan to the fluent path.</summary>
    public static CommandPlan PackagedScan(ZapPackagedScanSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan();
    }
}
