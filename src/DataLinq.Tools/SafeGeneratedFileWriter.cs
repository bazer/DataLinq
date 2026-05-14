using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataLinq.ErrorHandling;
using ThrowAway;

namespace DataLinq.Tools;

public static class SafeGeneratedFileWriter
{
    public static Option<bool, IDLOptionFailure> WriteAll(
        IEnumerable<(string path, string contents)> files,
        Encoding encoding,
        Action<string>? log = null)
    {
        if (!TryCreateWritePlan(files, out var writePlan, out var failure))
            return failure!;

        return WriteAllCore(writePlan, encoding, log);
    }

    private static bool TryCreateWritePlan(
        IEnumerable<(string path, string contents)> files,
        out List<GeneratedFileWrite> writePlan,
        out IDLOptionFailure? failure)
    {
        writePlan = [];
        failure = null;

        foreach (var (path, contents) in files)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                failure = DLOptionFailure.Fail(DLFailureType.InvalidArgument, "Generated file path cannot be empty.");
                return false;
            }

            writePlan.Add(new GeneratedFileWrite(Path.GetFullPath(path), contents ?? ""));
        }

        var duplicate = writePlan
            .GroupBy(static file => file.TargetPath, GetPathComparer())
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate != null)
        {
            failure = DLOptionFailure.Fail(
                DLFailureType.InvalidArgument,
                $"Generated output contains duplicate target path '{duplicate.Key}'.");
            return false;
        }

        return true;
    }

    private static Option<bool, IDLOptionFailure> WriteAllCore(
        List<GeneratedFileWrite> writePlan,
        Encoding encoding,
        Action<string>? log)
    {
        var stagedWrites = new List<StagedGeneratedFileWrite>();

        try
        {
            foreach (var file in writePlan)
            {
                var directory = Path.GetDirectoryName(file.TargetPath);
                if (string.IsNullOrWhiteSpace(directory))
                    return DLOptionFailure.Fail(DLFailureType.InvalidArgument, $"Generated file path '{file.TargetPath}' does not have a directory.");

                Directory.CreateDirectory(directory);

                var tempPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(file.TargetPath)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tempPath, file.Contents, encoding);
                stagedWrites.Add(new StagedGeneratedFileWrite(file.TargetPath, tempPath));
            }

            foreach (var stagedWrite in stagedWrites)
            {
                log?.Invoke($"Writing {stagedWrite.TargetPath}");
                CommitStagedWrite(stagedWrite);
            }

            foreach (var stagedWrite in stagedWrites)
                DeleteIfExists(stagedWrite.BackupPath);

            return true;
        }
        catch (Exception exception)
        {
            var rollbackFailure = RollBack(stagedWrites);
            var message = rollbackFailure == null
                ? $"Failed to write generated files. Existing files were restored. {exception.Message}"
                : $"Failed to write generated files and rollback also failed. {exception.Message} Rollback failure: {rollbackFailure.Message}";

            return DLOptionFailure.Fail(DLFailureType.Exception, message);
        }
    }

    private static void CommitStagedWrite(StagedGeneratedFileWrite stagedWrite)
    {
        if (File.Exists(stagedWrite.TargetPath))
        {
            stagedWrite.BackupPath = Path.Combine(
                Path.GetDirectoryName(stagedWrite.TargetPath)!,
                $".{Path.GetFileName(stagedWrite.TargetPath)}.{Guid.NewGuid():N}.bak");
            File.Move(stagedWrite.TargetPath, stagedWrite.BackupPath);
        }

        File.Move(stagedWrite.TempPath, stagedWrite.TargetPath);
        stagedWrite.Committed = true;
    }

    private static Exception? RollBack(List<StagedGeneratedFileWrite> stagedWrites)
    {
        try
        {
            foreach (var stagedWrite in stagedWrites.AsEnumerable().Reverse())
            {
                if (stagedWrite.Committed)
                    DeleteIfExists(stagedWrite.TargetPath);

                if (stagedWrite.BackupPath != null && File.Exists(stagedWrite.BackupPath))
                    File.Move(stagedWrite.BackupPath, stagedWrite.TargetPath);

                DeleteIfExists(stagedWrite.TempPath);
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void DeleteIfExists(string? path)
    {
        if (path != null && File.Exists(path))
            File.Delete(path);
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record GeneratedFileWrite(string TargetPath, string Contents);

    private sealed class StagedGeneratedFileWrite
    {
        public StagedGeneratedFileWrite(string targetPath, string tempPath)
        {
            TargetPath = targetPath;
            TempPath = tempPath;
        }

        public string TargetPath { get; }
        public string TempPath { get; }
        public string? BackupPath { get; set; }
        public bool Committed { get; set; }
    }
}
