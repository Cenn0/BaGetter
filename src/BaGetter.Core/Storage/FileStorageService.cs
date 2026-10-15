using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGetter.Core;

/// <summary>
/// Stores content on disk.
/// </summary>
public class FileStorageService : IStorageService
{
    // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
    private const int DefaultCopyBufferSize = 81920;

    private readonly string _storePath;

    public FileStorageService(IOptionsSnapshot<FileSystemStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Resolve relative path components ('.'/'..') and ensure there is a trailing slash.
        _storePath = Path.GetFullPath(options.Value.Path);
        if (!_storePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            _storePath += Path.DirectorySeparatorChar;
    }

    public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);
        var content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return Task.FromResult<Stream>(content);
    }

    public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Uri(GetFullPath(path));

        return Task.FromResult(result);
    }

    public async Task<StoragePutResult> PutAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrEmpty(contentType)) throw new ArgumentException("Content type is required", nameof(contentType));

        cancellationToken.ThrowIfCancellationRequested();

        path = GetFullPath(path);

        // Ensure that the path exists.
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        // Stage the entire payload next to its final path, on the same filesystem.
        // A failed or cancelled write must never expose a partial package at its
        // permanent path. File.Move below is an atomic rename on local filesystems.
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path), $".bagetter-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fileStream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                DefaultCopyBufferSize, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(fileStream, DefaultCopyBufferSize, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            try
            {
                // Do not overwrite an existing version: NuGet versions are immutable.
                File.Move(temporaryPath, path);
                return StoragePutResult.Success;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another writer may have won the race. Compare full content with
                // the committed file, never with another writer's staging file.
                using var targetStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                content.Position = 0;
                return content.Matches(targetStream)
                    ? StoragePutResult.AlreadyExists
                    : StoragePutResult.Conflict;
            }
        }
        finally
        {
            // Also covers cancellations and I/O errors during the staging copy.
            File.Delete(temporaryPath);
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(GetFullPath(path));
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reclaim immutable revision directories which are no longer referenced
    /// by the database. Never touches legacy package paths.
    /// </summary>
    /// <remarks>
    /// Use a substantial minimum age to avoid racing uploads which have not
    /// published their database row yet. Only call against a live database.
    /// </remarks>
    public async Task<int> CleanupUnreferencedRevisionsAsync(
        Func<string, NuGetVersion, string, CancellationToken, Task<bool>> isReferenced,
        TimeSpan minimumAge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isReferenced);
        if (minimumAge < TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "Minimum age must be at least 24 hours");

        var root = Path.Combine(_storePath, "packages");
        if (!Directory.Exists(root)) return 0;

        var cutoff = DateTime.UtcNow - minimumAge;
        var deleted = 0;
        foreach (var idDirectory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLink(idDirectory)) continue;
            var id = Path.GetFileName(idDirectory);
            foreach (var versionDirectory in Directory.EnumerateDirectories(idDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsLink(versionDirectory) ||
                    !NuGetVersion.TryParse(Path.GetFileName(versionDirectory), out var version))
                    continue;

                var revisions = Path.Combine(versionDirectory, "revisions");
                if (!Directory.Exists(revisions) || IsLink(revisions)) continue;
                foreach (var directory in Directory.EnumerateDirectories(revisions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsLink(directory)) continue;

                    var revision = Path.GetFileName(directory);
                    if (!Guid.TryParseExact(revision, "N", out _) ||
                        !string.Equals(revision, revision.ToLowerInvariant(), StringComparison.Ordinal))
                        continue;

                    // A recent directory or file can be part of an in-flight upload.
                    if (Directory.GetLastWriteTimeUtc(directory) >= cutoff ||
                        Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                            .Any(file => File.GetLastWriteTimeUtc(file) >= cutoff))
                        continue;

                    // If the DB is unavailable the delegate throws and cleanup stops
                    // without deleting an unverified revision.
                    if (await isReferenced(id, version, revision, cancellationToken)) continue;
                    try
                    {
                        Directory.Delete(directory, recursive: true);
                        deleted++;
                    }
                    catch (IOException)
                    {
                        // Files may still be open on Windows. Retry next sweep.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // An operator can resolve filesystem permissions.
                    }
                }
            }
        }
        return deleted;
    }

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private string GetFullPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Path is required", nameof(path));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_storePath, path));

        // Verify path is under the _storePath.
        if (!fullPath.StartsWith(_storePath, StringComparison.Ordinal) ||
            fullPath.Length == _storePath.Length)
        {
            throw new ArgumentException("Path resolves outside store path", nameof(path));
        }

        return fullPath;
    }
}
