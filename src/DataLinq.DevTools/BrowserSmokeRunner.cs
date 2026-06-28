using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace DataLinq.DevTools;

public static class BrowserSmokeRunner
{
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromMinutes(3);

    public static CompatibilityCommandReport Run(
        CompatibilityTargetDefinition target,
        string publishDirectory,
        string targetRoot,
        DevToolPaths paths)
    {
        var logPath = Path.Combine(targetRoot, "browser-smoke.log");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = RunAsync(target, publishDirectory).GetAwaiter().GetResult();
            stopwatch.Stop();

            File.WriteAllText(logPath, result.Log, Encoding.UTF8);

            if (result.Passed)
            {
                return new CompatibilityCommandReport(
                    CompatibilityCommandStatus.Succeeded,
                    0,
                    stopwatch.Elapsed.TotalSeconds,
                    logPath,
                    CompatibilityFailureClassification.None,
                    result.Summary);
            }

            var status = target.Kind == CompatibilityTargetKind.Wasm
                ? CompatibilityCommandStatus.Unsupported
                : CompatibilityCommandStatus.Failed;
            var classification = target.Kind == CompatibilityTargetKind.Wasm
                ? CompatibilityFailureClassification.UnsupportedNoAot
                : result.FailureClassification;

            return new CompatibilityCommandReport(
                status,
                null,
                stopwatch.Elapsed.TotalSeconds,
                logPath,
                classification,
                result.Summary);
        }
        catch (BrowserSmokeEnvironmentException exception)
        {
            stopwatch.Stop();
            File.WriteAllText(logPath, exception.ToString(), Encoding.UTF8);

            return new CompatibilityCommandReport(
                CompatibilityCommandStatus.Failed,
                null,
                stopwatch.Elapsed.TotalSeconds,
                logPath,
                exception.FailureClassification,
                exception.Message);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            File.WriteAllText(logPath, exception.ToString(), Encoding.UTF8);

            return new CompatibilityCommandReport(
                CompatibilityCommandStatus.Failed,
                null,
                stopwatch.Elapsed.TotalSeconds,
                logPath,
                CompatibilityFailureClassification.Unknown,
                exception.Message);
        }
    }

    private static async Task<BrowserSmokeRunResult> RunAsync(
        CompatibilityTargetDefinition target,
        string publishDirectory)
    {
        using var server = BrowserSmokeStaticServer.Start(publishDirectory);
        var browserPath = BrowserLocator.FindBrowserPath();
        var consoleMessages = new List<string>();
        var pageErrors = new List<string>();
        var snapshots = new List<BrowserSmokeSnapshot>();
        var stopwatch = Stopwatch.StartNew();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = browserPath
        });
        var page = await browser.NewPageAsync();
        page.Console += (_, message) => consoleMessages.Add($"{message.Type}: {message.Text}");
        page.PageError += (_, message) => pageErrors.Add(message);

        await page.GotoAsync(server.BaseUrl, new PageGotoOptions
        {
            Timeout = (float)SmokeTimeout.TotalMilliseconds,
            WaitUntil = WaitUntilState.Load
        });

        BrowserSmokeSnapshot? lastSnapshot = null;

        while (stopwatch.Elapsed < SmokeTimeout)
        {
            lastSnapshot = await CaptureSnapshot(page);
            snapshots.Add(lastSnapshot);

            if (string.Equals(lastSnapshot.Status, "passed", StringComparison.OrdinalIgnoreCase))
            {
                return BrowserSmokeRunResult.Success(
                    $"Browser smoke passed at '{lastSnapshot.Stage}'.",
                    BuildLog(
                        target,
                        publishDirectory,
                        server.BaseUrl,
                        browserPath,
                        stopwatch.Elapsed,
                        snapshots,
                        consoleMessages,
                        pageErrors));
            }

            if (string.Equals(lastSnapshot.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var classification = ClassifyBrowserFailure(consoleMessages, pageErrors, lastSnapshot);
                return BrowserSmokeRunResult.Failed(
                    classification,
                    $"Browser smoke failed at '{lastSnapshot.Stage}'.",
                    BuildLog(
                        target,
                        publishDirectory,
                        server.BaseUrl,
                        browserPath,
                        stopwatch.Elapsed,
                        snapshots,
                        consoleMessages,
                        pageErrors));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        var stage = lastSnapshot?.Stage ?? "unknown";
        return BrowserSmokeRunResult.Failed(
            CompatibilityFailureClassification.SdkOrWebAssemblyToolchain,
            $"Browser smoke timed out at '{stage}'.",
            BuildLog(
                target,
                publishDirectory,
                server.BaseUrl,
                browserPath,
                stopwatch.Elapsed,
                snapshots,
                consoleMessages,
                pageErrors));
    }

    private static CompatibilityFailureClassification ClassifyBrowserFailure(
        IReadOnlyCollection<string> consoleMessages,
        IReadOnlyCollection<string> pageErrors,
        BrowserSmokeSnapshot snapshot)
    {
        var combined = string.Join(
            Environment.NewLine,
            consoleMessages.Concat(pageErrors).Append(snapshot.Text).Append(snapshot.Stage));

        if (ContainsAny(
            combined,
            "MONO_WASM",
            "WebAssembly",
            "wasm",
            "function signature mismatch",
            "RuntimeError",
            "SQLitePCLRaw",
            "sqlite3_config",
            "sqlite3_db_config"))
        {
            return CompatibilityFailureClassification.SdkOrWebAssemblyToolchain;
        }

        return CompatibilityFailureClassification.Unknown;
    }

    private static async Task<BrowserSmokeSnapshot> CaptureSnapshot(IPage page)
    {
        const string expression =
            """
            () => {
                const boot = document.getElementById("boot-status");
                const smoke = document.querySelector("[data-datalinq-smoke-status]");
                const smokeStatus = smoke && smoke.getAttribute("data-datalinq-smoke-status");
                const bootStatus = boot && boot.dataset && boot.dataset.status;
                const status = bootStatus === "failed"
                    ? "failed"
                    : smokeStatus ||
                    bootStatus ||
                    "running";
                const smokeStage = smoke && smoke.getAttribute("data-datalinq-smoke-stage");
                const stage = status === "failed"
                    ? ((boot && boot.textContent) || smokeStage || document.readyState || "failed")
                    : smokeStage ||
                    (boot && boot.textContent) ||
                    document.readyState ||
                    "starting";
                const logs = Array.isArray(window.datalinqLog)
                    ? window.datalinqLog.slice(-100).map(entry => `${entry.method}: ${entry.message}`)
                    : [];
                return JSON.stringify({
                    status,
                    stage,
                    text: document.body ? document.body.innerText : "",
                    logs
                });
            }
            """;

        try
        {
            var json = await page.EvaluateAsync<string>(expression);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new BrowserSmokeSnapshot(
                GetString(root, "status", "running"),
                GetString(root, "stage", "unknown"),
                GetString(root, "text", string.Empty),
                GetStringArray(root, "logs"));
        }
        catch (Exception exception) when (exception is PlaywrightException or JsonException)
        {
            return new BrowserSmokeSnapshot(
                "failed",
                "playwright-evaluate",
                exception.Message,
                []);
        }
    }

    private static string GetString(JsonElement root, string propertyName, string fallback)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return fallback;

        return value.GetString() ?? fallback;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        var entries = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
                entries.Add(text);
        }

        return entries;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string BuildLog(
        CompatibilityTargetDefinition target,
        string publishDirectory,
        string smokeUrl,
        string browserPath,
        TimeSpan duration,
        IReadOnlyList<BrowserSmokeSnapshot> snapshots,
        IReadOnlyCollection<string> consoleMessages,
        IReadOnlyCollection<string> pageErrors)
    {
        var last = snapshots.LastOrDefault();
        var builder = new StringBuilder();
        builder.AppendLine($"target={target.Name}");
        builder.AppendLine($"display={target.DisplayName}");
        builder.AppendLine($"publishDirectory={publishDirectory}");
        builder.AppendLine($"url={smokeUrl}");
        builder.AppendLine($"browser={browserPath}");
        builder.AppendLine($"durationSeconds={duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"status={last?.Status ?? "unknown"}");
        builder.AppendLine($"stage={last?.Stage ?? "unknown"}");
        builder.AppendLine();
        builder.AppendLine("Stages:");

        foreach (var snapshot in snapshots
                     .Select(static snapshot => $"{snapshot.Status}: {snapshot.Stage}")
                     .Distinct(StringComparer.Ordinal))
        {
            builder.AppendLine($"- {snapshot}");
        }

        if (last?.ConsoleLogs.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Window console log:");
            foreach (var entry in last.ConsoleLogs)
                builder.AppendLine($"- {entry}");
        }

        if (consoleMessages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Playwright console:");
            foreach (var entry in consoleMessages)
                builder.AppendLine($"- {entry}");
        }

        if (pageErrors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Page errors:");
            foreach (var entry in pageErrors)
                builder.AppendLine($"- {entry}");
        }

        if (!string.IsNullOrWhiteSpace(last?.Text))
        {
            builder.AppendLine();
            builder.AppendLine("DOM text:");
            builder.AppendLine(last.Text);
        }

        return builder.ToString();
    }

    private sealed record BrowserSmokeRunResult(
        bool Passed,
        CompatibilityFailureClassification FailureClassification,
        string Summary,
        string Log)
    {
        public static BrowserSmokeRunResult Success(string summary, string log) =>
            new(true, CompatibilityFailureClassification.None, summary, log);

        public static BrowserSmokeRunResult Failed(
            CompatibilityFailureClassification classification,
            string summary,
            string log) =>
            new(false, classification, summary, log);
    }

    private sealed record BrowserSmokeSnapshot(
        string Status,
        string Stage,
        string Text,
        IReadOnlyList<string> ConsoleLogs);

    private sealed class BrowserSmokeStaticServer : IDisposable
    {
        private static readonly IReadOnlyDictionary<string, string> ContentTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".html"] = "text/html; charset=utf-8",
                [".css"] = "text/css; charset=utf-8",
                [".js"] = "text/javascript; charset=utf-8",
                [".json"] = "application/json; charset=utf-8",
                [".wasm"] = "application/wasm",
                [".dll"] = "application/octet-stream",
                [".dat"] = "application/octet-stream",
                [".pdb"] = "application/octet-stream",
                [".blat"] = "application/octet-stream"
            };

        private readonly HttpListener listener;
        private readonly string webRoot;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task acceptLoop;

        private BrowserSmokeStaticServer(HttpListener listener, string webRoot, string baseUrl)
        {
            this.listener = listener;
            this.webRoot = webRoot;
            BaseUrl = baseUrl;
            acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        public static BrowserSmokeStaticServer Start(string publishDirectory)
        {
            var webRoot = ResolveWebRoot(publishDirectory);
            var port = GetAvailablePort();
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            listener.Start();
            return new BrowserSmokeStaticServer(listener, webRoot, baseUrl);
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Close();

            try
            {
                acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best-effort shutdown for release tooling.
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(context, cancellation.Token), cancellation.Token);
            }
        }

        private async Task ServeAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var requestPath = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/");
                var filePath = ResolveRequestPath(requestPath);
                var originalFilePath = filePath;
                var contentEncoding = SelectContentEncoding(context.Request, filePath);

                if (contentEncoding is not null)
                    filePath += $".{contentEncoding}";

                if (!File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                var contentType = ResolveContentType(originalFilePath);
                var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                context.Response.StatusCode = 200;
                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = bytes.Length;
                context.Response.Headers["Cache-Control"] = "no-store";

                if (contentEncoding is not null)
                    context.Response.Headers["Content-Encoding"] = contentEncoding;

                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            }
            catch (Exception exception)
            {
                var bytes = Encoding.UTF8.GetBytes(exception.ToString());
                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            }
            finally
            {
                context.Response.Close();
            }
        }

        private string ResolveRequestPath(string requestPath)
        {
            var relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relativePath))
                relativePath = "index.html";

            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            var fullRoot = Path.GetFullPath(webRoot);
            if (!IsPathInsideOrEqual(fullRoot, fullPath))
                throw new InvalidOperationException($"Refusing to serve path outside web root: '{requestPath}'.");

            if (Directory.Exists(fullPath))
                fullPath = Path.Combine(fullPath, "index.html");

            if (!File.Exists(fullPath) && Path.GetExtension(fullPath).Length == 0)
                fullPath = Path.Combine(webRoot, "index.html");

            return fullPath;
        }

        private static bool IsPathInsideOrEqual(string root, string path)
        {
            var relativePath = Path.GetRelativePath(root, path);
            return relativePath == "." ||
                   (!relativePath.Equals("..", StringComparison.Ordinal) &&
                    !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !Path.IsPathRooted(relativePath));
        }

        private static string? SelectContentEncoding(HttpListenerRequest request, string filePath)
        {
            var acceptEncoding = request.Headers["Accept-Encoding"] ?? string.Empty;
            if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(filePath + ".br"))
            {
                return "br";
            }

            if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(filePath + ".gz"))
            {
                return "gzip";
            }

            return null;
        }

        private static string ResolveContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return ContentTypes.TryGetValue(extension, out var contentType)
                ? contentType
                : "application/octet-stream";
        }

        private static string ResolveWebRoot(string publishDirectory)
        {
            var wwwroot = Path.Combine(publishDirectory, "wwwroot");
            if (File.Exists(Path.Combine(wwwroot, "index.html")))
                return wwwroot;

            if (File.Exists(Path.Combine(publishDirectory, "index.html")))
                return publishDirectory;

            throw new DirectoryNotFoundException(
                $"Could not find a Blazor WebAssembly web root in '{publishDirectory}'.");
        }

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private static class BrowserLocator
    {
        public static string FindBrowserPath()
        {
            var configured = Environment.GetEnvironmentVariable("DATALINQ_BROWSER_PATH");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured))
                    return configured;

                throw new BrowserSmokeEnvironmentException(
                    CompatibilityFailureClassification.SdkOrWebAssemblyToolchain,
                    $"DATALINQ_BROWSER_PATH points to a missing browser executable: '{configured}'.");
            }

            foreach (var candidate in EnumerateBrowserCandidates())
            {
                if (Path.IsPathFullyQualified(candidate))
                {
                    if (File.Exists(candidate))
                        return candidate;
                }
                else
                {
                    var resolved = ResolveExecutableOnPath(candidate);
                    if (resolved is not null)
                        return resolved;
                }
            }

            throw new BrowserSmokeEnvironmentException(
                CompatibilityFailureClassification.SdkOrWebAssemblyToolchain,
                "Could not find a Chromium-compatible browser. Set DATALINQ_BROWSER_PATH to Edge, Chrome, or Chromium.");
        }

        private static IEnumerable<string> EnumerateBrowserCandidates()
        {
            if (OperatingSystem.IsWindows())
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                yield return Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe");
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
                yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
                yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
            }

            yield return "google-chrome";
            yield return "chrome";
            yield return "chromium";
            yield return "chromium-browser";
            yield return "microsoft-edge";
            yield return "msedge";
        }

        private static string? ResolveExecutableOnPath(string executableName)
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var extensions = OperatingSystem.IsWindows()
                ? new[] { ".exe", ".cmd", ".bat", string.Empty }
                : new[] { string.Empty };

            foreach (var path in paths)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(path, executableName + extension);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }
    }

    private sealed class BrowserSmokeEnvironmentException : Exception
    {
        public BrowserSmokeEnvironmentException(
            CompatibilityFailureClassification failureClassification,
            string message)
            : base(message)
        {
            FailureClassification = failureClassification;
        }

        public CompatibilityFailureClassification FailureClassification { get; }
    }
}
