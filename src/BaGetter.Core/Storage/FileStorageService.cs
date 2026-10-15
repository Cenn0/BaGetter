using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

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
