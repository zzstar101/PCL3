using PCL3.Minecraft.Artifacts;

namespace PCL3.Minecraft.Launch;

public sealed record MinecraftLaunchStartResult(
    MinecraftPreparationResult Preparation,
    LaunchPlan? LaunchPlan,
    IMinecraftRunningProcess? Process) : IAsyncDisposable
{
    public bool Started => Process is not null;

    public ValueTask DisposeAsync() =>
        Process is null ? ValueTask.CompletedTask : Process.DisposeAsync();
}

public sealed record MinecraftLaunchRunResult(
    MinecraftPreparationResult Preparation,
    LaunchPlan? LaunchPlan,
    int? ExitCode)
{
    public bool Started => LaunchPlan is not null;
}

public sealed class MinecraftLaunchPipeline
{
    private readonly MinecraftPreparationService _preparationService;
    private readonly IMinecraftProcessExecutor _processExecutor;

    public MinecraftLaunchPipeline(
        MinecraftPreparationService preparationService,
        IMinecraftProcessExecutor processExecutor)
    {
        ArgumentNullException.ThrowIfNull(preparationService);
        ArgumentNullException.ThrowIfNull(processExecutor);
        _preparationService = preparationService;
        _processExecutor = processExecutor;
    }

    public async Task<MinecraftLaunchStartResult> PrepareAndStartAsync(
        MinecraftLaunchContext context,
        MinecraftProcessStartOptions? processOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var preparation = await _preparationService.PrepareAsync(
            context.VersionChain,
            context.RuntimePlan,
            cancellationToken).ConfigureAwait(false);

        if (!preparation.IsSuccess)
        {
            return new MinecraftLaunchStartResult(preparation, null, null);
        }

        Directory.CreateDirectory(Path.GetFullPath(context.GameDirectory));
        var launchPlan = MinecraftLaunchPlanBuilder.Build(context);
        var process = _processExecutor.Start(launchPlan, processOptions);

        return new MinecraftLaunchStartResult(preparation, launchPlan, process);
    }

    public async Task<MinecraftLaunchRunResult> RunToExitAsync(
        MinecraftLaunchContext context,
        MinecraftProcessStartOptions? processOptions = null,
        bool terminateOnCancellation = true,
        CancellationToken cancellationToken = default)
    {
        var start = await PrepareAndStartAsync(
            context,
            processOptions,
            cancellationToken).ConfigureAwait(false);

        if (!start.Started)
        {
            return new MinecraftLaunchRunResult(start.Preparation, null, null);
        }

        await using var process = start.Process!;
        try
        {
            var exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new MinecraftLaunchRunResult(start.Preparation, start.LaunchPlan, exitCode);
        }
        catch (OperationCanceledException) when (terminateOnCancellation)
        {
            await process.TerminateAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
