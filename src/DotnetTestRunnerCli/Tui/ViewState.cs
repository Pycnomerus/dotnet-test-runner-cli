using DotnetTestRunnerCli.Models;

namespace DotnetTestRunnerCli.Tui;

public sealed class ViewState
{
    public TestNode Root { get; private set; }
    public List<TestNode> VisibleNodes { get; private set; } = [];
    public int CursorIndex { get; private set; } = 0;
    public int ScrollOffset { get; private set; } = 0;
    public int ViewportHeight { get; set; } = 20;

    private string? _filterQuery;

    public TestNode? CurrentNode => VisibleNodes.ElementAtOrDefault(CursorIndex);

    public ViewState(TestNode root)
    {
        Root = root;
        RebuildVisibleList();
    }

    public void Reset(TestNode newRoot)
    {
        Root = newRoot;
        CursorIndex = 0;
        ScrollOffset = 0;
        _filterQuery = null;
        RebuildVisibleList();
    }

    /// <summary>
    /// Sets the active filter query and rebuilds the visible list.
    /// Pass null to clear filtering and return to the normal tree view.
    /// </summary>
    public void ApplyFilter(string? query)
    {
        _filterQuery = query;
        RebuildVisibleList();
    }

    public void RebuildVisibleList()
    {
        var list = new List<TestNode>();
        CollectVisible(Root, list);
        VisibleNodes = list;

        // Clamp cursor in case tree shrank
        CursorIndex = Math.Clamp(CursorIndex, 0, Math.Max(0, VisibleNodes.Count - 1));
        EnsureCursorVisible();
    }

    private void CollectVisible(TestNode node, List<TestNode> list)
    {
        // In filter mode, skip entire subtrees with no matching descendants.
        if (_filterQuery != null && !HasAnyMatch(node, _filterQuery)) return;

        list.Add(node);

        // In filter mode force-expand every ancestor so all matches are reachable,
        // regardless of the node's saved IsExpanded state.
        if (node.IsExpanded || _filterQuery != null)
            foreach (var child in node.Children)
                CollectVisible(child, list);
    }

    private static bool HasAnyMatch(TestNode node, string query)
    {
        if (node.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            node.FullyQualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var child in node.Children)
            if (HasAnyMatch(child, query)) return true;
        return false;
    }

    public void MoveCursorDown(int delta = 1)
    {
        CursorIndex = Math.Min(CursorIndex + delta, VisibleNodes.Count - 1);
        EnsureCursorVisible();
    }

    public void MoveCursorUp(int delta = 1)
    {
        CursorIndex = Math.Max(CursorIndex - delta, 0);
        EnsureCursorVisible();
    }

    public void MoveCursorToTop()
    {
        CursorIndex = 0;
        EnsureCursorVisible();
    }

    public void MoveCursorToBottom()
    {
        CursorIndex = Math.Max(0, VisibleNodes.Count - 1);
        EnsureCursorVisible();
    }

    public void PageDown()
    {
        MoveCursorDown(Math.Max(1, ViewportHeight / 2));
    }

    public void PageUp()
    {
        MoveCursorUp(Math.Max(1, ViewportHeight / 2));
    }

    public void JumpTo(int index)
    {
        CursorIndex = Math.Clamp(index, 0, Math.Max(0, VisibleNodes.Count - 1));
        EnsureCursorVisible();
    }

    private void EnsureCursorVisible()
    {
        if (CursorIndex < ScrollOffset)
            ScrollOffset = CursorIndex;
        if (CursorIndex >= ScrollOffset + ViewportHeight)
            ScrollOffset = CursorIndex - ViewportHeight + 1;
        ScrollOffset = Math.Max(0, ScrollOffset);
    }
}
