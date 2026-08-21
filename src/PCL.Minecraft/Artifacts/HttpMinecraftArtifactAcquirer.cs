using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;

namespace PCL3.Minecraft.Artifacts;

public sealed class HttpMinecraftArtifactAcquirer : IMinecraftArtifactAcquirer
{
    private readonly HttpClient _httpClient;
    private readonly MinecraftArtifactAcquirerOptions _options;

    public HttpMinecraftArtifactAcquirer(
        HttpClient httpClient,
        MinecraftArtifactAcquirerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new MinecraftArtifactAcquirerOptions();

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.AttemptsPerSource, 1);
    }

    public async Task<MinecraftArtifactAcquisitionResult> AcquireAsync(
        MinecraftArtifactAcquisitionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        using var semaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        var results = new ConcurrentBag<MinecraftArtifactAcquisitionItemResult>();

        var tasks = plan.Artifacts.Select(async artifact =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results.Add(await AcquireOneAsync(artifact, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return new MinecraftArtifactAcquisitionResult(
            results
                .OrderBy(result => result.Artifact.Purpose)
                .ThenBy(result => result.Artifact.Id, StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<MinecraftArtifactAcquisitionItemResult> AcquireOneAsync(
        MinecraftArtifactRequest artifact,
        CancellationToken cancellationToken)
    {
        artifact = artifact.Normalize();
        var existing = await MinecraftArtifactVerifier.VerifyAsync(
            artifact,
            cancellationToken).ConfigureAwait(false);
        if (existing.IsValid)
        {
            return new MinecraftArtifactAcquisitionItemResult(
                artifact,
                MinecraftArtifactAcquisitionStatus.AlreadyValid);
        }

        if (artifact.Sources.Count == 0)
        {
            return new MinecraftArtifactAcquisitionItemResult(
                artifact,
                MinecraftArtifactAcquisitionStatus.Failed,
                Error: $"Artifact '{artifact.Id}' is missing or invalid and has no download source.");
        }

        var errors = new List<string>();
        foreach (var source in artifact.Sources)
        {
            if (!TryValidateSource(source, out var uri, out var sourceError))
            {
                errors.Add(sourceError!);
                continue;
            }

            for (var attempt = 1; attempt <= _options.AttemptsPerSource; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var finalSource = await DownloadVerifiedAsync(
                        artifact,
                        uri!,
                        cancellationToken).ConfigureAwait(false);
                    return new MinecraftArtifactAcquisitionItemResult(
                        artifact,
                        MinecraftArtifactAcquisitionStatus.Downloaded,
                        finalSource.AbsoluteUri);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or IOException or InvalidDataException)
                {
                    errors.Add($"{uri} (attempt {attempt}): {exception.Message}");
                }
            }
        }

        return new MinecraftArtifactAcquisitionItemResult(
            artifact,
            MinecraftArtifactAcquisitionStatus.Failed,
            Error: string.Join(" | ", errors));
    }

    private bool TryValidateSource(string source, out Uri? uri, out string? error)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out uri))
        {
            error = $"Artifact source '{source}' is not an absolute URI.";
            return false;
        }

        return ValidateTransport(uri, out error);
    }

    private bool ValidateTransport(Uri uri, out string? error)
    {
        if (uri.Scheme is not ("https" or "http"))
        {
            error = $"Artifact source '{uri}' uses unsupported scheme '{uri.Scheme}'.";
            return false;
        }

        if (uri.Scheme == "http" && !_options.AllowInsecureHttp)
        {
            error = $"Artifact source '{uri}' uses insecure HTTP and is disabled by policy.";
            return false;
        }

        error = null;
        return true;
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Mojang manifests mandate SHA-1 as an artifact integrity/cache identifier; it is not used as a security signature.")]
    private async Task<Uri> DownloadVerifiedAsync(
        MinecraftArtifactRequest artifact,
        Uri source,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw new HttpRequestException(
                $"Server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        var finalUri = response.RequestMessage?.RequestUri ?? source;
        if (!ValidateTransport(finalUri, out var transportError))
        {
            throw new InvalidDataException(
                $"Download redirect violated transport policy: {transportError}");
        }

        if (artifact.Size is { } expectedSize &&
            response.Content.Headers.ContentLength is { } contentLength &&
            contentLength != expectedSize)
        {
            throw new InvalidDataException(
                $"Content-Length {contentLength} does not match expected size {expectedSize}.");
        }

        var targetDirectory = Path.GetDirectoryName(artifact.LocalPath) ??
            throw new InvalidDataException($"Artifact path '{artifact.LocalPath}' has no parent directory.");
        Directory.CreateDirectory(targetDirectory);

        var temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(artifact.LocalPath)}.pcl3-{Guid.NewGuid():N}.tmp");

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = artifact.Sha1 is null
                ? null
                : IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            long total = 0;

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

                    total = checked(total + read);
                    if (artifact.Size is { } maximumExpected && total > maximumExpected)
                    {
                        throw new InvalidDataException(
                            $"Downloaded size exceeded expected size {maximumExpected}.");
                    }

                    hash?.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (artifact.Size is { } expected && total != expected)
            {
                throw new InvalidDataException(
                    $"Downloaded size {total} does not match expected size {expected}.");
            }

            if (artifact.Sha1 is not null)
            {
                var actualSha1 = Convert.ToHexStringLower(hash!.GetHashAndReset());
                if (!string.Equals(actualSha1, artifact.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Downloaded SHA-1 {actualSha1} does not match expected {artifact.Sha1}.");
                }
            }

            File.Move(temporaryPath, artifact.LocalPath, overwrite: true);
            return finalUri;
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
