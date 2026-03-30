using System.Text.RegularExpressions;

namespace DotnetTestRunnerCli.Services;

public sealed class SolutionDiscoveryService
{
    private static readonly Regex ProjectLineRegex = new(
        @"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]+""\s*,\s*""([^""]+\.csproj)""",
        RegexOptions.Compiled);

    public string ResolveSolutionPath(string? explicitPath, string searchDirectory)
    {
        if (explicitPath != null)
        {
            var abs = Path.GetFullPath(explicitPath);
            if (!File.Exists(abs))
                throw new FileNotFoundException($"Solution file not found: {abs}");
            return abs;
        }

        var slnFiles = Directory.GetFiles(searchDirectory, "*.sln", SearchOption.TopDirectoryOnly);

        return slnFiles.Length switch
        {
            0 => throw new FileNotFoundException($"No .sln file found in: {searchDirectory}"),
            1 => Path.GetFullPath(slnFiles[0]),
            _ => throw new InvalidOperationException(
                $"Multiple .sln files found in {searchDirectory}. Specify one explicitly:\n" +
                string.Join("\n", slnFiles.Select(f => $"  {f}")))
        };
    }

    /// <summary>
    /// Returns all .csproj paths found in the solution. Test project filtering
    /// is left to TestDiscoveryService (which actually runs --list-tests to confirm).
    /// This avoids false negatives from projects that inherit test dependencies
    /// via Directory.Build.props or Central Package Management.
    /// </summary>
    public IReadOnlyList<string> ExtractProjectPaths(string solutionFilePath)
    {
        var slnDir = Path.GetDirectoryName(solutionFilePath)!;
        var slnContent = File.ReadAllText(solutionFilePath);
        var result = new List<string>();

        foreach (Match match in ProjectLineRegex.Matches(slnContent))
        {
            var relativePath = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            var absolutePath = Path.GetFullPath(Path.Combine(slnDir, relativePath));

            if (File.Exists(absolutePath))
                result.Add(absolutePath);
        }

        return result;
    }
}
