using DotnetTestRunnerCli.Models;
using Spectre.Console;

namespace DotnetTestRunnerCli.Tui;

public sealed class TreeRenderer
{
    // Detail pane is 40% of terminal width, min 30 chars, max 60 chars.
    private static int DetailWidth(int termWidth) =>
        Math.Clamp((int)(termWidth * 0.40), 30, 60);

    public void Render(
        ViewState viewState, SearchState searchState,
        string statusLine, TestNode? detailNode,
        ref int detailScrollOffset,
        ref int detailHScrollOffset,
        IReadOnlyList<string>? outputLog = null)
    {
        var termWidth  = Console.WindowWidth;
        var termHeight = Console.WindowHeight;

        var detailW = DetailWidth(termWidth);
        var treeW   = termWidth - detailW - 1; // -1 for the separator column

        // Reserve: 1 header + viewport + 1 status
        var viewportHeight = Math.Max(1, termHeight - 2);
        viewState.ViewportHeight = viewportHeight;

        // Split right pane: detail on top, log on bottom.
        // Detail gets up to 1/3 of viewport (capped at actual detail lines); log gets the rest.
        var detailLines = BuildDetailLines(detailNode);
        var detailRows  = Math.Min(detailLines.Count, Math.Max(3, viewportHeight / 3));
        // Clamp vertical scroll.
        detailScrollOffset = Math.Clamp(detailScrollOffset, 0, Math.Max(0, detailLines.Count - detailRows));
        // Clamp horizontal scroll against the widest plain-text line.
        var maxLineWidth = 0;
        foreach (var dl in detailLines)
        {
            try { maxLineWidth = Math.Max(maxLineWidth, Markup.Remove(dl).Length); }
            catch { /* ignore lines with unparseable markup */ }
        }
        detailHScrollOffset = Math.Clamp(detailHScrollOffset, 0, Math.Max(0, maxLineWidth - detailW));
        var logSepRow   = detailRows; // 0-based within the right pane
        var logRows     = viewportHeight - detailRows - 1; // -1 for the separator line

        // ── Header (full width) ─────────────────────────────────────────
        WriteAt(0, 0, FormatHeader(viewState, searchState), termWidth);

        // ── Tree + Right pane (side by side) ────────────────────────────
        var visible = viewState.VisibleNodes;

        for (var i = 0; i < viewportHeight; i++)
        {
            var row     = i + 1;
            var nodeIdx = viewState.ScrollOffset + i;

            // Left: tree
            if (nodeIdx < visible.Count)
            {
                var node     = visible[nodeIdx];
                var isCursor = nodeIdx == viewState.CursorIndex;
                var line     = FormatNode(node, GetTreePrefix(node), isCursor, searchState);
                WriteClipped(row, 0, line, treeW, isCursor);
            }
            else
            {
                WriteAt(row, 0, "", treeW);
            }

            // Separator
            Console.SetCursorPosition(treeW, row);
            Console.Write('│');

            // Right: detail (top section) or separator or log (bottom section)
            if (i < detailRows)
            {
                var lineIdx = detailScrollOffset + i;
                if (lineIdx < detailLines.Count)
                {
                    var dl = detailLines[lineIdx];
                    if (dl == "─")
                    {
                        // Full-width separator — not affected by scroll.
                        WritePlain(row, treeW + 1, new string('─', detailW), detailW);
                    }
                    else if (detailHScrollOffset > 0)
                    {
                        // Horizontal scroll: strip markup, slice the plain text window.
                        string plain;
                        try { plain = Markup.Remove(dl); }
                        catch { plain = ""; }
                        var slice = plain.Length > detailHScrollOffset ? plain[detailHScrollOffset..] : "";
                        WritePlain(row, treeW + 1, slice, detailW);
                    }
                    else
                    {
                        WriteAt(row, treeW + 1, dl, detailW);
                    }
                }
                else
                {
                    WriteAt(row, treeW + 1, "", detailW);
                }
            }
            else if (i == logSepRow)
            {
                // Horizontal separator: show vertical scroll position + horizontal offset.
                var scrollable  = detailLines.Count > detailRows;
                var vScrollTag  = scrollable
                    ? $" {detailScrollOffset + 1}-{Math.Min(detailScrollOffset + detailRows, detailLines.Count)}/{detailLines.Count}"
                    : "";
                var hScrollTag  = detailHScrollOffset > 0 ? $" →{detailHScrollOffset}" : "";
                var scrollTag   = (vScrollTag + hScrollTag).Length > 0 ? vScrollTag + hScrollTag + " " : " ";
                var labelPlain  = $"─ Output{scrollTag}";
                var dashes      = new string('─', Math.Max(0, detailW - labelPlain.Length));
                WriteAt(row, treeW + 1, $"[dim]{dashes}─ Output{scrollTag}[/]", detailW);
            }
            else
            {
                // Log lines — show the tail of the log
                var logLineIdx = i - logSepRow - 1;
                var logOffset  = Math.Max(0, (outputLog?.Count ?? 0) - logRows);
                var absIdx     = logOffset + logLineIdx;
                var logContent = outputLog != null && absIdx >= 0 && absIdx < outputLog.Count
                    ? $"[dim]{Esc(TruncatePlain(outputLog[absIdx], detailW - 1))}[/]"
                    : "";
                WriteAt(row, treeW + 1, logContent, detailW);
            }
        }

        // ── Status (full width) ─────────────────────────────────────────
        WriteAt(termHeight - 1, 0, statusLine, termWidth);
    }

    // ── Detail pane ────────────────────────────────────────────────────

    private static List<string> BuildDetailLines(TestNode? node)
    {
        var lines = new List<string>();
        if (node == null) return lines;

        if (node.Type == TestNodeType.Test)
        {
            // Name + FQN — full content, renderer handles clipping/scrolling.
            lines.Add($"[bold]{Esc(node.DisplayName)}[/]");
            lines.Add($"[dim]{Esc(node.FullyQualifiedName)}[/]");
            lines.Add("─");

            // Status + duration
            var icon     = StatusIcon(node.Status);
            var statusTxt = node.Status.ToString();
            var durStr   = node.Duration > TimeSpan.Zero
                ? $" [dim]{FormatDuration(node.Duration)}[/]" : "";
            lines.Add($"{icon} {StatusColor(node.Status, statusTxt)}{durStr}");

            // Error message
            if (node.Status == TestStatus.Failed && node.ErrorMessage != null)
            {
                lines.Add("─");
                foreach (var errLine in node.ErrorMessage.Split('\n'))
                {
                    var trimmed = errLine.TrimEnd();
                    if (trimmed.Length == 0) continue;
                    lines.Add($"[dim]{Esc(trimmed)}[/]");
                }
            }
        }
        else
        {
            // Aggregate stats for non-test nodes
            lines.Add($"[bold]{Esc(node.DisplayName)}[/]");
            lines.Add("─");

            var all = node.GetAllTestDescendants().ToList();
            var passed  = all.Count(t => t.Status == TestStatus.Passed);
            var failed  = all.Count(t => t.Status == TestStatus.Failed);
            var running = all.Count(t => t.Status == TestStatus.Running);
            var skipped = all.Count(t => t.Status == TestStatus.Skipped);
            var notRun  = all.Count(t => t.Status == TestStatus.NotRun);

            lines.Add($"[dim]Tests: {all.Count} total[/]");
            if (passed  > 0) lines.Add($"  [green]✓ Passed:  {passed}[/]");
            if (failed  > 0) lines.Add($"  [red]✗ Failed:  {failed}[/]");
            if (running > 0) lines.Add($"  [yellow]⊙ Running: {running}[/]");
            if (skipped > 0) lines.Add($"  [blue]⊘ Skipped: {skipped}[/]");
            if (notRun  > 0) lines.Add($"  [dim]○ Not run: {notRun}[/]");
        }

        return lines;
    }

    private static string StatusIcon(TestStatus s) => s switch
    {
        TestStatus.Passed  => "[green]✓[/]",
        TestStatus.Failed  => "[red]✗[/]",
        TestStatus.Running => "[yellow]⊙[/]",
        TestStatus.Skipped => "[blue]⊘[/]",
        _                  => "[dim]○[/]"
    };

    private static string StatusColor(TestStatus s, string text) => s switch
    {
        TestStatus.Passed  => $"[green]{text}[/]",
        TestStatus.Failed  => $"[red]{text}[/]",
        TestStatus.Running => $"[yellow]{text}[/]",
        TestStatus.Skipped => $"[blue]{text}[/]",
        _                  => $"[dim]{text}[/]"
    };

    private static string FormatDuration(TimeSpan d) =>
        d.TotalSeconds >= 1 ? $"{d.TotalSeconds:F2} s" : $"{d.TotalMilliseconds:F0} ms";

    // ── Tree rendering ──────────────────────────────────────────────────

    private static string FormatHeader(ViewState viewState, SearchState searchState)
    {
        var total     = viewState.VisibleNodes.Count;
        var pos       = viewState.CursorIndex + 1;
        var matchInfo = searchState.HasQuery
            ? $"  [yellow]{searchState.MatchIndices.Count} match(es)[/]" : "";
        var filterTag = searchState is { HasQuery: true, IsFiltered: true }
            ? "  [bold yellow][filtered][/]" : "";
        return $"[bold]dotnet test runner[/]  [dim]{pos}/{total}[/]{matchInfo}{filterTag}";
    }

    private static string FormatNode(
        TestNode node, string prefix, bool isCursor, SearchState searchState)
    {
        var expandIcon = node.HasChildren ? (node.IsExpanded ? "▼" : "▶") : " ";
        var statusIcon = GetStatusIcon(node.ComputeAggregateStatus());
        var selectedMark = node.IsSelected ? "[bold yellow]*[/] " : "  ";

        var name = Esc(node.DisplayName);
        if (searchState.HasQuery && node.IsSearchMatch)
            name = $"[yellow]{name}[/]";

        var typeTag = node.Type == TestNodeType.Project ? " [dim italic](project)[/]" : "";
        var content = $"{Esc(prefix)}{expandIcon} {statusIcon} {selectedMark}{name}{typeTag}";

        return isCursor ? $"[bold on blue]{content}[/]" : content;
    }

    private static string GetStatusIcon(TestStatus status) => status switch
    {
        TestStatus.Passed  => "[green]✓[/]",
        TestStatus.Failed  => "[red]✗[/]",
        TestStatus.Running => "[yellow]⊙[/]",
        TestStatus.Skipped => "[blue]⊘[/]",
        _                  => "[dim]○[/]"
    };

    private static string GetTreePrefix(TestNode node)
    {
        if (node.Parent == null) return "";
        var ancestors = new List<TestNode>();
        var cur = node;
        while (cur.Parent != null) { ancestors.Add(cur); cur = cur.Parent; }
        ancestors.Reverse();

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < ancestors.Count; i++)
        {
            var isLast = IsLastChild(ancestors[i]);
            sb.Append(i == ancestors.Count - 1
                ? (isLast ? "└─ " : "├─ ")
                : (isLast ? "   " : "│  "));
        }
        return sb.ToString();
    }

    private static bool IsLastChild(TestNode node) =>
        node.Parent == null || node.Parent.Children[^1] == node;

    // ── Low-level output ────────────────────────────────────────────────

    /// Write plain text (no markup) at absolute (col, row), padded/truncated to exactWidth.
    private static void WritePlain(int row, int col, string text, int exactWidth)
    {
        if (row < 0 || row >= Console.WindowHeight) return;
        Console.SetCursorPosition(col, row);
        if (text.Length >= exactWidth)
            Console.Write(text[..exactWidth]);
        else
            Console.Write(text.PadRight(exactWidth));
    }

    /// Write markup at absolute (col, row), padded/truncated to exactWidth characters.
    private static void WriteAt(int row, int col, string markup, int exactWidth)
    {
        if (row < 0 || row >= Console.WindowHeight) return;
        Console.SetCursorPosition(col, row);
        var plain = Markup.Remove(markup);

        if (plain.Length > exactWidth)
        {
            // Content wider than column — strip markup and truncate to avoid overflow.
            Console.Write(plain[..exactWidth]);
            return;
        }

        var padLen = exactWidth - plain.Length;
        try
        {
            AnsiConsole.Markup(markup);
            if (padLen > 0) Console.Write(new string(' ', padLen));
        }
        catch
        {
            Console.Write(plain.PadRight(exactWidth));
        }
    }

    /// Write a tree node line clipped to treeWidth.
    /// Cursor rows get full-width highlight so we skip markup and use plain text clipping.
    private static void WriteClipped(int row, int col, string markup, int treeWidth, bool isCursor)
    {
        if (row < 0 || row >= Console.WindowHeight) return;
        Console.SetCursorPosition(col, row);
        var plain  = Markup.Remove(markup);
        var padLen = Math.Max(0, treeWidth - plain.Length);

        if (plain.Length > treeWidth)
        {
            // Clip: write plain text only, truncated
            if (isCursor)
            {
                // Keep the highlight colour even when clipped
                try { AnsiConsole.Markup($"[bold on blue]{Esc(plain[..treeWidth])}[/]"); }
                catch { Console.Write(plain[..treeWidth]); }
            }
            else
            {
                Console.Write(plain[..treeWidth]);
            }
        }
        else
        {
            try
            {
                AnsiConsole.Markup(markup);
                if (padLen > 0) Console.Write(new string(' ', padLen));
            }
            catch
            {
                Console.Write(plain.PadRight(treeWidth));
            }
        }
    }

    private static string Esc(string s) => s.Replace("[", "[[").Replace("]", "]]");

    private static string TruncatePlain(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
