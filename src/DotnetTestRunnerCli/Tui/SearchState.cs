using DotnetTestRunnerCli.Models;

namespace DotnetTestRunnerCli.Tui;

public sealed class SearchState
{
    public bool IsActive { get; private set; } = false;
    public bool IsFiltered { get; private set; } = false;
    public string Query { get; private set; } = string.Empty;
    public List<int> MatchIndices { get; private set; } = [];
    public int CurrentMatchIndex { get; private set; } = -1;

    public bool HasQuery => Query.Length > 0;

    public void Activate()
    {
        IsActive = true;
    }

    public void AppendChar(char c)
    {
        Query += c;
    }

    public void RemoveLastChar()
    {
        if (Query.Length > 0)
            Query = Query[..^1];
    }

    public void ToggleFilter() => IsFiltered = !IsFiltered;

    public void Deactivate(bool clearQuery)
    {
        IsActive = false;
        if (clearQuery)
        {
            Query = string.Empty;
            MatchIndices.Clear();
            CurrentMatchIndex = -1;
            IsFiltered = false;
        }
    }

    public void UpdateMatches(IReadOnlyList<TestNode> visibleNodes)
    {
        MatchIndices.Clear();
        CurrentMatchIndex = -1;

        foreach (var node in visibleNodes)
            node.IsSearchMatch = false;

        if (Query.Length == 0) return;

        for (var i = 0; i < visibleNodes.Count; i++)
        {
            var node = visibleNodes[i];
            if (node.DisplayName.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || node.FullyQualifiedName.Contains(Query, StringComparison.OrdinalIgnoreCase))
            {
                node.IsSearchMatch = true;
                MatchIndices.Add(i);
            }
        }

        if (MatchIndices.Count > 0)
            CurrentMatchIndex = 0;
    }

    public int? AdvanceToNextMatch()
    {
        if (MatchIndices.Count == 0) return null;
        CurrentMatchIndex = (CurrentMatchIndex + 1) % MatchIndices.Count;
        return MatchIndices[CurrentMatchIndex];
    }

    public int? AdvanceToPrevMatch()
    {
        if (MatchIndices.Count == 0) return null;
        CurrentMatchIndex = (CurrentMatchIndex - 1 + MatchIndices.Count) % MatchIndices.Count;
        return MatchIndices[CurrentMatchIndex];
    }
}
