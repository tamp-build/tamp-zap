using System.Globalization;

namespace Tamp.Zap;

/// <summary>How the ZAP CLI is reached.</summary>
public enum ZapRunMode
{
    /// <summary>
    /// Run the official ZAP image via <c>docker run</c>. Default — the image
    /// bundles a JRE, so the runner needs Docker but not Java.
    /// </summary>
    Docker = 0,

    /// <summary>
    /// Invoke a locally installed <c>zap.sh</c> / <c>zap.bat</c>. Requires a JRE
    /// on the runner. Set <see cref="ZapSettingsBase.ZapCommand"/> when the
    /// binary isn't on PATH.
    /// </summary>
    Local = 1,
}

/// <summary>
/// Shared base for ZAP CommandPlan settings. Owns run mode, the container
/// image, host-to-container path translation, and environment variables.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Path translation is the point.</strong> In <see cref="ZapRunMode.Docker"/>
/// mode ZAP sees a container filesystem, not yours. Every path an adopter supplies
/// is a HOST path; this base mounts <see cref="WorkDirectory"/> at
/// <see cref="ContainerWorkDirectory"/> and rewrites paths beneath it. Supplying a
/// path outside <see cref="WorkDirectory"/> throws with an explanation rather than
/// producing a plan that fails inside the container with a bare "file not found".
/// </para>
/// <para>
/// <strong>Secrets.</strong> Never bake a bearer token or session cookie into a plan
/// file. The ZAP Automation Framework substitutes <c>${VAR}</c> from the process
/// environment, so put a placeholder in the plan and pass the value through
/// <c>SetSecretEnvironmentVariable</c>. In Docker mode those names are
/// forwarded with <c>-e NAME</c> (value inherited from the runner's environment,
/// so it never lands in the OS process table or the plan file).
/// </para>
/// </remarks>
public abstract class ZapSettingsBase
{
    /// <summary>How to reach ZAP. Default <see cref="ZapRunMode.Docker"/>.</summary>
    public ZapRunMode RunMode { get; set; } = ZapRunMode.Docker;

    /// <summary>
    /// Container image for <see cref="ZapRunMode.Docker"/>. Default
    /// <c>ghcr.io/zaproxy/zaproxy:stable</c>. The Docker Hub mirror
    /// <c>zaproxy/zap-stable</c> is interchangeable. Note the legacy
    /// <c>owasp/zap2docker-*</c> names are retired.
    /// </summary>
    public string Image { get; set; } = DefaultImage;

    /// <summary>Default ZAP image — GHCR, stable channel.</summary>
    public const string DefaultImage = "ghcr.io/zaproxy/zaproxy:stable";

    /// <summary>
    /// Path to the local ZAP launcher for <see cref="ZapRunMode.Local"/>. Defaults
    /// to <c>zap.sh</c> resolved off PATH.
    /// </summary>
    public string? ZapCommand { get; set; }

    /// <summary>The <c>docker</c> executable. Defaults to <c>docker</c> off PATH.</summary>
    public string? DockerCommand { get; set; }

    /// <summary>
    /// HOST directory holding the plan file and receiving reports. Mounted at
    /// <see cref="ContainerWorkDirectory"/> in Docker mode. Required in Docker mode.
    /// </summary>
    public string? WorkDirectory { get; set; }

    /// <summary>Mount point for <see cref="WorkDirectory"/> inside the container. ZAP's conventional work dir.</summary>
    public string ContainerWorkDirectory { get; set; } = "/zap/wrk";

    /// <summary>
    /// Docker <c>--network</c> value. On Linux, <c>host</c> lets ZAP reach a target
    /// on the runner's own localhost. On Windows/macOS use the default bridge and
    /// address the host as <c>host.docker.internal</c> instead — <c>--network host</c>
    /// does not behave the same way there.
    /// </summary>
    public string? NetworkMode { get; set; }

    /// <summary>
    /// Docker <c>--user</c> value. The image runs as the <c>zap</c> user; on Linux CI
    /// where the mounted work dir is owned by the runner, passing the runner's
    /// uid:gid avoids reports failing to write.
    /// </summary>
    public string? ContainerUser { get; set; }

    /// <summary>Escape hatch for <c>docker run</c> flags this wrapper doesn't model.</summary>
    public List<string> ExtraDockerArguments { get; } = new();

    /// <summary>Escape hatch for ZAP CLI arguments this wrapper doesn't model.</summary>
    public List<string> ExtraZapArguments { get; } = new();

    /// <summary>Per-invocation environment variables, forwarded to the container with a value.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>
    /// Names of environment variables forwarded WITHOUT a value (<c>docker run -e NAME</c>),
    /// so the value is inherited from the runner's environment rather than written
    /// into the command line. Use for tokens and cookies referenced as <c>${NAME}</c>
    /// in an automation plan.
    /// </summary>
    public List<string> PassthroughEnvironmentVariables { get; } = new();

    /// <summary>Working directory for the spawned process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-verb ZAP arguments, in container-path terms where relevant.</summary>
    protected abstract IEnumerable<string> BuildZapArguments();

    /// <summary>
    /// The executable invoked inside the container. <c>zap.sh</c> for the daemon /
    /// automation path; the packaged scans override this with their script name.
    /// </summary>
    protected virtual string EntryPoint => "zap.sh";

    /// <summary>Per-verb validation. Call <c>base.Validate()</c> from overrides.</summary>
    protected virtual void Validate()
    {
        if (RunMode == ZapRunMode.Docker && string.IsNullOrWhiteSpace(WorkDirectory))
        {
            throw new InvalidOperationException(
                "WorkDirectory is required in Docker run mode (set via SetWorkDirectory) — it is the host " +
                "directory mounted into the container for the plan file and report output.");
        }
    }

    /// <summary>
    /// Translate a HOST path to the path ZAP will see. Identity in
    /// <see cref="ZapRunMode.Local"/> mode; rewritten under
    /// <see cref="ContainerWorkDirectory"/> in Docker mode.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The path is not beneath <see cref="WorkDirectory"/>, so the container could
    /// not see it. Failing here beats a "file not found" from inside the container.
    /// </exception>
    protected string ToContainerPath(string hostPath, string parameterName)
    {
        if (RunMode == ZapRunMode.Local) return hostPath;

        var root = Path.GetFullPath(WorkDirectory!);
        var full = Path.GetFullPath(hostPath);

        // Ordinal-ignore-case is right on Windows and harmless on Linux CI,
        // where the host paths in play are produced by the build itself.
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{parameterName} ('{hostPath}') is not beneath WorkDirectory ('{WorkDirectory}'), so the ZAP " +
                $"container cannot see it. Move the file under WorkDirectory, or widen WorkDirectory to a " +
                $"common parent.");
        }

        var relative = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var containerRelative = relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
        return containerRelative.Length == 0
            ? ContainerWorkDirectory
            : $"{ContainerWorkDirectory.TrimEnd('/')}/{containerRelative}";
    }

    /// <summary>Build the <see cref="CommandPlan"/> for this verb.</summary>
    public CommandPlan ToCommandPlan()
    {
        Validate();

        var zapArgs = BuildZapArguments().ToList();
        zapArgs.AddRange(ExtraZapArguments);

        return RunMode == ZapRunMode.Docker
            ? BuildDockerPlan(zapArgs)
            : BuildLocalPlan(zapArgs);
    }

    private CommandPlan BuildDockerPlan(List<string> zapArgs)
    {
        var args = new List<string> { "run", "--rm" };

        // :rw is explicit rather than implied — ZAP writes reports back out
        // through this mount, and a read-only bind is a confusing failure.
        args.Add("-v");
        args.Add($"{Path.GetFullPath(WorkDirectory!)}:{ContainerWorkDirectory}:rw");

        if (!string.IsNullOrWhiteSpace(NetworkMode)) { args.Add("--network"); args.Add(NetworkMode!); }
        if (!string.IsNullOrWhiteSpace(ContainerUser)) { args.Add("--user"); args.Add(ContainerUser!); }

        foreach (var (name, value) in EnvironmentVariables) { args.Add("-e"); args.Add($"{name}={value}"); }
        // Value-less -e inherits from the runner's environment: keeps secrets
        // out of the process table and out of the plan file.
        foreach (var name in PassthroughEnvironmentVariables) { args.Add("-e"); args.Add(name); }

        args.AddRange(ExtraDockerArguments);
        args.Add(Image);
        args.Add(EntryPoint);
        args.AddRange(zapArgs);

        return new CommandPlan
        {
            Executable = DockerCommand ?? "docker",
            Arguments = args,
            // Docker mode passes env through -e flags; the child process here is
            // the docker client, which only needs the passthrough values present.
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory,
        };
    }

    private CommandPlan BuildLocalPlan(List<string> zapArgs) => new()
    {
        Executable = ZapCommand ?? EntryPoint,
        Arguments = zapArgs,
        Environment = new Dictionary<string, string>(EnvironmentVariables),
        WorkingDirectory = WorkingDirectory,
    };

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Fluent setters shared by every ZAP verb.</summary>
public static class ZapSettingsBaseExtensions
{
    public static T SetRunMode<T>(this T s, ZapRunMode mode) where T : ZapSettingsBase { s.RunMode = mode; return s; }
    public static T SetImage<T>(this T s, string image) where T : ZapSettingsBase { s.Image = image; return s; }
    public static T SetZapCommand<T>(this T s, string? command) where T : ZapSettingsBase { s.ZapCommand = command; return s; }
    public static T SetDockerCommand<T>(this T s, string? command) where T : ZapSettingsBase { s.DockerCommand = command; return s; }
    public static T SetWorkDirectory<T>(this T s, string path) where T : ZapSettingsBase { s.WorkDirectory = path; return s; }
    public static T SetContainerWorkDirectory<T>(this T s, string path) where T : ZapSettingsBase { s.ContainerWorkDirectory = path; return s; }
    public static T SetNetworkMode<T>(this T s, string? mode) where T : ZapSettingsBase { s.NetworkMode = mode; return s; }
    public static T SetContainerUser<T>(this T s, string? user) where T : ZapSettingsBase { s.ContainerUser = user; return s; }
    public static T AddDockerArgument<T>(this T s, string arg) where T : ZapSettingsBase { s.ExtraDockerArguments.Add(arg); return s; }
    public static T AddZapArgument<T>(this T s, string arg) where T : ZapSettingsBase { s.ExtraZapArguments.Add(arg); return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : ZapSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : ZapSettingsBase { s.WorkingDirectory = cwd; return s; }

    /// <summary>
    /// Forward an environment variable by NAME only, inheriting its value from the
    /// runner. The value never appears on the command line or in the plan file —
    /// use this for bearer tokens and session cookies referenced as <c>${NAME}</c>
    /// in an automation plan.
    /// </summary>
    public static T SetSecretEnvironmentVariable<T>(this T s, string name) where T : ZapSettingsBase
    {
        s.PassthroughEnvironmentVariables.Add(name);
        return s;
    }
}

/// <summary>
/// Settings for an Automation Framework run — <c>zap.sh -cmd -autorun &lt;plan.yaml&gt;</c>.
/// The primary verb: one declarative plan expresses contexts, authentication,
/// OpenAPI import, spidering, active scanning, and reporting.
/// </summary>
public sealed class ZapAutomationSettings : ZapSettingsBase
{
    /// <summary>HOST path to the automation plan YAML. Required, and must sit under <see cref="ZapSettingsBase.WorkDirectory"/> in Docker mode.</summary>
    public string? PlanFile { get; set; }

    /// <summary>
    /// Run ZAP with <c>-cmd</c> (headless, exit when the plan completes). Default true.
    /// Set false only when attaching to a long-lived daemon.
    /// </summary>
    public bool Headless { get; set; } = true;

    /// <summary>Additional ZAP config overrides (<c>-config key=value</c>).</summary>
    public Dictionary<string, string> ConfigOverrides { get; } = new();

    protected override void Validate()
    {
        base.Validate();
        if (string.IsNullOrWhiteSpace(PlanFile))
            throw new InvalidOperationException("PlanFile is required (set via SetPlanFile) — the Automation Framework plan YAML to run.");
    }

    protected override IEnumerable<string> BuildZapArguments()
    {
        if (Headless) yield return "-cmd";
        foreach (var (k, v) in ConfigOverrides) { yield return "-config"; yield return $"{k}={v}"; }
        yield return "-autorun";
        yield return ToContainerPath(PlanFile!, nameof(PlanFile));
    }
}

/// <summary>Fluent setters for <see cref="ZapAutomationSettings"/>.</summary>
public static class ZapAutomationSettingsExtensions
{
    public static ZapAutomationSettings SetPlanFile(this ZapAutomationSettings s, string path) { s.PlanFile = path; return s; }
    public static ZapAutomationSettings SetHeadless(this ZapAutomationSettings s, bool v = true) { s.Headless = v; return s; }
    public static ZapAutomationSettings SetConfig(this ZapAutomationSettings s, string key, string value) { s.ConfigOverrides[key] = value; return s; }
}

/// <summary>Which packaged scan script to run.</summary>
public enum ZapPackagedScanKind
{
    /// <summary>Spider + passive rules only. Fast, non-invasive — safe against shared environments.</summary>
    Baseline = 0,

    /// <summary>Spider, optional AJAX spider, then a full ACTIVE scan. Injects payloads; never point it at anything you can't afford to have modified.</summary>
    Full = 1,

    /// <summary>Active scan driven from an OpenAPI, GraphQL, or SOAP definition.</summary>
    Api = 2,
}

/// <summary>
/// Settings for ZAP's packaged scan scripts (<c>zap-baseline.py</c>,
/// <c>zap-full-scan.py</c>, <c>zap-api-scan.py</c>).
/// </summary>
/// <remarks>
/// Convenient for quick adoption, but they hardcode report handling and can't
/// express authentication contexts. For anything beyond an anonymous smoke scan,
/// prefer <c>Zap.Automation</c>.
/// <para>
/// <strong>Exit codes.</strong> <c>0</c> = clean, <c>2</c> = warnings, <c>1</c> = at
/// least one FAIL rule tripped, <c>3</c> = the scan itself failed. Treat <c>0</c>,
/// <c>1</c>, and <c>2</c> as "the scan ran" and fail the target only on <c>3</c>.
/// </para>
/// </remarks>
public sealed class ZapPackagedScanSettings : ZapSettingsBase
{
    /// <summary>Which script to run. Default <see cref="ZapPackagedScanKind.Baseline"/> — the safe one.</summary>
    public ZapPackagedScanKind Kind { get; set; } = ZapPackagedScanKind.Baseline;

    /// <summary>Target URL (<c>-t</c>). Required. For <see cref="ZapPackagedScanKind.Api"/> this is the spec URL or file.</summary>
    public string? Target { get; set; }

    /// <summary>API definition format for <see cref="ZapPackagedScanKind.Api"/> (<c>-f</c>): <c>openapi</c>, <c>graphql</c>, or <c>soap</c>.</summary>
    public string? ApiFormat { get; set; }

    /// <summary>HOST path for the JSON report (<c>-J</c>).</summary>
    public string? JsonReportFile { get; set; }

    /// <summary>HOST path for the HTML report (<c>-r</c>).</summary>
    public string? HtmlReportFile { get; set; }

    /// <summary>Spider for this many minutes (<c>-m</c>).</summary>
    public int? SpiderMinutes { get; set; }

    /// <summary>Also run the AJAX spider (<c>-j</c>) — needed for SPAs, whose routes the classic spider can't discover.</summary>
    public bool AjaxSpider { get; set; }

    /// <summary>Include alerts at INFO level and above (<c>-I</c> inverts the default "fail on warn").</summary>
    public bool IgnoreWarnings { get; set; }

    /// <summary>Show debug output (<c>-d</c>).</summary>
    public bool Debug { get; set; }

    /// <summary>Minutes to wait for passive scanning to finish (<c>-T</c>).</summary>
    public int? PassiveScanTimeoutMinutes { get; set; }

    protected override void Validate()
    {
        base.Validate();
        if (string.IsNullOrWhiteSpace(Target))
            throw new InvalidOperationException("Target is required (set via SetTarget) — the URL to scan, or the API definition for an Api scan.");
        if (Kind == ZapPackagedScanKind.Api && string.IsNullOrWhiteSpace(ApiFormat))
            throw new InvalidOperationException("ApiFormat is required for an Api scan (set via SetApiFormat) — one of 'openapi', 'graphql', 'soap'.");
    }

    /// <summary>The packaged script name for <see cref="Kind"/>.</summary>
    public string ScriptName => Kind switch
    {
        ZapPackagedScanKind.Baseline => "zap-baseline.py",
        ZapPackagedScanKind.Full => "zap-full-scan.py",
        ZapPackagedScanKind.Api => "zap-api-scan.py",
        _ => throw new InvalidOperationException($"Unhandled scan kind '{Kind}'."),
    };

    protected override IEnumerable<string> BuildZapArguments()
    {
        yield return "-t"; yield return Target!;
        if (Kind == ZapPackagedScanKind.Api) { yield return "-f"; yield return ApiFormat!; }
        if (!string.IsNullOrEmpty(JsonReportFile)) { yield return "-J"; yield return ToContainerPath(JsonReportFile!, nameof(JsonReportFile)); }
        if (!string.IsNullOrEmpty(HtmlReportFile)) { yield return "-r"; yield return ToContainerPath(HtmlReportFile!, nameof(HtmlReportFile)); }
        if (SpiderMinutes is int m) { yield return "-m"; yield return Invariant(m); }
        if (AjaxSpider) yield return "-j";
        if (IgnoreWarnings) yield return "-I";
        if (Debug) yield return "-d";
        if (PassiveScanTimeoutMinutes is int t) { yield return "-T"; yield return Invariant(t); }
    }

    /// <summary>Packaged scans replace <c>zap.sh</c> with their own script.</summary>
    protected override string EntryPoint => ScriptName;
}

/// <summary>Fluent setters for <see cref="ZapPackagedScanSettings"/>.</summary>
public static class ZapPackagedScanSettingsExtensions
{
    public static ZapPackagedScanSettings SetKind(this ZapPackagedScanSettings s, ZapPackagedScanKind kind) { s.Kind = kind; return s; }
    public static ZapPackagedScanSettings SetTarget(this ZapPackagedScanSettings s, string target) { s.Target = target; return s; }
    public static ZapPackagedScanSettings SetApiFormat(this ZapPackagedScanSettings s, string format) { s.ApiFormat = format; return s; }
    public static ZapPackagedScanSettings SetJsonReportFile(this ZapPackagedScanSettings s, string path) { s.JsonReportFile = path; return s; }
    public static ZapPackagedScanSettings SetHtmlReportFile(this ZapPackagedScanSettings s, string path) { s.HtmlReportFile = path; return s; }
    public static ZapPackagedScanSettings SetSpiderMinutes(this ZapPackagedScanSettings s, int? minutes) { s.SpiderMinutes = minutes; return s; }
    public static ZapPackagedScanSettings SetAjaxSpider(this ZapPackagedScanSettings s, bool v = true) { s.AjaxSpider = v; return s; }
    public static ZapPackagedScanSettings SetIgnoreWarnings(this ZapPackagedScanSettings s, bool v = true) { s.IgnoreWarnings = v; return s; }
    public static ZapPackagedScanSettings SetDebug(this ZapPackagedScanSettings s, bool v = true) { s.Debug = v; return s; }
    public static ZapPackagedScanSettings SetPassiveScanTimeoutMinutes(this ZapPackagedScanSettings s, int? minutes) { s.PassiveScanTimeoutMinutes = minutes; return s; }
}
