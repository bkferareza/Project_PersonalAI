using System.Diagnostics;

namespace Machine.Inference;

public static class LlamaServerArguments
{
    public static void AddTo(
        ProcessStartInfo startInfo,
        BundledInferenceConfiguration configuration,
        int port,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        startInfo.Environment["LLAMA_API_KEY"] = apiKey;

        Add(startInfo,
            "--model", configuration.ModelPath,
            "--alias", configuration.ModelAlias,
            "--host", "127.0.0.1",
            "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--no-ui",
            "--no-slots",
            "--parallel", "1",
            "--ctx-size", configuration.ContextLength.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "--n-gpu-layers", configuration.GpuLayerCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "--flash-attn", "on",
            "--jinja",
            "--no-mmproj",
            "--threads-http", "2",
            "--timeout", "120",
            "--verbosity", "4",
            "--cache-prompt");
    }

    private static void Add(
        ProcessStartInfo startInfo,
        params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }
}
