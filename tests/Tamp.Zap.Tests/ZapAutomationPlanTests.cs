namespace Tamp.Zap.Tests;

public class ZapAutomationPlanTests
{
    private const string Target = "https://app.example.test";

    // ------------------------------------------------------------------
    // Shared shape
    // ------------------------------------------------------------------

    [Fact]
    public void Every_profile_emits_a_sarif_report_job()
    {
        foreach (var yaml in AllProfiles())
        {
            Assert.Contains("type: report", yaml, StringComparison.Ordinal);
            Assert.Contains($"template: {ZapAutomationPlan.SarifTemplate}", yaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_profile_anchors_the_context_to_the_target()
    {
        foreach (var yaml in AllProfiles())
        {
            Assert.Contains(Target, yaml, StringComparison.Ordinal);
            // includePaths keeps an active scan from wandering off-site.
            Assert.Contains("includePaths:", yaml, StringComparison.Ordinal);
            Assert.Contains($"\"{Target}.*\"", yaml, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------
    // Anonymous — must stay non-invasive
    // ------------------------------------------------------------------

    [Fact]
    public void Anonymous_profile_never_active_scans()
    {
        var yaml = ZapAutomationPlan.Anonymous(Target);

        // This is the profile that's safe to point at a shared environment.
        // If an activeScan job ever appears here, that guarantee is gone.
        Assert.DoesNotContain("activeScan", yaml, StringComparison.Ordinal);
        Assert.Contains("type: spider", yaml, StringComparison.Ordinal);
        Assert.Contains("passiveScan-wait", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymous_profile_carries_no_credentials()
    {
        var yaml = ZapAutomationPlan.Anonymous(Target);

        Assert.DoesNotContain("replacer", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Cookie", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymous_spider_budget_is_configurable()
    {
        Assert.Contains("maxDuration: 7", ZapAutomationPlan.Anonymous(Target, spiderMinutes: 7), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // API
    // ------------------------------------------------------------------

    [Fact]
    public void Api_profile_imports_the_spec_and_active_scans()
    {
        var yaml = ZapAutomationPlan.Api(Target, $"{Target}/openapi/v1.json");

        Assert.Contains("type: openapi", yaml, StringComparison.Ordinal);
        Assert.Contains($"{Target}/openapi/v1.json", yaml, StringComparison.Ordinal);
        Assert.Contains("type: activeScan", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_profile_can_import_graphql_instead()
    {
        var yaml = ZapAutomationPlan.Api(Target, $"{Target}/graphql", graphql: true);

        Assert.Contains("type: graphql", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("type: openapi", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_profile_emits_a_token_placeholder_not_a_token()
    {
        var yaml = ZapAutomationPlan.Api(Target, $"{Target}/openapi/v1.json", tokenEnvVar: "MY_TOKEN");

        // The whole point: the plan is a build artifact that may be committed
        // or archived, so it must reference the secret, never contain it.
        Assert.Contains("${MY_TOKEN}", yaml, StringComparison.Ordinal);
        Assert.Contains("Bearer ${MY_TOKEN}", yaml, StringComparison.Ordinal);
        Assert.Contains("matchString: Authorization", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_profile_defaults_to_the_conventional_token_var()
    {
        var yaml = ZapAutomationPlan.Api(Target, $"{Target}/openapi/v1.json");
        Assert.Contains($"${{{ZapAutomationPlan.DefaultTokenEnvVar}}}", yaml, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // SPA
    // ------------------------------------------------------------------

    [Fact]
    public void Spa_profile_runs_the_ajax_spider()
    {
        var yaml = ZapAutomationPlan.Spa(Target);

        // A client-routed front end is invisible to the classic spider, so
        // this job is what makes the profile meaningful at all.
        Assert.Contains("type: spiderAjax", yaml, StringComparison.Ordinal);
        Assert.Contains("type: activeScan", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Spa_profile_emits_a_cookie_placeholder_not_a_cookie()
    {
        var yaml = ZapAutomationPlan.Spa(Target, cookieName: "tamp.findings.auth", cookieEnvVar: "MY_COOKIE");

        Assert.Contains("tamp.findings.auth=${MY_COOKIE}", yaml, StringComparison.Ordinal);
        Assert.Contains("matchString: Cookie", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Spa_ajax_budget_is_configurable()
    {
        var yaml = ZapAutomationPlan.Spa(Target, ajaxMinutes: 11);
        Assert.Contains("maxDuration: 11", yaml, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Report naming + IO
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("zap-anon.sarif")]
    [InlineData("custom-name.sarif")]
    public void Report_file_name_is_honoured(string name)
    {
        Assert.Contains($"reportFile: \"{name}\"", ZapAutomationPlan.Anonymous(Target, name), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_creates_the_parent_directory_and_returns_the_path()
    {
        var dir = AbsolutePath.CreateTempDirectory("tamp-zap-plan");
        var path = dir / "nested" / "plan.yaml";

        var returned = ZapAutomationPlan.Write(path, ZapAutomationPlan.Anonymous(Target));

        Assert.Equal(path.Value, returned.Value);
        Assert.True(File.Exists(path.Value));
        Assert.Contains("type: spider", File.ReadAllText(path.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => ZapAutomationPlan.Anonymous(null!));
        Assert.Throws<ArgumentNullException>(() => ZapAutomationPlan.Api(Target, null!));
        Assert.Throws<ArgumentNullException>(() => ZapAutomationPlan.Spa(null!));
        Assert.Throws<ArgumentNullException>(() => ZapAutomationPlan.Write(null!, "x"));
    }

    [Fact]
    public void Quotes_in_values_are_escaped()
    {
        // Not untrusted input, but an unescaped quote would silently produce
        // a plan ZAP can't parse — a confusing failure a long way from here.
        var yaml = ZapAutomationPlan.Spa(Target, cookieName: "we\"ird");
        Assert.Contains("we\\\"ird", yaml, StringComparison.Ordinal);
    }

    private static IEnumerable<string> AllProfiles() =>
    [
        ZapAutomationPlan.Anonymous(Target),
        ZapAutomationPlan.Api(Target, $"{Target}/openapi/v1.json"),
        ZapAutomationPlan.Spa(Target),
    ];
}
