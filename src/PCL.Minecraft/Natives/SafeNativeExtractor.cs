using System.IO.Compression;

namespace PCL3.Minecraft.Natives;

public static class SafeNativeExtractor
{
    public static async Task ExtractAsync(
        NativeExtractionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var destination = Path.GetFullPath(plan.DestinationDirectory);
        Directory.CreateDirectory(destination);
        EnsureNotReparsePoint(destination);

        foreach (var archive in plan.Archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExtractArchiveAsync(
                archive,
                destination,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExtractArchiveAsync(
        NativeExtractionArchive archive,
        string destination,
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
                await using (var input = entry.Open())
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

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
}
