namespace Tamp.Zap.Tests;

public class ZapSettingsTests
{
    private static AbsolutePath TempWork() => AbsolutePath.CreateTempDirectory("tamp-zap-tests");

    // ------------------------------------------------------------------
    // Docker assembly
    // ------------------------------------------------------------------

    [Fact]
    public void Automation_in_docker_mode_mounts_the_work_dir_and_runs_the_plan()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value));

        Assert.Equal("docker", plan.Executable);
        var args = plan.Arguments;

        Assert.Equal("run", args[0]);
        Assert.Equal("--rm", args[1]);
        Assert.Contains("-v", args);
        Assert.Contains($"{Path.GetFullPath(work.Value)}:/zap/wrk:rw", args);
        Assert.Contains(ZapSettingsBase.DefaultImage, args);
        Assert.Contains("zap.sh", args);
        Assert.Contains("-cmd", args);
        Assert.Contains("-autorun", args);
        // The plan path must be rewritten to what the container sees.
        Assert.Contains("/zap/wrk/plan.yaml", args);

        // Nothing on the ZAP side of the image may carry a host path — the
        // -v mount is the one place a Windows drive path legitimately appears.
        var zapSideArgs = args.Skip(args.ToList().IndexOf(ZapSettingsBase.DefaultImage) + 1);
        Assert.DoesNotContain(zapSideArgs, a => a.Contains(":\\", StringComparison.Ordinal));
    }

    [Fact]
    public void Local_mode_emits_zap_directly_with_host_paths()
    {
        var work = TempWork();
        var planPath = (work / "plan.yaml").Value;
        var plan = Zap.Automation(s => s
            .SetRunMode(ZapRunMode.Local)
            .SetPlanFile(planPath));

        Assert.Equal("zap.sh", plan.Executable);
        Assert.DoesNotContain("run", plan.Arguments);
        // No translation in local mode — ZAP sees the real filesystem.
        Assert.Contains(planPath, plan.Arguments);
    }

    [Fact]
    public void Local_mode_does_not_require_a_work_directory()
    {
        var ex = Record.Exception(() => Zap.Automation(s => s
            .SetRunMode(ZapRunMode.Local)
            .SetPlanFile("/tmp/plan.yaml")));

        Assert.Null(ex);
    }

    [Fact]
    public void Docker_mode_requires_a_work_directory()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Zap.Automation(s => s.SetPlanFile("/tmp/plan.yaml")));

        Assert.Contains("WorkDirectory is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_requires_a_plan_file()
    {
        var work = TempWork();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Zap.Automation(s => s.SetWorkDirectory(work)));

        Assert.Contains("PlanFile is required", ex.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Path translation — the thing that silently breaks people
    // ------------------------------------------------------------------

    [Fact]
    public void A_path_outside_the_work_dir_fails_loudly_rather_than_inside_the_container()
    {
        var work = TempWork();
        var outside = AbsolutePath.CreateTempDirectory("tamp-zap-outside") / "plan.yaml";

        var ex = Assert.Throws<InvalidOperationException>(() => Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile(outside.Value)));

        Assert.Contains("not beneath WorkDirectory", ex.Message, StringComparison.Ordinal);
        // The message has to name the fix, not just the failure.
        Assert.Contains("WorkDirectory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_paths_translate_with_forward_slashes()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plans" / "anon.yaml").Value));

        Assert.Contains("/zap/wrk/plans/anon.yaml", plan.Arguments);
    }

    [Fact]
    public void Container_work_directory_is_configurable()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetContainerWorkDirectory("/mnt/scan")
            .SetPlanFile((work / "plan.yaml").Value));

        Assert.Contains($"{Path.GetFullPath(work.Value)}:/mnt/scan:rw", plan.Arguments);
        Assert.Contains("/mnt/scan/plan.yaml", plan.Arguments);
    }

    // ------------------------------------------------------------------
    // Secrets
    // ------------------------------------------------------------------

    [Fact]
    public void Secret_env_vars_are_forwarded_by_name_only()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value)
            .SetSecretEnvironmentVariable("ZAP_AUTH_TOKEN"));

        // -e NAME (no value) inherits from the runner. The token must never
        // appear on the command line.
        var idx = plan.Arguments.ToList().IndexOf("ZAP_AUTH_TOKEN");
        Assert.True(idx > 0, "expected the bare env var name in the docker args");
        Assert.Equal("-e", plan.Arguments[idx - 1]);
        Assert.DoesNotContain(plan.Arguments, a => a.StartsWith("ZAP_AUTH_TOKEN=", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_secret_env_vars_are_forwarded_with_their_value()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value)
            .SetEnvironmentVariable("ZAP_TARGET", "https://example.test"));

        Assert.Contains("ZAP_TARGET=https://example.test", plan.Arguments);
    }

    // ------------------------------------------------------------------
    // Docker knobs
    // ------------------------------------------------------------------

    [Fact]
    public void Network_mode_and_container_user_are_emitted_when_set()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value)
            .SetNetworkMode("host")
            .SetContainerUser("1000:1000"));

        var args = plan.Arguments.ToList();
        Assert.Equal("host", args[args.IndexOf("--network") + 1]);
        Assert.Equal("1000:1000", args[args.IndexOf("--user") + 1]);
    }

    [Fact]
    public void Docker_knobs_are_omitted_when_unset()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value));

        Assert.DoesNotContain("--network", plan.Arguments);
        Assert.DoesNotContain("--user", plan.Arguments);
    }

    [Fact]
    public void Config_overrides_are_emitted_before_autorun()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value)
            .SetConfig("connection.timeoutInSecs", "120"));

        var args = plan.Arguments.ToList();
        var configIdx = args.IndexOf("-config");
        Assert.True(configIdx >= 0);
        Assert.Equal("connection.timeoutInSecs=120", args[configIdx + 1]);
        Assert.True(configIdx < args.IndexOf("-autorun"));
    }

    // ------------------------------------------------------------------
    // Packaged scans
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(ZapPackagedScanKind.Baseline, "zap-baseline.py")]
    [InlineData(ZapPackagedScanKind.Full, "zap-full-scan.py")]
    public void Packaged_scan_uses_the_right_script_as_the_entry_point(ZapPackagedScanKind kind, string script)
    {
        var work = TempWork();
        var plan = Zap.PackagedScan(s => s
            .SetWorkDirectory(work)
            .SetKind(kind)
            .SetTarget("https://example.test"));

        // The script replaces zap.sh as the container entry point.
        Assert.Contains(script, plan.Arguments);
        Assert.DoesNotContain("zap.sh", plan.Arguments);
        var args = plan.Arguments.ToList();
        Assert.True(args.IndexOf(script) > args.IndexOf(ZapSettingsBase.DefaultImage));
    }

    [Fact]
    public void Api_scan_requires_a_format()
    {
        var work = TempWork();
        var ex = Assert.Throws<InvalidOperationException>(() => Zap.PackagedScan(s => s
            .SetWorkDirectory(work)
            .SetKind(ZapPackagedScanKind.Api)
            .SetTarget("https://example.test/openapi/v1.json")));

        Assert.Contains("ApiFormat is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_scan_emits_the_format_flag()
    {
        var work = TempWork();
        var plan = Zap.PackagedScan(s => s
            .SetWorkDirectory(work)
            .SetKind(ZapPackagedScanKind.Api)
            .SetTarget("https://example.test/openapi/v1.json")
            .SetApiFormat("openapi"));

        var args = plan.Arguments.ToList();
        Assert.Equal("openapi", args[args.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Packaged_scan_requires_a_target()
    {
        var work = TempWork();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Zap.PackagedScan(s => s.SetWorkDirectory(work)));

        Assert.Contains("Target is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Packaged_scan_translates_report_paths()
    {
        var work = TempWork();
        var plan = Zap.PackagedScan(s => s
            .SetWorkDirectory(work)
            .SetTarget("https://example.test")
            .SetJsonReportFile((work / "out.json").Value)
            .SetAjaxSpider()
            .SetSpiderMinutes(3));

        var args = plan.Arguments.ToList();
        Assert.Equal("/zap/wrk/out.json", args[args.IndexOf("-J") + 1]);
        Assert.Equal("3", args[args.IndexOf("-m") + 1]);
        Assert.Contains("-j", args);
    }

    // ------------------------------------------------------------------
    // Escape hatches + overloads
    // ------------------------------------------------------------------

    [Fact]
    public void Extra_arguments_land_on_the_right_side_of_the_image()
    {
        var work = TempWork();
        var plan = Zap.Automation(s => s
            .SetWorkDirectory(work)
            .SetPlanFile((work / "plan.yaml").Value)
            .AddDockerArgument("--memory=4g")
            .AddZapArgument("-silent"));

        var args = plan.Arguments.ToList();
        var imageIdx = args.IndexOf(ZapSettingsBase.DefaultImage);
        Assert.True(args.IndexOf("--memory=4g") < imageIdx, "docker args belong before the image");
        Assert.True(args.IndexOf("-silent") > imageIdx, "zap args belong after the image");
    }

    [Fact]
    public void Object_init_overload_matches_the_fluent_path()
    {
        var work = TempWork();
        var planPath = (work / "plan.yaml").Value;

        var fluent = Zap.Automation(s => s.SetWorkDirectory(work).SetPlanFile(planPath));
        var settings = new ZapAutomationSettings { WorkDirectory = work, PlanFile = planPath };
        var direct = Zap.Automation(settings);

        Assert.Equal(fluent.Executable, direct.Executable);
        Assert.Equal(fluent.Arguments, direct.Arguments);
    }

    [Fact]
    public void Null_configure_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Zap.Automation((Action<ZapAutomationSettings>)null!));
        Assert.Throws<ArgumentNullException>(() => Zap.PackagedScan((Action<ZapPackagedScanSettings>)null!));
        Assert.Throws<ArgumentNullException>(() => Zap.Automation((ZapAutomationSettings)null!));
        Assert.Throws<ArgumentNullException>(() => Zap.PackagedScan((ZapPackagedScanSettings)null!));
    }
}
