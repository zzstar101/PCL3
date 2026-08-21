using System.Collections.Concurrent;

namespace PCL3.Minecraft.Java;

public sealed record JavaProbeFailure(
    JavaInstallationCandidate Candidate,
    string ErrorType,
    string Message);

public sealed record JavaRuntimeInventory(
    IReadOnlyList<JavaRuntimeDescriptor> Runtimes,
    IReadOnlyList<JavaProbeFailure> Failures);

public static class JavaRuntimeInventoryBuilder
{
    public static async Task<JavaRuntimeInventory> ProbeAsync(
        IEnumerable<JavaInstallationCandidate> candidates,
        int maxConcurrency = 4,
        TimeSpan? probeTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var runtimes = new ConcurrentBag<JavaRuntimeDescriptor>();
        var failures = new ConcurrentBag<JavaProbeFailure>();

        var tasks = candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var runtime = await JavaRuntimeProbe.ProbeAsync(
                    candidate,
                    probeTimeout,
                    cancellationToken).ConfigureAwait(false);
                runtimes.Add(runtime);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new JavaProbeFailure(
                    candidate,
                    exception.GetType().Name,
                    exception.Message));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return new JavaRuntimeInventory(
            runtimes
                .OrderBy(runtime => runtime.MajorVersion)
                .ThenBy(runtime => runtime.Architecture)
                .ThenBy(runtime => runtime.HomePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            failures
                .OrderBy(failure => failure.Candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
