using System.Diagnostics;
using System.Net;
using Machine.Core;

namespace Machine.Ollama;

public interface IOllamaRuntimeHealthProbe
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}

public interface IOllamaRuntimeProcess : IDisposable
{
    bool HasExited { get; }
    void Stop();
}

public interface IOllamaRuntimeProcessLauncher
{
    IOllamaRuntimeProcess Start(string executablePath);
}

public sealed class OllamaRuntimeBootstrapper : IOllamaRuntimeBootstrapper
{
    private static readonly TimeSpan DefaultReadinessTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadinessPollInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly IOllamaRuntimeHealthProbe _healthProbe;
    private readonly IOllamaRuntimeProcessLauncher _processLauncher;
    private readonly Func<string?> _executableResolver;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _readinessTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IOllamaRuntimeProcess? _ownedProcess;
    private bool _disposed;

    public OllamaRuntimeBootstrapper(HttpClient httpClient)
        : this(new OllamaRuntimeHealthProbe(httpClient),
            new OllamaRuntimeProcessLauncher(),
            OllamaExecutableResolver.Find,
            Task.Delay,
            DefaultReadinessTimeout)
    {
    }

    public OllamaRuntimeBootstrapper(
        IOllamaRuntimeHealthProbe healthProbe,
        IOllamaRuntimeProcessLauncher processLauncher,
        Func<string?> executableResolver,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan? readinessTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(healthProbe);
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(executableResolver);
        ArgumentNullException.ThrowIfNull(delay);
        _healthProbe = healthProbe;
        _processLauncher = processLauncher;
        _executableResolver = executableResolver;
        _delay = delay;
        _readinessTimeout = readinessTimeout ?? DefaultReadinessTimeout;
    }

    public async Task<OllamaRuntimeBootstrapResult> EnsureAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _healthProbe.IsHealthyAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                return new OllamaRuntimeBootstrapResult(true, false, true);
            }

            if (_ownedProcess is not null && !_ownedProcess.HasExited)
            {
                return await PollForReadinessAsync(
                    executableWasFound: true, cancellationToken)
                    .ConfigureAwait(false);
            }

            var executablePath = _executableResolver();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new OllamaRuntimeBootstrapResult(false, false, false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _ownedProcess?.Dispose();
            _ownedProcess = _processLauncher.Start(executablePath);
            return await PollForReadinessAsync(
                executableWasFound: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _ownedProcess?.Stop();
        _ownedProcess?.Dispose();
        _ownedProcess = null;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<OllamaRuntimeBootstrapResult> PollForReadinessAsync(
        bool executableWasFound,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _readinessTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _healthProbe.IsHealthyAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                return new OllamaRuntimeBootstrapResult(
                    true, true, executableWasFound);
            }

            await _delay(ReadinessPollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        return new OllamaRuntimeBootstrapResult(
            false, true, executableWasFound);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public sealed class OllamaRuntimeHealthProbe : IOllamaRuntimeHealthProbe
{
    private readonly HttpClient _httpClient;

    public OllamaRuntimeHealthProbe(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                "api/version", cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

public sealed class OllamaRuntimeProcessLauncher : IOllamaRuntimeProcessLauncher
{
    public IOllamaRuntimeProcess Start(string executablePath) =>
        new OllamaRuntimeProcess(Process.Start(new ProcessStartInfo(
            executablePath,
            "serve")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException(
            "Ollama could not be started."));
}

public static class OllamaExecutableResolver
{
    public static string? Find()
    {
        var candidates = new List<string>();
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            candidates.Add(Path.Combine(localApplicationData,
                "Programs", "Ollama", "ollama.exe"));
        }

        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Ollama", "ollama.exe"));
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory.Trim(), "ollama.exe")));
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}

internal sealed class OllamaRuntimeProcess(Process process)
    : IOllamaRuntimeProcess
{
    public bool HasExited => process.HasExited;
    public void Stop()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
    public void Dispose() => process.Dispose();
}
