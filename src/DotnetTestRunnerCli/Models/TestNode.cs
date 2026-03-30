namespace DotnetTestRunnerCli.Models;

public sealed class TestNode
{
    public required TestNodeType Type { get; init; }
    public required string DisplayName { get; init; }
    public required string FullyQualifiedName { get; init; }
    public string? ProjectPath { get; init; }
    public TestNode? Parent { get; init; }

    public List<TestNode> Children { get; } = [];
    public bool IsExpanded { get; set; } = true;
    public bool IsSelected { get; set; } = false;
    public TestStatus Status { get; set; } = TestStatus.NotRun;
    public bool IsSearchMatch { get; set; } = false;
    public string? ErrorMessage { get; set; } = null;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    public bool IsLeaf => Children.Count == 0;
    public bool HasChildren => Children.Count > 0;

    public IEnumerable<TestNode> GetAllTestDescendants()
    {
        if (Type == TestNodeType.Test)
        {
            yield return this;
            yield break;
        }
        foreach (var child in Children)
            foreach (var test in child.GetAllTestDescendants())
                yield return test;
    }

    public TestStatus ComputeAggregateStatus()
    {
        if (Type == TestNodeType.Test) return Status;
        if (Children.Count == 0) return Status;

        var childStatuses = Children.Select(c => c.ComputeAggregateStatus()).ToList();

        if (childStatuses.Any(s => s == TestStatus.Running)) return TestStatus.Running;
        if (childStatuses.Any(s => s == TestStatus.Failed)) return TestStatus.Failed;
        if (childStatuses.All(s => s == TestStatus.Passed)) return TestStatus.Passed;
        if (childStatuses.Any(s => s == TestStatus.NotRun)) return TestStatus.NotRun;
        return TestStatus.Skipped;
    }
}
