using DotnetTestRunnerCli.Models;
using DotnetTestRunnerCli.Utilities;

namespace DotnetTestRunnerCli.Services;

public sealed class TestDiscoveryService
{
    // Packages that identify a test project — checked in csproj text and
    // Directory.Build.props/targets anywhere up the directory tree.
    private static readonly string[] TestProjectMarkers =
    [
        "Microsoft.NET.Test.Sdk",
        "IsTestProject",
        "xunit",
        "nunit",
        "MSTest",
        "TUnit",
        "Xunit",
    ];

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverTestsAsync(
        IReadOnlyList<string> projectPaths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Fast file-based pre-filter: skip projects that have no test markers
        // anywhere in their csproj or in any Directory.Build.* up the tree.
        // This is a pure file read — no process spawning — so it's instant.
        var candidates = projectPaths
            .Where(p => LooksLikeTestProject(p))
            .ToList();

        var tasks = candidates.Select(p => DiscoverProjectAsync(p, progress, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results
            .Where(r => r.fqns.Count > 0)
            .ToDictionary(r => r.path, r => r.fqns);
    }

    private static bool LooksLikeTestProject(string csprojPath)
    {
        // Check the csproj itself
        if (FileContainsAnyMarker(csprojPath)) return true;

        // Walk up the directory tree looking for Directory.Build.props/targets
        var dir = Path.GetDirectoryName(csprojPath);
        while (dir != null)
        {
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) && FileContainsAnyMarker(candidate))
                    return true;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break; // reached filesystem root
            dir = parent;
        }

        return false;
    }

    private static bool FileContainsAnyMarker(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            return TestProjectMarkers.Any(m => content.Contains(m, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static async Task<(string path, IReadOnlyList<string> fqns)> DiscoverProjectAsync(
        string projectPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        progress?.Report(projectName);

        // --no-build: assumes the solution is already built (standard before running tests).
        // Avoids recompiling every project during discovery — makes probing instant.
        var (lines, exitCode) = await ProcessRunner.RunAndCaptureAsync(
            "dotnet",
            $"test \"{projectPath}\" --list-tests --no-build --nologo",
            cancellationToken: cancellationToken);

        if (exitCode != 0)
            return (projectPath, Array.Empty<string>());

        var fqns = ParseListTestsOutput(lines);
        return (projectPath, fqns);
    }

    /// <summary>
    /// Handles two output formats:
    ///
    /// VSTest (xUnit v2, NUnit 3, MSTest):
    ///   The following Tests are available:
    ///       Namespace.Class.Method
    ///
    /// Microsoft Testing Platform / MTP (xUnit v3, TUnit, NUnit 4+):
    ///   No "following Tests" header — test names appear as indented lines.
    /// </summary>
    private static IReadOnlyList<string> ParseListTestsOutput(IReadOnlyList<string> lines)
    {
        // Try VSTest format first
        var vsTestFqns = ParseVsTestFormat(lines);
        if (vsTestFqns.Count > 0) return vsTestFqns;

        // Fallback: MTP format — indented lines that look like FQNs
        return ParseMtpFormat(lines);
    }

    private static List<string> ParseVsTestFormat(IReadOnlyList<string> lines)
    {
        var fqns = new List<string>();
        var inList = false;

        foreach (var line in lines)
        {
            if (!inList)
            {
                if (line.Contains("The following Tests are available", StringComparison.OrdinalIgnoreCase))
                    inList = true;
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (IsMetadataLine(trimmed)) continue;

            fqns.Add(trimmed);
        }

        return fqns;
    }

    private static List<string> ParseMtpFormat(IReadOnlyList<string> lines)
    {
        // MTP outputs test names as indented lines (1+ leading spaces)
        // that contain at least one dot and no spaces in the test name itself.
        var fqns = new List<string>();

        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] != ' ') continue;

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (IsMetadataLine(trimmed)) continue;

            // Must look like a FQN: at least one dot, no unescaped spaces in the core name
            // Allow trailing parameter lists like "ClassName.Method(arg1, arg2)"
            var namepart = trimmed.Contains('(') ? trimmed[..trimmed.IndexOf('(')] : trimmed;
            if (namepart.Contains('.') && !namepart.Contains(' '))
                fqns.Add(trimmed);
        }

        return fqns;
    }

    private static bool IsMetadataLine(string s) =>
        s.StartsWith("Test run for", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("A total of", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Build ", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Passed!", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("Failed!", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("VSTest", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("NuGet", StringComparison.OrdinalIgnoreCase);

    public TestNode BuildTree(
        string solutionName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> testsByProject)
    {
        var root = new TestNode
        {
            Type = TestNodeType.Solution,
            DisplayName = solutionName,
            FullyQualifiedName = solutionName
        };

        foreach (var (projectPath, fqns) in testsByProject)
        {
            var projectNode = new TestNode
            {
                Type = TestNodeType.Project,
                DisplayName = Path.GetFileNameWithoutExtension(projectPath),
                FullyQualifiedName = Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath = projectPath,
                Parent = root
            };

            BuildProjectSubtree(projectNode, fqns);
            CollapseNamespaceChains(projectNode);
            root.Children.Add(projectNode);
        }

        return root;
    }

    private static void BuildProjectSubtree(TestNode projectNode, IReadOnlyList<string> fqns)
    {
        // Flat dictionary keyed by full FQN prefix — acts as a path-compressed trie
        var nodeByFqn = new Dictionary<string, TestNode>(StringComparer.Ordinal);

        foreach (var fqn in fqns)
        {
            // Strip params only for structural parsing (namespace/class extraction).
            // The full FQN (with params) is preserved as the node identity so that each
            // parameterized test variant (InlineData, TestCase, Theory row) gets its own node.
            var clean = FullyQualifiedNameParser.StripParameters(fqn);
            var (ns, className, methodName) = FullyQualifiedNameParser.Parse(clean);

            // Build/reuse namespace nodes
            var parent = projectNode;
            if (!string.IsNullOrEmpty(ns))
            {
                var parts = ns.Split('.');
                var currentFqn = "";
                foreach (var part in parts)
                {
                    currentFqn = currentFqn.Length == 0 ? part : $"{currentFqn}.{part}";
                    if (!nodeByFqn.TryGetValue(currentFqn, out var nsNode))
                    {
                        nsNode = new TestNode
                        {
                            Type = TestNodeType.Namespace,
                            DisplayName = part,
                            FullyQualifiedName = currentFqn,
                            ProjectPath = projectNode.ProjectPath,
                            Parent = parent
                        };
                        parent.Children.Add(nsNode);
                        nodeByFqn[currentFqn] = nsNode;
                    }
                    parent = nsNode;
                }
            }

            // Build/reuse class node
            var classFqn = string.IsNullOrEmpty(ns) ? className : $"{ns}.{className}";
            if (!nodeByFqn.TryGetValue(classFqn, out var classNode))
            {
                classNode = new TestNode
                {
                    Type = TestNodeType.Class,
                    DisplayName = className,
                    FullyQualifiedName = classFqn,
                    ProjectPath = projectNode.ProjectPath,
                    Parent = parent
                };
                parent.Children.Add(classNode);
                nodeByFqn[classFqn] = classNode;
            }

            // Display name: method name + parameter list if present (e.g. "Add(1, 2, 3)")
            var parenIdx = fqn.IndexOf('(');
            var methodDisplay = parenIdx >= 0 ? methodName + fqn[parenIdx..] : methodName;

            // Test node: always new; FullyQualifiedName keeps full params for exact matching.
            classNode.Children.Add(new TestNode
            {
                Type = TestNodeType.Test,
                DisplayName = methodDisplay,
                FullyQualifiedName = fqn,
                ProjectPath = projectNode.ProjectPath,
                Parent = classNode
            });
        }
    }

    /// <summary>
    /// Compresses single-child namespace chains: A → B → C becomes "A.B.C".
    /// </summary>
    private static void CollapseNamespaceChains(TestNode node)
    {
        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];

            while (child.Type == TestNodeType.Namespace
                   && child.Children.Count == 1
                   && child.Children[0].Type == TestNodeType.Namespace)
            {
                var only = child.Children[0];
                var merged = new TestNode
                {
                    Type = TestNodeType.Namespace,
                    DisplayName = $"{child.DisplayName}.{only.DisplayName}",
                    FullyQualifiedName = only.FullyQualifiedName,
                    ProjectPath = child.ProjectPath,
                    Parent = node,
                    IsExpanded = child.IsExpanded
                };
                foreach (var gc in only.Children)
                    merged.Children.Add(gc);

                node.Children[i] = merged;
                child = merged;
            }

            CollapseNamespaceChains(child);
        }
    }
}
