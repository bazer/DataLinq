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
    /// <summary>
    /// Stages all files before replacing targets, restoring earlier targets if a write fails.
    /// A backup-cleanup failure is reported after all targets are committed; the generated
    /// output is retained and the failure identifies backups that need manual cleanup.
    /// </summary>
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
                stagedWrites.Add(new StagedGeneratedFileWrite(file.TargetPath, tempPath));
                File.WriteAllText(tempPath, file.Contents, encoding);
            }

            foreach (var stagedWrite in stagedWrites)
            {
                log?.Invoke($"Writing {stagedWrite.TargetPath}");
                CommitStagedWrite(stagedWrite);
            }
        }
        catch (Exception exception)
        {
            var rollbackFailure = RollBack(stagedWrites);
            var message = rollbackFailure == null
                ? $"Failed to write generated files. Existing files were restored. {exception.Message}"
                : $"Failed to write generated files and rollback also failed. {exception.Message} Rollback failure: {rollbackFailure.Message}";

            return DLOptionFailure.Fail(DLFailureType.Exception, message);
        }

        // All targets are committed. Backup cleanup is a separate phase: once one
        // backup has been deleted, the batch can no longer safely roll back.
        var cleanupFailures = new List<string>();
        foreach (var stagedWrite in stagedWrites)
        {
            try
            {
                DeleteIfExists(stagedWrite.BackupPath);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add($"'{stagedWrite.BackupPath}': {exception.Message}");
            }
        }

        return cleanupFailures.Count == 0
            ? true
            : DLOptionFailure.Fail(DLFailureType.Exception,
                "All generated files were written, but backup cleanup failed. The generated output was retained. " +
                string.Join(" ", cleanupFailures));
    }

    private static void CommitStagedWrite(StagedGeneratedFileWrite stagedWrite)
    {
        if (File.Exists(stagedWrite.TargetPath))
        {
            var backupPath = Path.Combine(
                Path.GetDirectoryName(stagedWrite.TargetPath)!,
                $".{Path.GetFileName(stagedWrite.TargetPath)}.{Guid.NewGuid():N}.bak");
            File.Move(stagedWrite.TargetPath, backupPath);
            stagedWrite.BackupPath = backupPath;
        }

        File.Move(stagedWrite.TempPath, stagedWrite.TargetPath);
        stagedWrite.Committed = true;
    }

    private static Exception? RollBack(List<StagedGeneratedFileWrite> stagedWrites)
    {
        var failures = new List<Exception>();
        foreach (var stagedWrite in stagedWrites.AsEnumerable().Reverse())
        {
            try
            {
                if (stagedWrite.BackupPath != null)
                {
                    // Never remove the only remaining copy if a backup was lost.
                    if (!File.Exists(stagedWrite.BackupPath))
                        throw new IOException($"Cannot restore '{stagedWrite.TargetPath}': backup '{stagedWrite.BackupPath}' is missing. The current output was retained.");

                    if (stagedWrite.Committed)
                        DeleteIfExists(stagedWrite.TargetPath);
                    File.Move(stagedWrite.BackupPath, stagedWrite.TargetPath);
                }
                else if (stagedWrite.Committed)
                {
                    DeleteIfExists(stagedWrite.TargetPath);
                }
            }
            catch (Exception exception)
            {
                failures.Add(new IOException($"Rollback failed for '{stagedWrite.TargetPath}'. {exception.Message}", exception));
            }

            // A failed restore must not prevent cleanup or restoration of other files.
            try
            {
                DeleteIfExists(stagedWrite.TempPath);
            }
            catch (Exception exception)
            {
                failures.Add(new IOException($"Cannot remove staged file '{stagedWrite.TempPath}'. {exception.Message}", exception));
            }
        }

        return failures.Count == 0 ? null : new AggregateException(failures);
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
