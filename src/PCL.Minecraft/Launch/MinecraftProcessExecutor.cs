using System.Diagnostics;

namespace PCL3.Minecraft.Launch;

public sealed record MinecraftProcessStartOptions(
    bool RedirectStandardOutput = false,
    bool RedirectStandardError = false,
    bool CreateNoWindow = false);

public static class MinecraftProcessStartInfoBuilder
{
    public static ProcessStartInfo Build(
        LaunchPlan plan,
        MinecraftProcessStartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.WorkingDirectory);
        options ??= new MinecraftProcessStartOptions();

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = Path.GetFullPath(plan.WorkingDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = options.RedirectStandardOutput,
            RedirectStandardError = options.RedirectStandardError,
            CreateNoWindow = options.CreateNoWindow
        };

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in plan.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }
}

public interface IMinecraftRunningProcess : IAsyncDisposable
{
    int Id { get; }

    bool HasExited { get; }

    TextReader? StandardOutput { get; }

    TextReader? StandardError { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);

    Task TerminateAsync(CancellationToken cancellationToken = default);
}

public interface IMinecraftProcessExecutor
{
    IMinecraftRunningProcess Start(
        LaunchPlan plan,
        MinecraftProcessStartOptions? options = null);
}

public sealed class SystemMinecraftProcessExecutor : IMinecraftProcessExecutor
{
    public IMinecraftRunningProcess Start(
        LaunchPlan plan,
        MinecraftProcessStartOptions? options = null)
    {
        options ??= new MinecraftProcessStartOptions();
        var process = new Process
        {
            StartInfo = MinecraftProcessStartInfoBuilder.Build(plan, options)
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Failed to start Minecraft process '{plan.Executable}'.");
            }

            return new SystemMinecraftRunningProcess(process, options);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private sealed class SystemMinecraftRunningProcess(
        Process process,
        MinecraftProcessStartOptions options) : IMinecraftRunningProcess
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public TextReader? StandardOutput =>
            options.RedirectStandardOutput ? process.StandardOutput : null;

        public TextReader? StandardError =>
            options.RedirectStandardError ? process.StandardError : null;

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }

        public async Task TerminateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
