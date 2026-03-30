using System.Diagnostics;

namespace DotnetTestRunnerCli.Utilities;

public static class ProcessRunner
{
    public static async Task<(IReadOnlyList<string> Lines, int ExitCode)> RunAndCaptureAsync(
        string executable,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var psi = BuildPsi(executable, arguments, workingDirectory);
        using var process = new Process { StartInfo = psi };
        var lines = new List<string>();

        process.Start();

        // Waiting for both streams to reach EOF is the reliable signal that the
        // process has finished — WaitForExitAsync can return before the pipe is
        // fully flushed, causing reads to hang or miss trailing output.
        var stdoutTask = DrainAsync(process.StandardOutput, lines, cancellationToken);
        var stderrTask = DrainAsync(process.StandardError, null, cancellationToken);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillSafe(process);
            throw;
        }

        return (lines, process.ExitCode);
    }

    public static async Task<int> RunAndStreamAsync(
        string executable,
        string arguments,
        Func<string, Task> onLineReceived,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var psi = BuildPsi(executable, arguments, workingDirectory);
        using var process = new Process { StartInfo = psi };

        process.Start();

        // Route both stdout and stderr through the callback — some test runners
        // emit result lines on stderr. Reading both also prevents buffer deadlocks.
        var stdoutTask = StreamAsync(process.StandardOutput, onLineReceived, cancellationToken);
        var stderrTask = StreamAsync(process.StandardError, onLineReceived, cancellationToken);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillSafe(process);
            throw;
        }

        return process.ExitCode;
    }

    private static ProcessStartInfo BuildPsi(string executable, string arguments, string? workingDirectory) =>
        new(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

    private static async Task DrainAsync(StreamReader reader, List<string>? sink, CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
            sink?.Add(line);
    }

    private static async Task StreamAsync(StreamReader reader, Func<string, Task> callback, CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
            await callback(line);
    }

    private static void KillSafe(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }
}
