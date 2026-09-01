using System.Security.Cryptography;

namespace Machine.Inference;

public static class InferenceArtifactValidator
{
    public static async Task ValidateAsync(
        BundledInferenceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var file in configuration.RuntimeFiles)
        {
            await ValidateFileAsync(
                Path.Combine(configuration.RuntimeDirectory, file.Name),
                file.SizeBytes,
                file.Sha256,
                cancellationToken).ConfigureAwait(false);
        }

        await ValidateFileAsync(
            configuration.ModelPath,
            configuration.ModelSizeBytes,
            configuration.ModelSha256,
            cancellationToken).ConfigureAwait(false);
        await using var model = new FileStream(
            configuration.ModelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4,
            useAsync: true);
        var magic = new byte[4];
        if (await model.ReadAsync(magic, cancellationToken)
                .ConfigureAwait(false) != magic.Length ||
            !magic.SequenceEqual("GGUF"u8.ToArray()))
        {
            throw new InvalidDataException(
                "The app-owned inference model is not a GGUF file.");
        }
    }

    public static async Task ValidateFileAsync(
        string path,
        long expectedSizeBytes,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "A pinned local inference artifact is missing.",
                path);
        }

        if (file.Length != expectedSizeBytes)
        {
            throw new InvalidDataException(
                $"Inference artifact size does not match its manifest: {file.Name}");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(hash);
        if (!string.Equals(
                actual,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Inference artifact SHA-256 does not match its manifest: {file.Name}");
        }
    }
}
