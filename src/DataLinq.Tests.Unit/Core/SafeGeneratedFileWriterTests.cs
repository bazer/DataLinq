using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Tools;

namespace DataLinq.Tests.Unit.Core;

public class SafeGeneratedFileWriterTests
{
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
