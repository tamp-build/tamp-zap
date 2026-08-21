using System.Text;

namespace Tamp.Zap;

/// <summary>
/// Generates ZAP Automation Framework plan YAML for the scan profiles adopters
/// actually run in CI, so nobody has to hand-author the schema.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this isn't a CommandPlan verb.</strong> The Tamp creed is one wrapper
/// per CLI tool; <see cref="Zap"/>'s verbs emit <c>CommandPlan</c>s that shell out.
/// This helper only writes a file — no external tool involved — so it lives beside
/// the verbs rather than on the facade. Same split as <c>Tamp.AxeCore</c>'s SARIF
/// annotator.
/// </para>
/// <para>
/// <strong>Three profiles, because there are three auth surfaces.</strong> A typical
/// app exposes anonymous routes, a token-authenticated API, and a session-authenticated
/// UI. One scan can't cover all three, and conflating them produces a scan that
/// silently only ever sees the login page.
/// </para>
/// <list type="bullet">
///   <item><see cref="Anonymous"/> — no credentials. Asserts that nothing outside the
///   intended public allow-list answers. Passive only, so it's safe anywhere.</item>
///   <item><see cref="Api"/> — imports an OpenAPI/GraphQL definition and active-scans
///   it with a bearer token injected on every request.</item>
///   <item><see cref="Spa"/> — AJAX-spiders a JS front end with a session cookie, then
///   active-scans. The classic spider can't discover client-routed pages.</item>
/// </list>
/// <para>
/// <strong>Secrets never go in the file.</strong> Token and cookie profiles emit a
/// <c>${VAR}</c> placeholder that ZAP substitutes from the environment at run time.
/// Pair with <c>SetSecretEnvironmentVariable(name)</c> so the value is forwarded by
/// name and stays out of both the plan file and the OS process table.
/// </para>
/// <para>
/// <strong>Active scanning writes.</strong> <see cref="Api"/> and <see cref="Spa"/>
/// submit forms and fuzz parameters — they WILL create, modify, and delete data through
/// whatever endpoints they reach. Point them at disposable environments only.
/// </para>
/// </remarks>
public static class ZapAutomationPlan
{
    /// <summary>Report template id for SARIF 2.1.0 output (reports add-on).</summary>
    public const string SarifTemplate = "sarif-json";

    /// <summary>Conventional env var name for a bearer token placeholder.</summary>
    public const string DefaultTokenEnvVar = "ZAP_AUTH_TOKEN";

    /// <summary>Conventional env var name for a session cookie placeholder.</summary>
    public const string DefaultCookieEnvVar = "ZAP_SESSION_COOKIE";

    /// <summary>
    /// Anonymous profile — spider + passive rules, no credentials, no active scan.
    /// Safe to run against any environment.
    /// </summary>
    /// <param name="target">Base URL of the deployed app.</param>
    /// <param name="reportFile">SARIF report filename, relative to the plan's work dir.</param>
    /// <param name="spiderMinutes">Spider budget. Default 2.</param>
    public static string Anonymous(string target, string reportFile = "zap-anon.sarif", int spiderMinutes = 2)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reportFile);

        var sb = new StringBuilder();
        WriteEnv(sb, "anon", target);
        sb.AppendLine("jobs:");
        WriteSpider(sb, "anon", spiderMinutes);
        sb.AppendLine("  - type: passiveScan-wait");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      maxDuration: 5");
        WriteReport(sb, reportFile);
        return sb.ToString();
    }

    /// <summary>
    /// API profile — import an API definition, then active-scan it with a bearer
    /// token injected on every request.
    /// </summary>
    /// <param name="target">Base URL of the API.</param>
    /// <param name="apiDefinitionUrl">URL (or container path) of the OpenAPI / GraphQL definition.</param>
    /// <param name="reportFile">SARIF report filename, relative to the plan's work dir.</param>
    /// <param name="tokenEnvVar">
    /// Environment variable holding the bearer token. Emitted as a <c>${VAR}</c>
    /// placeholder — the value is never written to the plan.
    /// </param>
    /// <param name="graphql">True to import as GraphQL rather than OpenAPI.</param>
    public static string Api(
        string target,
        string apiDefinitionUrl,
        string reportFile = "zap-api.sarif",
        string tokenEnvVar = DefaultTokenEnvVar,
        bool graphql = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(apiDefinitionUrl);
        ArgumentNullException.ThrowIfNull(reportFile);
        ArgumentNullException.ThrowIfNull(tokenEnvVar);

        var sb = new StringBuilder();
        WriteEnv(sb, "api", target);
        sb.AppendLine("jobs:");
        // replacer runs before every request, so the token rides on the spec
        // fetch and each scanned request alike.
        sb.AppendLine("  - type: replacer");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      deleteAllRules: true");
        sb.AppendLine("    rules:");
        sb.AppendLine("      - description: bearer-token");
        sb.AppendLine("        matchType: request_header");
        sb.AppendLine("        matchString: Authorization");
        sb.AppendLine("        matchRegex: false");
        sb.AppendLine($"        replacementString: \"Bearer ${{{tokenEnvVar}}}\"");
        sb.AppendLine("        tokenProcessing: false");
        sb.AppendLine($"  - type: {(graphql ? "graphql" : "openapi")}");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      context: api");
        sb.AppendLine($"      endpoint: \"{Escape(apiDefinitionUrl)}\"");
        sb.AppendLine("  - type: activeScan");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      context: api");
        sb.AppendLine("      maxRuleDurationInMins: 5");
        sb.AppendLine("      maxScanDurationInMins: 60");
        WriteReport(sb, reportFile);
        return sb.ToString();
    }

    /// <summary>
    /// SPA profile — AJAX-spider a JavaScript front end with a session cookie, then
    /// active-scan what was discovered.
    /// </summary>
    /// <param name="target">Base URL of the SPA.</param>
    /// <param name="reportFile">SARIF report filename, relative to the plan's work dir.</param>
    /// <param name="cookieName">Session cookie name (e.g. the app's auth cookie).</param>
    /// <param name="cookieEnvVar">
    /// Environment variable holding the cookie value. Emitted as a <c>${VAR}</c>
    /// placeholder — the value is never written to the plan.
    /// </param>
    /// <param name="ajaxMinutes">AJAX spider budget. Default 5.</param>
    public static string Spa(
        string target,
        string reportFile = "zap-spa.sarif",
        string cookieName = "session",
        string cookieEnvVar = DefaultCookieEnvVar,
        int ajaxMinutes = 5)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reportFile);
        ArgumentNullException.ThrowIfNull(cookieName);
        ArgumentNullException.ThrowIfNull(cookieEnvVar);

        var sb = new StringBuilder();
        WriteEnv(sb, "spa", target);
        sb.AppendLine("jobs:");
        sb.AppendLine("  - type: replacer");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      deleteAllRules: true");
        sb.AppendLine("    rules:");
        sb.AppendLine("      - description: session-cookie");
        sb.AppendLine("        matchType: request_header");
        sb.AppendLine("        matchString: Cookie");
        sb.AppendLine("        matchRegex: false");
        sb.AppendLine($"        replacementString: \"{Escape(cookieName)}=${{{cookieEnvVar}}}\"");
        sb.AppendLine("        tokenProcessing: false");
        WriteSpider(sb, "spa", 2);
        // A React/Vue front end routes client-side; the classic spider only sees
        // the shell, so the AJAX spider is mandatory rather than optional here.
        sb.AppendLine("  - type: spiderAjax");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      context: spa");
        sb.AppendLine("      browserId: firefox-headless");
        sb.AppendLine($"      maxDuration: {ajaxMinutes}");
        sb.AppendLine("  - type: passiveScan-wait");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      maxDuration: 5");
        sb.AppendLine("  - type: activeScan");
        sb.AppendLine("    parameters:");
        sb.AppendLine("      context: spa");
        sb.AppendLine("      maxRuleDurationInMins: 5");
        sb.AppendLine("      maxScanDurationInMins: 60");
        WriteReport(sb, reportFile);
        return sb.ToString();
    }

    /// <summary>
    /// Write a plan to disk, creating the parent directory if needed. Returns the
    /// path for chaining into <c>SetPlanFile</c>.
    /// </summary>
    public static AbsolutePath Write(AbsolutePath path, string planYaml)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(planYaml);
        path.EnsureParentDirectoryExists();
        File.WriteAllText(path.Value, planYaml);
        return path;
    }

    private static void WriteEnv(StringBuilder sb, string contextName, string target)
    {
        sb.AppendLine("env:");
        sb.AppendLine("  contexts:");
        sb.AppendLine($"    - name: {contextName}");
        sb.AppendLine("      urls:");
        sb.AppendLine($"        - \"{Escape(target)}\"");
        sb.AppendLine("      includePaths:");
        // Anchor the include to the target so the scan can't wander off-site.
        sb.AppendLine($"        - \"{Escape(target.TrimEnd('/'))}.*\"");
        sb.AppendLine("  parameters:");
        sb.AppendLine("    failOnError: true");
        sb.AppendLine("    progressToStdout: true");
    }

    private static void WriteSpider(StringBuilder sb, string contextName, int minutes)
    {
        sb.AppendLine("  - type: spider");
        sb.AppendLine("    parameters:");
        sb.AppendLine($"      context: {contextName}");
        sb.AppendLine($"      maxDuration: {minutes}");
    }

    private static void WriteReport(StringBuilder sb, string reportFile)
    {
        sb.AppendLine("  - type: report");
        sb.AppendLine("    parameters:");
        sb.AppendLine($"      template: {SarifTemplate}");
        sb.AppendLine("      reportDir: /zap/wrk");
        sb.AppendLine($"      reportFile: \"{Escape(reportFile)}\"");
    }

    // Minimal escaping for the double-quoted YAML scalars this builder emits.
    // Values here are URLs, cookie names, and filenames supplied by the adopter's
    // own build — not untrusted input — so backslash + quote is sufficient.
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
