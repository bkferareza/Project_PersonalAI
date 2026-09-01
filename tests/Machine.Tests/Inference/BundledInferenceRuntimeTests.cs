using System.Diagnostics;
using System.Security.Cryptography;
using Machine.Core;
using Machine.Inference;

namespace Machine.Tests;

public sealed class BundledInferenceRuntimeTests
{
    [Fact]
    public void ArgumentsAreFixedPrivateAndAuthenticated()
    {
        var configuration = CreateConfiguration("runtime", "model.gguf");
        var startInfo = new ProcessStartInfo();

        LlamaServerArguments.AddTo(
            startInfo,
            configuration,
            49152,
            "launch-secret");

        var arguments = startInfo.ArgumentList.ToArray();
        Assert.Equal("launch-secret", startInfo.Environment["LLAMA_API_KEY"]);
        Assert.DoesNotContain("launch-secret", arguments);
        AssertArgument(arguments, "--host", "127.0.0.1");
        AssertArgument(arguments, "--port", "49152");
        AssertArgument(arguments, "--model", configuration.ModelPath);
        AssertArgument(arguments, "--alias", configuration.ModelAlias);
        AssertArgument(arguments, "--parallel", "1");
        AssertArgument(arguments, "--ctx-size", "8192");
        AssertArgument(arguments, "--n-gpu-layers", "99");
        Assert.Contains("--no-ui", arguments);
        Assert.Contains("--no-slots", arguments);
        Assert.Contains("--no-mmproj", arguments);
        Assert.DoesNotContain("0.0.0.0", arguments);
        Assert.DoesNotContain("11434", arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void ArgumentsRejectInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LlamaServerArguments.AddTo(
                new ProcessStartInfo(),
                CreateConfiguration("runtime", "model.gguf"),
                port,
                "launch-secret"));
    }

    [Fact]
    public async Task ArtifactValidatorAcceptsExactPinnedFiles()
    {
        using var directory = new TemporaryDirectory();
        var runtimePath = Path.Combine(directory.Path, "runtime.bin");
        var modelPath = Path.Combine(directory.Path, "model.gguf");
        await File.WriteAllBytesAsync(runtimePath, "runtime"u8.ToArray());
        await File.WriteAllBytesAsync(
            modelPath,
            [.. "GGUF"u8.ToArray(), .. "model"u8.ToArray()]);
        var configuration = CreateConfiguration(
            directory.Path,
            modelPath,
            new InferenceArtifactFile(
                "runtime.bin",
                new FileInfo(runtimePath).Length,
                await HashAsync(runtimePath)),
            await HashAsync(modelPath));

        await InferenceArtifactValidator.ValidateAsync(configuration);
    }

    [Fact]
    public async Task ArtifactValidatorRejectsWrongHash()
    {
        using var directory = new TemporaryDirectory();
        var runtimePath = Path.Combine(directory.Path, "runtime.bin");
        var modelPath = Path.Combine(directory.Path, "model.gguf");
        await File.WriteAllBytesAsync(runtimePath, "runtime"u8.ToArray());
        await File.WriteAllBytesAsync(
            modelPath,
            [.. "GGUF"u8.ToArray(), .. "model"u8.ToArray()]);
        var configuration = CreateConfiguration(
            directory.Path,
            modelPath,
            new InferenceArtifactFile(
                "runtime.bin",
                new FileInfo(runtimePath).Length,
                new string('0', 64)),
            await HashAsync(modelPath));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            InferenceArtifactValidator.ValidateAsync(configuration));

        Assert.Contains("SHA-256", exception.Message);
    }

    [Fact]
    public async Task ArtifactValidatorRejectsMissingFileWithoutDownloading()
    {
        using var directory = new TemporaryDirectory();
        var configuration = CreateConfiguration(
            directory.Path,
            Path.Combine(directory.Path, "model.gguf"),
            new InferenceArtifactFile(
                "missing-runtime.bin",
                1,
                new string('0', 64)),
            new string('0', 64));

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            InferenceArtifactValidator.ValidateAsync(configuration));

        Assert.EndsWith(
            "missing-runtime.bin",
            exception.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void DiagnosticsRemainBounded()
    {
        var diagnostics = new BoundedInferenceDiagnostics(
            capacity: 2,
            maximumLineLength: 4);

        diagnostics.Add("one", "12345");
        diagnostics.Add("two", "abcde");
        diagnostics.Add("three", "vwxyz");

        Assert.Equal(["two: abcd", "three: vwxy"], diagnostics.Snapshot());
    }

    [Fact]
    public async Task MissingArtifactsProduceUnavailableStatusWithoutStarting()
    {
        using var directory = new TemporaryDirectory();
        var configuration = CreateConfiguration(
            directory.Path,
            Path.Combine(directory.Path, "missing-model.gguf"),
            new InferenceArtifactFile(
                "missing-runtime.exe",
                1,
                new string('0', 64)));
        await using var runtime = new BundledLlamaInferenceRuntime(
            configuration);

        var status = await runtime.GetStatusAsync();

        Assert.False(status.IsRuntimeAvailable);
        Assert.Equal(LocalInferenceModelState.Faulted, status.ModelState);
        Assert.Equal(
            LocalInferenceFailureKind.RuntimeUnavailable,
            status.Failure?.Kind);
        Assert.Null(status.ProcessId);
        Assert.Equal(configuration.ModelName, status.ConfiguredModelName);
        Assert.Equal(configuration.Quantization,
            status.ConfiguredQuantization);
        Assert.Equal(configuration.ModelSizeBytes,
            status.ConfiguredModelSizeBytes);
        Assert.Equal(configuration.RuntimeCommit, status.RuntimeSha);
        Assert.Equal(configuration.GpuLayerCount, status.GpuLayerCount);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task RuntimeRejectsUnpinnedModelBeforeProcessStart()
    {
        using var directory = new TemporaryDirectory();
        await using var runtime = new BundledLlamaInferenceRuntime(
            CreateConfiguration(
                directory.Path,
                Path.Combine(directory.Path, "missing-model.gguf")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runtime.GenerateAsync(new(
                "user-supplied-model.gguf",
                [new LocalInferenceMessage(
                    LocalInferenceMessageRole.User,
                    "Hello")],
                4096,
                64,
                0.1d)));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task JobObjectTerminatesExactOwnedChildOnDispose()
    {
        var systemDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.System);
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(systemDirectory, "ping.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("127.0.0.1");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
            "The bounded Job Object test child did not start.");

        try
        {
            using var job = WindowsKillOnCloseJob.CreateAndAssign(process);
            job.Dispose();
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));

            await process.WaitForExitAsync(timeout.Token);

            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
            }
        }
    }

    private static BundledInferenceConfiguration CreateConfiguration(
        string runtimeDirectory,
        string modelPath,
        InferenceArtifactFile? runtimeFile = null,
        string? modelSha = null) =>
        new(
            "llama.cpp",
            "b10724",
            "2d8d612e4c68d3801e556a1b4a028f55ec33ecbb",
            "CUDA 12.4",
            runtimeDirectory,
            Path.Combine(runtimeDirectory, "llama-server.exe"),
            runtimeFile is null ? [] : [runtimeFile],
            "Qwen3.5-4B",
            "qwen3.5:4b",
            modelPath,
            File.Exists(modelPath) ? new FileInfo(modelPath).Length : 0,
            modelSha ?? new string('0', 64),
            "Q4_K_M",
            8192,
            99,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(10));

    private static void AssertArgument(
        string[] arguments,
        string name,
        string expectedValue)
    {
        var index = Array.IndexOf(arguments, name);
        Assert.InRange(index, 0, arguments.Length - 2);
        Assert.Equal(expectedValue, arguments[index + 1]);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Matasuri-Inference-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
