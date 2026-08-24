using System.Globalization;
using System.Text.Json;

namespace Machine.Core;

internal enum MachinePersistenceValidationResult
{
    Accepted,
    Rejected,
    Incompatible
}

internal enum MachineSafeJsonLoadStatus
{
    NotFound,
    Loaded,
    Rejected,
    Incompatible,
    Unavailable
}

internal sealed record MachineSafeJsonLoadResult<T>(
    T? Value,
    MachineSafeJsonLoadStatus Status)
    where T : class;

internal sealed class SafeJsonFile<T> where T : class
{
    internal const int MaximumRejectedCopyCount = 3;

    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<T, MachinePersistenceValidationResult> _validate;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _writesBlocked;

    internal SafeJsonFile(
        string filePath,
        JsonSerializerOptions jsonOptions,
        Func<T, MachinePersistenceValidationResult> validate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(validate);
        _filePath = filePath;
        _jsonOptions = jsonOptions;
        _validate = validate;
    }

    internal async Task<MachineSafeJsonLoadResult<T>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new(null, MachineSafeJsonLoadStatus.NotFound);
            }

            try
            {
                await using var stream = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                var value = await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (value is null)
                {
                    return await PreserveRejectedAsync(
                        MachineSafeJsonLoadStatus.Rejected,
                        cancellationToken).ConfigureAwait(false);
                }

                var validation = _validate(value);
                if (validation == MachinePersistenceValidationResult.Accepted)
                {
                    return new(value, MachineSafeJsonLoadStatus.Loaded);
                }

                return await PreserveRejectedAsync(
                    validation == MachinePersistenceValidationResult.Incompatible
                        ? MachineSafeJsonLoadStatus.Incompatible
                        : MachineSafeJsonLoadStatus.Rejected,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException or NotSupportedException)
            {
                return await PreserveRejectedAsync(
                    MachineSafeJsonLoadStatus.Rejected,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                _writesBlocked = true;
                return new(null, MachineSafeJsonLoadStatus.Unavailable);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SaveAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_writesBlocked)
            {
                throw new InvalidOperationException(
                    "Persistence writes are blocked because the existing " +
                    "state could not be safely accepted.");
            }

            if (_validate(value) !=
                MachinePersistenceValidationResult.Accepted)
            {
                throw new ArgumentException(
                    "The persistence state is not safe to write.",
                    nameof(value));
            }

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        value,
                        _jsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MachineSafeJsonLoadResult<T>> PreserveRejectedAsync(
        MachineSafeJsonLoadStatus rejectionStatus,
        CancellationToken cancellationToken)
    {
        _writesBlocked = true;
        try
        {
            var sourceBytes = await File.ReadAllBytesAsync(
                _filePath,
                cancellationToken).ConfigureAwait(false);
            var timestamp = DateTimeOffset.UtcNow.ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture);
            var rejectedPath = _filePath + $".rejected-{timestamp}-" +
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var pendingPath = rejectedPath + ".pending";
            try
            {
                await File.WriteAllBytesAsync(
                    pendingPath,
                    sourceBytes,
                    cancellationToken).ConfigureAwait(false);
                var copiedBytes = await File.ReadAllBytesAsync(
                    pendingPath,
                    cancellationToken).ConfigureAwait(false);
                if (!sourceBytes.AsSpan().SequenceEqual(copiedBytes))
                {
                    return new(null, MachineSafeJsonLoadStatus.Unavailable);
                }

                File.Move(pendingPath, rejectedPath);
                PruneRejectedCopies(rejectedPath);
                return new(null, rejectionStatus);
            }
            finally
            {
                TryDelete(pendingPath);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(null, MachineSafeJsonLoadStatus.Unavailable);
        }
    }

    private void PruneRejectedCopies(string newestPath)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var fileName = Path.GetFileName(_filePath);
        var rejected = Directory.GetFiles(
                directory,
                fileName + ".rejected-*")
            .Where(path => !path.EndsWith(
                ".pending",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => string.Equals(
                path,
                newestPath,
                StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .Skip(MaximumRejectedCopyCount)
            .ToArray();
        foreach (var path in rejected)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
