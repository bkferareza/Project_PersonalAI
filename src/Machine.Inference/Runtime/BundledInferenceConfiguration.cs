using System.Text.Json;

namespace Machine.Inference;

public sealed record InferenceArtifactFile(
    string Name,
    long SizeBytes,
    string Sha256);

public sealed record BundledInferenceConfiguration(
    string RuntimeName,
    string RuntimeVersion,
    string RuntimeCommit,
    string Backend,
    string RuntimeDirectory,
    string ExecutablePath,
    IReadOnlyList<InferenceArtifactFile> RuntimeFiles,
    string ModelName,
    string ModelAlias,
    string ModelPath,
    long ModelSizeBytes,
    string ModelSha256,
    string Quantization,
    int ContextLength,
    int GpuLayerCount,
    TimeSpan StartupTimeout,
    TimeSpan GenerationTimeout,
    TimeSpan StopTimeout,
    TimeSpan ResidencyDuration)
{
    public const int DefaultContextLength = 8192;
    public const int DefaultGpuLayerCount = 99;

    public static BundledInferenceConfiguration LoadDefault()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var manifestDirectory = Path.Combine(
            baseDirectory,
            "Inference",
            "Manifests");
        var runtimeManifestPath = Path.Combine(
            manifestDirectory,
            "runtime-manifest.json");
        var modelManifestPath = Path.Combine(
            manifestDirectory,
            "model-manifest.json");
        using var runtimeDocument = JsonDocument.Parse(
            File.ReadAllText(runtimeManifestPath));
        using var modelDocument = JsonDocument.Parse(
            File.ReadAllText(modelManifestPath));
        var runtime = runtimeDocument.RootElement;
        var model = modelDocument.RootElement;
        var runtimeDirectory = Path.Combine(
            baseDirectory,
            "Inference",
            "Runtime");
        var executableName = RequiredString(
            runtime,
            "expectedExecutable");
        var modelFileName = RequiredString(model, "fileName");
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var modelPath = Path.Combine(
            localApplicationData,
            "Matasuri",
            "Inference",
            "Models",
            modelFileName);
        var runtimeFiles = runtime.GetProperty("files")
            .EnumerateArray()
            .Select(file => new InferenceArtifactFile(
                RequiredString(file, "name"),
                file.GetProperty("sizeBytes").GetInt64(),
                RequiredString(file, "sha256")))
            .ToArray();
        return new(
            RequiredString(runtime, "runtimeName"),
            RequiredString(runtime, "releaseTag"),
            RequiredString(runtime, "sourceCommit"),
            RequiredString(runtime, "backend"),
            runtimeDirectory,
            Path.Combine(runtimeDirectory, executableName),
            runtimeFiles,
            RequiredString(model, "modelName"),
            RequiredString(model, "runtimeAlias"),
            modelPath,
            model.GetProperty("sizeBytes").GetInt64(),
            RequiredString(model, "sha256"),
            RequiredString(model, "quantization"),
            DefaultContextLength,
            DefaultGpuLayerCount,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(10));
    }

    private static string RequiredString(
        JsonElement element,
        string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException(
                $"Inference manifest property '{propertyName}' is missing.")
            : value;
    }
}
