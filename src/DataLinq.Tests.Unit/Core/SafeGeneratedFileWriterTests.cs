using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Tools;

namespace DataLinq.Tests.Unit.Core;

public class SafeGeneratedFileWriterTests
{
    [Test]
    public async Task WriteAll_NoOverwriteRejectsLateCollisionAndRollsBackEarlierNewFile()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var first = Path.Combine(fixture.BasePath, "First.cs");
        var second = Path.Combine(fixture.BasePath, "Second.cs");
        var result = SafeGeneratedFileWriter.WriteAll(
            [(first, "first generated"), (second, "second generated")], Encoding.UTF8, overwriteExisting: false,
            message =>
            {
                if (message.EndsWith(second, StringComparison.Ordinal))
                    File.WriteAllText(second, "concurrently created");
            });

        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(File.Exists(first)).IsFalse();
        await Assert.That(File.ReadAllText(second)).IsEqualTo("concurrently created");
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.tmp").Length).IsEqualTo(0);
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.bak").Length).IsEqualTo(0);
    }

    [Test]
    public async Task WriteAll_WritesAllFilesOnSuccess()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var firstPath = Path.Combine(fixture.BasePath, "Models", "First.cs");
        var secondPath = Path.Combine(fixture.BasePath, "Models", "Nested", "Second.cs");

        var result = SafeGeneratedFileWriter.WriteAll(
            [
                (firstPath, "first"),
                (secondPath, "second")
            ],
            Encoding.UTF8);

        await Assert.That(result.HasFailed).IsFalse();
        await Assert.That(File.ReadAllText(firstPath)).IsEqualTo("first");
        await Assert.That(File.ReadAllText(secondPath)).IsEqualTo("second");
    }

    [Test]
    public async Task WriteAll_RejectsDuplicateTargetsBeforeWriting()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var targetPath = Path.Combine(fixture.BasePath, "Existing.cs");
        File.WriteAllText(targetPath, "existing");

        var result = SafeGeneratedFileWriter.WriteAll(
            [
                (targetPath, "first"),
                (targetPath, "second")
            ],
            Encoding.UTF8);

        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(File.ReadAllText(targetPath)).IsEqualTo("existing");
    }

    [Test]
    public async Task WriteAll_RollsBackPreviouslyReplacedFilesWhenLaterWriteFails()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var existingPath = Path.Combine(fixture.BasePath, "Existing.cs");
        var blockedPath = Path.Combine(fixture.BasePath, "Blocked.cs");
        File.WriteAllText(existingPath, "existing");
        Directory.CreateDirectory(blockedPath);

        var result = SafeGeneratedFileWriter.WriteAll(
            [
                (existingPath, "updated"),
                (blockedPath, "blocked")
            ],
            Encoding.UTF8);

        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(File.ReadAllText(existingPath)).IsEqualTo("existing");
        await Assert.That(Directory.Exists(blockedPath)).IsTrue();
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.tmp").Length).IsEqualTo(0);
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.bak").Length).IsEqualTo(0);
    }

    [Test]
    [TUnit.Core.RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task WriteAll_BackupCleanupFailureRetainsEveryCommittedOutput()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var paths = new[] { "First.cs", "Second.cs", "Third.cs" }
            .Select(name => Path.Combine(fixture.BasePath, name)).ToArray();
        foreach (var path in paths)
            File.WriteAllText(path, "original");

        string? protectedBackup = null;
        try
        {
            var result = SafeGeneratedFileWriter.WriteAll(
                paths.Select(path => (path, "replacement")), Encoding.UTF8,
                message =>
                {
                    if (message.EndsWith(paths[2], StringComparison.Ordinal))
                    {
                        protectedBackup = Directory.GetFiles(fixture.BasePath, ".Second.cs.*.bak").Single();
                        // Windows allows renaming this backup but refuses its deletion.
                        File.SetAttributes(protectedBackup, FileAttributes.ReadOnly);
                    }
                });

            await Assert.That(result.HasFailed).IsTrue();
            await Assert.That(result.Failure.ToString()).Contains("All generated files were written");
            await Assert.That(result.Failure.ToString()).Contains(protectedBackup!);
            foreach (var path in paths)
                await Assert.That(File.ReadAllText(path)).IsEqualTo("replacement");
            await Assert.That(Directory.GetFiles(fixture.BasePath, "*.bak").Length).IsEqualTo(1);
            await Assert.That(Directory.GetFiles(fixture.BasePath, "*.tmp").Length).IsEqualTo(0);
        }
        finally
        {
            if (protectedBackup != null && File.Exists(protectedBackup))
                File.SetAttributes(protectedBackup, FileAttributes.Normal);
        }
    }

    [Test]
    public async Task WriteAll_MissingBackupRetainsCurrentFileAndContinuesOtherRestores()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var paths = new[] { "First.cs", "Second.cs", "Third.cs" }
            .Select(name => Path.Combine(fixture.BasePath, name)).ToArray();
        foreach (var path in paths)
            File.WriteAllText(path, "original");

        var result = SafeGeneratedFileWriter.WriteAll(
            paths.Select(path => (path, "replacement")), Encoding.UTF8,
            message =>
            {
                if (message.EndsWith(paths[2], StringComparison.Ordinal))
                {
                    File.Delete(Directory.GetFiles(fixture.BasePath, ".Second.cs.*.bak").Single());
                    throw new IOException("Injected failure after an external backup removal.");
                }
            });

        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(result.Failure.ToString()).Contains(paths[1]);
        await Assert.That(result.Failure.ToString()).Contains("backup");
        await Assert.That(result.Failure.ToString()).DoesNotContain("Existing files were restored");
        await Assert.That(File.ReadAllText(paths[0])).IsEqualTo("original");
        await Assert.That(File.ReadAllText(paths[1])).IsEqualTo("replacement");
        await Assert.That(File.ReadAllText(paths[2])).IsEqualTo("original");
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.tmp").Length).IsEqualTo(0);
    }

    [Test]
    public async Task WriteAll_RollsBackNewTargetsAlongsideExistingFiles()
    {
        using var fixture = SafeGeneratedFileWriterFixture.Create();
        var existing = Path.Combine(fixture.BasePath, "Existing.cs");
        var created = Path.Combine(fixture.BasePath, "Created.cs");
        var blocked = Path.Combine(fixture.BasePath, "Blocked.cs");
        File.WriteAllText(existing, "original");
        Directory.CreateDirectory(blocked);

        var result = SafeGeneratedFileWriter.WriteAll(
            [(existing, "replacement"), (created, "new"), (blocked, "blocked")], Encoding.UTF8);

        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(File.ReadAllText(existing)).IsEqualTo("original");
        await Assert.That(File.Exists(created)).IsFalse();
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.tmp").Length).IsEqualTo(0);
        await Assert.That(Directory.GetFiles(fixture.BasePath, "*.bak").Length).IsEqualTo(0);
    }


    private sealed class SafeGeneratedFileWriterFixture : IDisposable
    {
        private SafeGeneratedFileWriterFixture(string basePath)
        {
            BasePath = basePath;
        }

        public string BasePath { get; }

        public static SafeGeneratedFileWriterFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-safe-writer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new SafeGeneratedFileWriterFixture(basePath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(BasePath))
                    Directory.Delete(BasePath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
