using System.Diagnostics;

namespace Machine.Tests;

public sealed class DevelopmentStateSafetyScriptTests
{
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
