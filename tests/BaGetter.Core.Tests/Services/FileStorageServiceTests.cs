using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using Moq;
using Xunit;

namespace BaGetter.Core.Tests.Services;

public class FileStorageServiceTests
{
    public class GetAsync : FactsBase
    {
        [Fact]
        public async Task ThrowsIfStorePathDoesNotExist()
        {
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _target.GetAsync("hello.txt"));
        }

        [Fact]
        public async Task ThrowsIfFileDoesNotExist()
        {
            // Ensure the store path exists.
            Directory.CreateDirectory(_storePath);

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _target.GetAsync("hello.txt"));

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                _target.GetAsync("hello/world.txt"));
        }

        [Fact]
        public async Task GetsStream()
        {
            // Arrange
            using (var content = StringStream("Hello world"))
            {
                await _target.PutAsync("hello.txt", content, "text/plain");
            }

            // Act
            var result = await _target.GetAsync("hello.txt");

            // Assert
            Assert.Equal("Hello world", await ToStringAsync(result));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (var path in OutsideStorePathData)
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.GetAsync(path));
            }
        }
    }

    public class GetDownloadUriAsync : FactsBase
    {
        [Fact]
        public async Task CreatesUriEvenIfDoesntExist()
        {
            var result = await _target.GetDownloadUriAsync("test.txt");
            var expected = new Uri(Path.Combine(_storePath, "test.txt"));

            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (var path in OutsideStorePathData)
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.GetDownloadUriAsync(path));
            }
        }
    }

    public class PutAsync : FactsBase
    {
        [Fact]
        public async Task SavesContent()
        {
            StoragePutResult result;
            using (var content = StringStream("Hello world"))
            {
                result = await _target.PutAsync("test.txt", content, "text/plain");
            }

            var path = Path.Combine(_storePath, "test.txt");

            Assert.True(File.Exists(path));
            Assert.Equal(StoragePutResult.Success, result);
            Assert.Equal("Hello world", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task ReturnsAlreadyExistsIfContentAlreadyExists()
        {
            // Arrange
            var path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "Hello world");

            StoragePutResult result;
            using (var content = StringStream("Hello world"))
            {
                // Act
                result = await _target.PutAsync("test.txt", content, "text/plain");
            }

            // Assert
            Assert.Equal(StoragePutResult.AlreadyExists, result);
        }

        [Fact]
        public async Task CancelledOrInterruptedWritesDoNotLeavePartialPackages()
        {
            var path = Path.Combine(_storePath, "interrupted.nupkg");
            using var content = new InterruptingStream();

            await Assert.ThrowsAsync<IOException>(() =>
                _target.PutAsync("interrupted.nupkg", content, "application/octet-stream"));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(_storePath, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task ConcurrentIdenticalPutsAreIdempotent()
        {
            using var first = StringStream("Hello world");
            using var second = StringStream("Hello world");

            var results = await Task.WhenAll(
                _target.PutAsync("same.nupkg", first, "application/octet-stream"),
                _target.PutAsync("same.nupkg", second, "application/octet-stream"));

            Assert.Contains(StoragePutResult.Success, results);
            Assert.Contains(StoragePutResult.AlreadyExists, results);
            Assert.Equal("Hello world", await File.ReadAllTextAsync(Path.Combine(_storePath, "same.nupkg")));
            Assert.Empty(Directory.GetFiles(_storePath, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task ConcurrentDifferentPutsNeverOverwriteTheWinner()
        {
            const int writers = 32;
            var streams = new Stream[writers];
            try
            {
                for (var i = 0; i < writers; i++)
                    streams[i] = StringStream($"Distinct package bytes {i}");

                var tasks = new Task<StoragePutResult>[writers];
                for (var i = 0; i < writers; i++)
                    tasks[i] = _target.PutAsync("immutable.nupkg", streams[i], "application/octet-stream");
                var results = await Task.WhenAll(tasks);

                Assert.Equal(1, results.Count(r => r == StoragePutResult.Success));
                Assert.Equal(writers - 1, results.Count(r => r == StoragePutResult.Conflict));
                var winningIndex = Array.IndexOf(results, StoragePutResult.Success);
                Assert.Equal($"Distinct package bytes {winningIndex}",
                    await File.ReadAllTextAsync(Path.Combine(_storePath, "immutable.nupkg")));
                Assert.Empty(Directory.GetFiles(_storePath, "*.tmp", SearchOption.AllDirectories));
            }
            finally
            {
                foreach (var stream in streams)
                    stream?.Dispose();
            }
        }

        private sealed class InterruptingStream : MemoryStream
        {
            public override async Task CopyToAsync(
                Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                throw new IOException("Simulated partial upload failure");
            }
        }

        [Fact]
        public async Task ReturnsConflictIfContentAlreadyExistsButContentsDoNotMatch()
        {
            // Arrange
            var path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "Hello world");

            StoragePutResult result;
            using (var content = StringStream("foo bar"))
            {
                // Act
                result = await _target.PutAsync("test.txt", content, "text/plain");
            }

            // Assert
            Assert.Equal(StoragePutResult.Conflict, result);
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (var path in OutsideStorePathData)
            {
                using var content = StringStream("Hello world");
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.PutAsync(path, content, "text/plain"));
            }
        }
    }

    public class DeleteAsync : FactsBase
    {
        [Fact]
        public async Task DoesNotThrowIfPathDoesNotExist()
        {
            await _target.DeleteAsync("test.txt");
        }

        [Fact]
        public async Task Deletes()
        {
            // Arrange
            var path = Path.Combine(_storePath, "test.txt");

            Directory.CreateDirectory(_storePath);
            await File.WriteAllTextAsync(path, "Hello world");

            // Act & Assert
            await _target.DeleteAsync("test.txt");

            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task NoAccessOutsideStorePath()
        {
            foreach (var path in OutsideStorePathData)
            {
                await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await _target.DeleteAsync(path));
            }
        }
    }

    public class RevisionCleanup : FactsBase
    {
        [Fact]
        public async Task DeletesOnlyAgedOrphansAndKeepsCommittedOrRecentRevisions()
        {
            var old = new string('a', 32);
            var live = new string('b', 32);
            var recent = new string('c', 32);
            var baseDir = Path.Combine(_storePath, "packages", "test.package", "1.0.0", "revisions");
            foreach (var revision in new[] { old, live, recent })
            {
                var folder = Path.Combine(baseDir, revision);
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, "test.nupkg"), revision);
                if (revision != recent)
                {
                    File.SetLastWriteTimeUtc(Path.Combine(folder, "test.nupkg"), DateTime.UtcNow.AddDays(-4));
                    Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddDays(-4));
                }
            }

            var count = await _target.CleanupUnreferencedRevisionsAsync(
                (id, version, revision, ct) =>
                {
                    Assert.Equal("test.package", id);
                    Assert.Equal(NuGetVersion.Parse("1.0.0"), version);
                    return Task.FromResult(revision == live);
                }, TimeSpan.FromDays(2));

            Assert.Equal(1, count);
            Assert.False(Directory.Exists(Path.Combine(baseDir, old)));
            Assert.True(Directory.Exists(Path.Combine(baseDir, live)));
            Assert.True(Directory.Exists(Path.Combine(baseDir, recent)));
        }

        [Fact]
        public async Task DoesNotDeleteFilesWhenDatabaseLookupFails()
        {
            var revision = new string('a', 32);
            var path = Path.Combine(_storePath, "packages", "test", "1.0.0", "revisions", revision);
            Directory.CreateDirectory(path);
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-4));
            await Assert.ThrowsAsync<IOException>(() => _target.CleanupUnreferencedRevisionsAsync(
                (id, version, candidate, ct) => throw new IOException("Database unavailable"),
                TimeSpan.FromDays(2)));
            Assert.True(Directory.Exists(path));
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly string _storePath;
        protected readonly Mock<IOptionsSnapshot<FileSystemStorageOptions>> _options;
        protected readonly FileStorageService _target;

        public FactsBase()
        {
            _storePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _options = new Mock<IOptionsSnapshot<FileSystemStorageOptions>>();

            _options
                .Setup(o => o.Value)
                .Returns(() => new FileSystemStorageOptions { Path = _storePath });

            _target = new FileStorageService(_options.Object);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_storePath, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        protected Stream StringStream(string input)
        {
            var bytes = Encoding.ASCII.GetBytes(input);

            return new MemoryStream(bytes);
        }

        protected async Task<string> ToStringAsync(Stream input)
        {
            using var reader = new StreamReader(input);
            return await reader.ReadToEndAsync();
        }

        public IEnumerable<string> OutsideStorePathData
        {
            get
            {
                var fullPath = Path.GetFullPath(_storePath);
                yield return "../file";
                yield return ".";
                yield return $"../{Path.GetFileName(_storePath)}";
                yield return $"../{Path.GetFileName(_storePath)}suffix";
                yield return $"../{Path.GetFileName(_storePath)}suffix/file";
                yield return fullPath;
                yield return fullPath + Path.DirectorySeparatorChar;
                yield return fullPath + Path.DirectorySeparatorChar + "..";
                yield return fullPath + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "file";
                yield return Path.GetPathRoot(_storePath);
                yield return Path.Combine(Path.GetPathRoot(_storePath), "file");
            }
        }
    }
}
