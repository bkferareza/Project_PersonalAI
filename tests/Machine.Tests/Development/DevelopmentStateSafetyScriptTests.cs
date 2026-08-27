using System.Diagnostics;

namespace Machine.Tests;

public sealed class DevelopmentStateSafetyScriptTests
{
    [Fact]
    public void StartupDevelopmentFixtureIsFixedBoundedAndNonExecuting()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "scripts",
            "development",
            "Set-MatasuriStartupDevelopmentFixture.ps1");
        var script = File.ReadAllText(path);

        Assert.Contains(
            "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
            script);
        Assert.Contains("MTSR-DEV Startup Fixture", script);
        Assert.Contains("__matasuri_development_fixture_never_matches__",
            script);
        Assert.Contains("[switch]$Remove", script);
        Assert.Contains("DoNotExpandEnvironmentNames", script);
        Assert.Contains("cleanup refused to delete it", script);
        Assert.DoesNotContain("Invoke-Expression", script);
        Assert.DoesNotContain("Start-Process", script);
        Assert.DoesNotContain("Remove-Item", script);
        Assert.DoesNotContain("Stop-Process", script);
    }

    [Fact]
    public void PackageManifestUnvirtualizesOnlyTheFixedUserRunKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Machine.App",
            "Package.appxmanifest"));

        Assert.Contains(
            "xmlns:virtualization=\"http://schemas.microsoft.com/appx/manifest/virtualization/windows10\"",
            manifest);
        Assert.Contains(
            "<rescap:Capability Name=\"unvirtualizedResources\" />",
            manifest);
        Assert.Contains(
            "<virtualization:ExcludedKey>" +
            "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\" +
            "CurrentVersion\\Run</virtualization:ExcludedKey>",
            manifest);
        Assert.Equal(
            1,
            manifest.Split("<virtualization:ExcludedKey>",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(
            "<desktop6:RegistryWriteVirtualization>",
            manifest);
    }

    [Fact]
    public async Task DevelopmentStateGuardPassesIsolatedSafetyScenarios()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testScript = Path.Combine(
            repositoryRoot,
            "scripts",
            "development",
            "tests",
            "DevelopmentStateSafety.Tests.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(testScript);
        startInfo.ArgumentList.Add("-RepositoryRoot");
        startInfo.ArgumentList.Add(repositoryRoot);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"Development safety script failed.\n" +
            $"stdout:\n{await standardOutput}\n" +
            $"stderr:\n{await standardError}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Machine.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test output.");
    }
}
