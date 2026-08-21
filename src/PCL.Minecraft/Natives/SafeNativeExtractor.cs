using System.Buffers;
using System.IO.Compression;

namespace PCL3.Minecraft.Natives;

public sealed record SafeNativeExtractionOptions(
    int MaxEntries = 10_000,
    long MaxEntryBytes = 512L * 1024 * 1024,
    long MaxTotalBytes = 2L * 1024 * 1024 * 1024);

public static class SafeNativeExtractor
{
    public static async Task ExtractAsync(
        NativeExtractionPlan plan,
        CancellationToken cancellationToken = default,
        SafeNativeExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new SafeNativeExtractionOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxEntryBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxTotalBytes, 1);

        var destination = Path.GetFullPath(plan.DestinationDirectory);
        Directory.CreateDirectory(destination);
        EnsureNotReparsePoint(destination);
        var budget = new ExtractionBudget(options);

        foreach (var archive in plan.Archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExtractArchiveAsync(
                archive,
                destination,
                budget,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExtractArchiveAsync(
        NativeExtractionArchive archive,
        string destination,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archive.ArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            budget.RegisterEntry(entry.Length);

            var normalizedEntryName = entry.FullName.Replace('\\', '/');
            if (ShouldExclude(normalizedEntryName, archive.Excludes) ||
                normalizedEntryName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var targetPath = GetSafeTargetPath(destination, normalizedEntryName);
            var targetDirectory = Path.GetDirectoryName(targetPath) ?? destination;
            EnsureSafeDirectoryChain(destination, targetDirectory);
            Directory.CreateDirectory(targetDirectory);
            EnsureSafeDirectoryChain(destination, targetDirectory);

            var temporaryPath = targetPath + $".pcl3-{Guid.NewGuid():N}.tmp";
            try
            {
                await using var input = entry.Open();
                await using var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await CopyWithBudgetAsync(input, output, budget, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static async Task CopyWithBudgetAsync(
        Stream input,
        Stream output,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long entryBytes = 0;

        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                entryBytes = checked(entryBytes + read);
                budget.RegisterExtractedBytes(entryBytes, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool ShouldExclude(string entryName, IReadOnlyList<string> excludes)
    {
        foreach (var exclude in excludes)
        {
            var normalizedExclude = exclude.Replace('\\', '/').TrimStart('/');
            if (normalizedExclude.Length != 0 &&
                entryName.StartsWith(normalizedExclude, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSafeTargetPath(string destination, string entryName)
    {
        if (entryName.StartsWith("/", StringComparison.Ordinal) ||
            entryName.Contains('\0'))
        {
            throw new InvalidDataException($"Unsafe native archive entry '{entryName}'.");
        }

        var relative = entryName.Replace('/', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(destination, relative));
        var relativeToRoot = Path.GetRelativePath(destination, target);

        if (Path.IsPathRooted(relativeToRoot) ||
            relativeToRoot.Equals("..", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Native archive entry '{entryName}' escapes the extraction directory.");
        }

        return target;
    }

    private static void EnsureSafeDirectoryChain(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory);
        if (relative is ".")
        {
            EnsureNotReparsePoint(root);
            return;
        }

        var current = root;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                EnsureNotReparsePoint(current);
            }
        }
    }

    private static void EnsureNotReparsePoint(string directory)
    {
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Native extraction refuses to traverse reparse-point directory '{directory}'.");
        }
    }

    private sealed class ExtractionBudget(SafeNativeExtractionOptions options)
    {
        private int _entries;
        private long _totalExtractedBytes;

        public void RegisterEntry(long declaredBytes)
        {
            _entries = checked(_entries + 1);
            if (_entries > options.MaxEntries)
            {
                throw new InvalidDataException(
                    $"Native extraction exceeded the entry limit of {options.MaxEntries}.");
            }

            if (declaredBytes < 0 || declaredBytes > options.MaxEntryBytes)
            {
                throw new InvalidDataException(
                    $"Native archive entry declares {declaredBytes} bytes; limit is {options.MaxEntryBytes}.");
            }
        }

        public void RegisterExtractedBytes(long entryBytes, int newlyExtractedBytes)
        {
            if (entryBytes > options.MaxEntryBytes)
            {
                throw new InvalidDataException(
                    $"Native archive entry exceeded the extraction limit of {options.MaxEntryBytes} bytes.");
            }

            _totalExtractedBytes = checked(_totalExtractedBytes + newlyExtractedBytes);
            if (_totalExtractedBytes > options.MaxTotalBytes)
            {
                throw new InvalidDataException(
                    $"Native extraction exceeded the total limit of {options.MaxTotalBytes} bytes.");
            }
        }
    }
}
