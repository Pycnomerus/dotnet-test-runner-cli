using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetTestRunnerCli.Models;
using DotnetTestRunnerCli.Utilities;

namespace DotnetTestRunnerCli.Services;

public sealed class TestRunnerService
{
    // Case-insensitive: handles both VSTest "  Passed" and MTP "passed" / "failed"
    private static readonly Regex ResultLineRegex = new(
        @"^\s+(Passed|Failed|Skipped)\s+(.+?)\s+\[([^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DurationRegex = new(
        @"(\d+(?:[.,]\d+)?)\s*(ms|s)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task RunAsync(
        IReadOnlyList<TestNode> targets,
        Action<RunResult> onResult,
        bool noBuild = false,
        Action<string>? onRawLine = null,
        CancellationToken cancellationToken = default)
    {
        var byProject = new Dictionary<string, List<TestNode>>();
        foreach (var target in targets)
        {
            var projectPath = ResolveProjectPath(target);
            if (projectPath == null) continue;
            if (!byProject.TryGetValue(projectPath, out var group))
            {
                group = [];
                byProject[projectPath] = group;
            }
            group.Add(target);
        }

        foreach (var (projectPath, projectTargets) in byProject)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunProjectAsync(projectPath, projectTargets, onResult, noBuild, onRawLine, cancellationToken);
        }
    }

    private static string? ResolveProjectPath(TestNode node)
    {
        var cur = node;
        while (cur != null)
        {
            if (cur.ProjectPath != null) return cur.ProjectPath;
            cur = cur.Parent;
        }
        return null;
    }

    private static async Task RunProjectAsync(
        string projectPath,
        List<TestNode> targets,
        Action<RunResult> onResult,
        bool noBuild,
        Action<string>? onRawLine,
        CancellationToken cancellationToken)
    {
        var filter     = BuildFilterExpression(targets);
        var filterArg  = string.IsNullOrEmpty(filter) ? "" : $" --filter \"{filter}\"";
        var noBuildArg = noBuild ? " --no-build" : "";

        // Write TRX to a temp file — reliable structured results across all runners
        var tmpTrx = Path.Combine(Path.GetTempPath(), $"dtrc_{Guid.NewGuid():N}.trx");

        var args = $"test \"{projectPath}\"{filterArg}" +
                   $" --logger \"console;verbosity=normal\"" +
                   $" --logger \"trx;LogFileName={tmpTrx}\"" +
                   $"{noBuildArg} --nologo";

        // Emit each result immediately on match — no buffering.
        // Error messages aren't available from console output anyway; TRX provides them.
        try
        {
            await ProcessRunner.RunAndStreamAsync("dotnet", args, async line =>
            {
                onRawLine?.Invoke(line);
                var m = ResultLineRegex.Match(line);
                if (m.Success)
                {
                    var status = m.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "passed"  => TestStatus.Passed,
                        "failed"  => TestStatus.Failed,
                        "skipped" => TestStatus.Skipped,
                        _         => TestStatus.NotRun
                    };
                    onResult(new RunResult(m.Groups[2].Value.Trim(), status, null, ParseDuration(m.Groups[3].Value)));
                }
                await Task.CompletedTask;
            }, cancellationToken: cancellationToken);
        }
        finally
        {
            // TRX pass: fills in anything the console parser missed AND adds error messages.
            // ApplyResult in TuiApp uses wasRunning to avoid double-counting.
            foreach (var result in ParseTrxResults(tmpTrx))
                onResult(result);

            try { File.Delete(tmpTrx); } catch { }
        }
    }

    // ── TRX parsing ────────────────────────────────────────────────────────

    private static IEnumerable<RunResult> ParseTrxResults(string trxPath)
    {
        if (!File.Exists(trxPath)) yield break;

        XDocument doc;
        try { doc = XDocument.Load(trxPath); }
        catch { yield break; }

        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        // Build testId → fully-qualified name from TestDefinitions.
        // UnitTest/@name carries the full display name including parameter values for
        // parameterized tests (e.g. "Add(1,2,3)"), while TestMethod/@name is just "Add".
        // We combine className (fully-qualified) from TestMethod with UnitTest/@name so
        // the result FQN matches the per-variant nodes built during discovery.
        var fqnById = doc
            .Descendants(ns + "UnitTest")
            .Where(e => e.Attribute("id") != null)
            .ToDictionary(
                e => e.Attribute("id")!.Value,
                e =>
                {
                    var tm           = e.Element(ns + "TestMethod");
                    var className    = tm?.Attribute("className")?.Value ?? "";
                    var unitTestName = e.Attribute("name")?.Value ?? "";
                    // UnitTest/@name already contains the full FQN (className.methodName(params))
                    // for both plain and Theory tests. Only prepend className when it is absent.
                    return string.IsNullOrEmpty(className) || unitTestName.StartsWith(className, StringComparison.OrdinalIgnoreCase)
                        ? unitTestName
                        : $"{className}.{unitTestName}";
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var r in doc.Descendants(ns + "UnitTestResult"))
        {
            var testId  = r.Attribute("testId")?.Value;
            var outcome = r.Attribute("outcome")?.Value;
            if (testId == null || outcome == null) continue;
            if (!fqnById.TryGetValue(testId, out var fqn)) continue;

            var status = outcome switch
            {
                "Passed"      => TestStatus.Passed,
                "Failed"      => TestStatus.Failed,
                "NotExecuted" => TestStatus.Skipped,
                _             => TestStatus.NotRun
            };

            TimeSpan.TryParse(r.Attribute("duration")?.Value, out var duration);

            var message    = r.Descendants(ns + "Message").FirstOrDefault()?.Value?.Trim();
            var stackTrace = r.Descendants(ns + "StackTrace").FirstOrDefault()?.Value?.Trim();
            var errorMsg   = (message, stackTrace) switch
            {
                (null,  null)  => null,
                (var m, null)  => m,
                (null,  var s) => s,
                (var m, var s) => $"{m}\n\n{s}"
            };

            yield return new RunResult(fqn, status, errorMsg, duration);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static TimeSpan ParseDuration(string bracket)
    {
        var m = DurationRegex.Match(bracket);
        if (!m.Success) return TimeSpan.Zero;
        var val = double.Parse(
            m.Groups[1].Value.Replace(',', '.'),
            System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value.Equals("s", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(val)
            : TimeSpan.FromMilliseconds(val);
    }

    internal static string BuildFilterExpression(IReadOnlyList<TestNode> nodes)
    {
        if (nodes.Count == 0) return string.Empty;

        var parts = nodes.Select(n =>
        {
            if (n.Type != TestNodeType.Test)
                return $"FullyQualifiedName~{n.FullyQualifiedName}";

            var parenIdx = n.FullyQualifiedName.IndexOf('(');
            if (parenIdx >= 0)
                // Theory variant: special chars in param list (parens, colons, quotes) break
                // MSBuild's filter parser. Exact-match on the stripped base name — the runner
                // resolves all parameterized cases automatically.
                return $"FullyQualifiedName={n.FullyQualifiedName[..parenIdx]}";

            return $"FullyQualifiedName={n.FullyQualifiedName}";
        }).ToList();

        return parts.Count == 1 ? parts[0] : $"({string.Join("|", parts)})";
    }
}
