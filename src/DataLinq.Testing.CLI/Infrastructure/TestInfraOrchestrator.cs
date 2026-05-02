using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal sealed class TestInfraOrchestrator(
    TestInfraCliSettings settings,
    PodmanClient podman,
    TestInfraRuntimeStateStore stateStore)
{
    private readonly object lifecycleSync = new();

    public void Up(CliTargetSelection selection, bool recreate)
    {
        lock (lifecycleSync)
        {
            if (selection.ServerTargets.Count > 0)
                podman.EnsureAvailable();

            if (recreate)
                Down(remove: true, selection: null);

            foreach (var target in selection.ServerTargets)
                EnsureContainerStarted(target);

            Wait(selection, holdLifecycleLock: true);
        }
    }

    public void Wait(CliTargetSelection selection) => Wait(selection, holdLifecycleLock: false);

    private void Wait(CliTargetSelection selection, bool holdLifecycleLock)
    {
        if (holdLifecycleLock)
        {
            WaitCore(selection);
            return;
        }

        lock (lifecycleSync)
            WaitCore(selection);
    }

    private void WaitCore(CliTargetSelection selection)
    {
        if (selection.ServerTargets.Count > 0)
            podman.EnsureAvailable();

        var host = ResolveHost(selection);
        var waitTasks = new List<Task>(selection.ServerTargets.Count);
        foreach (var target in selection.ServerTargets)
        {
            ConsoleSync.WriteLine($"Waiting for {target.DisplayName} on port {target.HostPort}...");
            waitTasks.Add(Task.Run(() => WaitForTarget(target, host)));
        }

        Task.WhenAll(waitTasks).GetAwaiter().GetResult();
        if (selection.ServerTargets.Count > 0)
            PersistState(selection, host);
        ConsoleSync.WriteLine($"Test infrastructure is ready for targets [{string.Join(", ", selection.Targets.Select(x => x.Id))}].");
    }

    public void PersistState(CliTargetSelection selection)
    {
        PersistState(selection, ResolveHost(selection));
    }

    public void Down(bool remove, CliTargetSelection? selection)
    {
        lock (lifecycleSync)
        {
            podman.EnsureAvailable();

            var targets = selection?.ServerTargets ?? TestCliCatalog.Targets
                .Where(x => x.ServerTarget is not null)
                .Select(x => x.ServerTarget!)
                .ToArray();

            foreach (var target in targets)
            {
                var containerName = settings.GetContainerName(target);
                if (!ContainerExists(containerName))
                    continue;

                if (remove)
                {
                    RemoveContainerForcefully(containerName);
                    continue;
                }

                if (ContainerRunning(containerName))
                {
                    ConsoleSync.WriteLine($"Stopping container '{containerName}'...");
                    podman.Execute(["stop", containerName]).ThrowIfFailed($"Failed to stop container '{containerName}'.");
                }
            }

            CleanupLegacyArtifacts(remove);
            RefreshRuntimeState();
        }
    }

    public void Reset(CliTargetSelection selection)
    {
        Down(remove: true, selection);
        Up(selection, recreate: false);
    }

    private void EnsureContainerStarted(DatabaseServerTarget target)
    {
        var containerName = settings.GetContainerName(target);
        if (!ContainerExists(containerName))
        {
            ConsoleSync.WriteLine($"Creating {target.DisplayName} container '{containerName}'...");
            CreateContainer(target, containerName);
            return;
        }

        if (!ContainerRunning(containerName))
        {
            ConsoleSync.WriteLine($"Starting {target.DisplayName} container '{containerName}'...");
            podman.Execute(["start", containerName]).ThrowIfFailed($"Failed to start container '{containerName}'.");
        }
    }

    private void WaitForTarget(DatabaseServerTarget target, string host)
    {
        var containerName = settings.GetContainerName(target);

        switch (target.Family)
        {
            case DatabaseServerFamily.MySql:
                WaitForMySqlAdmin(containerName, settings.AdminPassword);
                InitializeMySqlHostAdminUser(containerName);
                break;
            case DatabaseServerFamily.MariaDb:
                WaitForMariaDbReady(containerName);
                InitializeMariaDbHostAdminUser(containerName);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database family '{target.Family}'.");
        }

        WaitForHostPort(host, target.HostPort, $"host TCP port {host}:{target.HostPort}");
    }

    private void CreateContainer(DatabaseServerTarget target, string containerName)
    {
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name", containerName,
            "-p", $"{target.HostPort}:3306"
        };

        arguments.AddRange(target.Family switch
        {
            DatabaseServerFamily.MySql =>
            [
                "-e", $"MYSQL_ROOT_PASSWORD={settings.AdminPassword}",
                "-e", "MYSQL_ROOT_HOST=%",
                "-e", $"MYSQL_DATABASE={settings.EmployeesDatabase}",
                "-e", $"MYSQL_USER={settings.ApplicationUser}",
                "-e", $"MYSQL_PASSWORD={settings.ApplicationPassword}"
            ],
            DatabaseServerFamily.MariaDb =>
            [
                "-e", $"MARIADB_ROOT_PASSWORD={settings.AdminPassword}",
                "-e", "MARIADB_ROOT_HOST=%",
                "-e", $"MARIADB_DATABASE={settings.EmployeesDatabase}",
                "-e", $"MARIADB_USER={settings.ApplicationUser}",
                "-e", $"MARIADB_PASSWORD={settings.ApplicationPassword}"
            ],
            _ => throw new InvalidOperationException($"Unsupported database family '{target.Family}'.")
        });

        arguments.Add(target.Image);
        arguments.Add($"--max_connections={settings.ServerMaxConnections}");
        arguments.Add("--character-set-server=utf8mb4");
        arguments.Add("--collation-server=utf8mb4_unicode_ci");
        arguments.Add("--skip-name-resolve");

        podman.Execute(arguments).ThrowIfFailed($"Failed to create container '{containerName}'.");
    }

    private void RefreshRuntimeState()
    {
        var runningServerTargets = TestCliCatalog.Targets
            .Where(x => x.ServerTarget is not null && ContainerRunning(settings.GetContainerName(x.ServerTarget)))
            .ToArray();

        if (runningServerTargets.Length == 0)
        {
            stateStore.Delete();
            ConsoleSync.WriteLine("No Podman test containers are present.");
            return;
        }

        var host = PodmanHostResolver.Resolve(podman);
        stateStore.Save(stateStore.BuildState(settings, new CliTargetSelection(null, runningServerTargets), host));
    }

    private void PersistState(CliTargetSelection selection, string host)
    {
        stateStore.Save(stateStore.BuildState(settings, selection, host));
    }

    private string ResolveHost(CliTargetSelection selection)
    {
        if (selection.ServerTargets.Count == 0)
            return Environment.GetEnvironmentVariable("DATALINQ_TEST_DB_HOST") ?? "127.0.0.1";

        return PodmanHostResolver.Resolve(podman);
    }

    private void CleanupLegacyArtifacts(bool remove)
    {
        foreach (var legacyContainerName in new[]
        {
            $"{settings.ContainerPrefix}-mysql",
            $"{settings.ContainerPrefix}-mariadb"
        })
        {
            if (!ContainerExists(legacyContainerName))
                continue;

            if (remove)
            {
                RemoveContainerForcefully(legacyContainerName);
                continue;
            }

            if (ContainerRunning(legacyContainerName))
                podman.Execute(["stop", legacyContainerName]).ThrowIfFailed($"Failed to stop legacy container '{legacyContainerName}'.");
        }

        var legacyPod = settings.ContainerPrefix;
        var podExists = podman.Execute(["pod", "exists", legacyPod]);
        if (podExists.ExitCode == 0)
            podman.Execute(["pod", "rm", "-f", legacyPod]).ThrowIfFailed($"Failed to remove legacy pod '{legacyPod}'.");
    }

    private bool ContainerExists(string containerName) =>
        podman.Execute(["container", "exists", containerName]).ExitCode == 0;

    private bool ContainerRunning(string containerName)
    {
        var result = podman.Execute(["inspect", "--format", "{{.State.Running}}", containerName]);
        return result.ExitCode == 0 && result.StandardOutput.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private void ForceKillIfRunning(string containerName)
    {
        if (!ContainerRunning(containerName))
            return;

        ConsoleSync.WriteLine($"Killing container '{containerName}'...");
        var result = podman.Execute(["kill", containerName]);
        if (result.ExitCode == 0)
            return;

        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        ConsoleSync.WriteLine($"Warning: failed to kill container '{containerName}'. Continuing with forced removal. {detail}".TrimEnd());
    }

    private void RemoveContainerForcefully(string containerName)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ForceKillIfRunning(containerName);

            ConsoleSync.WriteLine($"Removing container '{containerName}'...");
            var result = podman.Execute(["rm", "-f", containerName]);
            if (result.ExitCode == 0 || !ContainerExists(containerName))
                return;

            if (attempt == maxAttempts)
                result.ThrowIfFailed($"Failed to remove container '{containerName}'.");

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    private void WaitForMySqlAdmin(string containerName, string password)
    {
        WaitUntil(() =>
        {
            var result = podman.Execute(["exec", containerName, "mysqladmin", "ping", "-h", "127.0.0.1", "-u", "root", $"-p{password}", "--silent"]);
            return result.ExitCode == 0;
        }, $"database readiness for container '{containerName}'");
    }

    private void WaitForMariaDbReady(string containerName)
    {
        WaitUntil(() =>
        {
            var result = podman.Execute(["exec", containerName, "healthcheck.sh", "--connect", "--innodb_initialized"]);
            return result.ExitCode == 0;
        }, $"database readiness for container '{containerName}'");
    }

    private void WaitForHostPort(string host, int port, string description)
    {
        WaitUntil(() =>
        {
            using var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(host, port);
                return connectTask.Wait(TimeSpan.FromSeconds(1)) && client.Connected;
            }
            catch
            {
                return false;
            }
        }, description);
    }

    private void InitializeMySqlHostAdminUser(string containerName)
    {
        var sql = BuildPrivilegeSql();
        podman.Execute(["exec", containerName, "mysql", "-h", "127.0.0.1", "-u", "root", $"-p{settings.AdminPassword}", "-e", sql])
            .ThrowIfFailed($"Failed to provision elevated privileges for host admin user '{settings.AdminUser}' in container '{containerName}'.");
    }

    private void InitializeMariaDbHostAdminUser(string containerName)
    {
        var sql = BuildPrivilegeSql();
        podman.Execute(["exec", containerName, "mariadb", "-h", "127.0.0.1", "-u", "root", $"-p{settings.AdminPassword}", "-e", sql])
            .ThrowIfFailed($"Failed to provision elevated privileges for host admin user '{settings.AdminUser}' in container '{containerName}'.");
    }

    private string BuildPrivilegeSql()
    {
        string[] hostPatterns = ["%", "localhost"];
        var statements = new List<string>();

        foreach (var hostPattern in hostPatterns)
        {
            statements.Add($"CREATE USER IF NOT EXISTS '{settings.ApplicationUser}'@'{hostPattern}' IDENTIFIED BY '{EscapeSqlLiteral(settings.ApplicationPassword)}';");
            statements.Add($"ALTER USER '{settings.ApplicationUser}'@'{hostPattern}' IDENTIFIED BY '{EscapeSqlLiteral(settings.ApplicationPassword)}';");
        }

        if (string.Equals(settings.AdminUser, settings.ApplicationUser, StringComparison.Ordinal))
        {
            foreach (var hostPattern in hostPatterns)
            {
                statements.Add($"GRANT ALL PRIVILEGES ON *.* TO '{settings.ApplicationUser}'@'{hostPattern}' WITH GRANT OPTION;");
            }
        }
        else
        {
            foreach (var hostPattern in hostPatterns)
            {
                statements.Add($"GRANT ALL PRIVILEGES ON *.* TO '{settings.ApplicationUser}'@'{hostPattern}';");
                statements.Add($"CREATE USER IF NOT EXISTS '{settings.AdminUser}'@'{hostPattern}' IDENTIFIED BY '{EscapeSqlLiteral(settings.AdminPassword)}';");
                statements.Add($"ALTER USER '{settings.AdminUser}'@'{hostPattern}' IDENTIFIED BY '{EscapeSqlLiteral(settings.AdminPassword)}';");
                statements.Add($"GRANT ALL PRIVILEGES ON *.* TO '{settings.AdminUser}'@'{hostPattern}' WITH GRANT OPTION;");
            }
        }

        statements.Add("FLUSH PRIVILEGES;");
        return string.Join(' ', statements);
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void WaitUntil(Func<bool> condition, string description, int timeoutSeconds = 90, int sleepMilliseconds = 1000)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(sleepMilliseconds);
        }

        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }
}
