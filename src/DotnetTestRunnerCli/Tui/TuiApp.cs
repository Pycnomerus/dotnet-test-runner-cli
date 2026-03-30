using DotnetTestRunnerCli.Models;
using DotnetTestRunnerCli.Services;
using DotnetTestRunnerCli.Utilities;
using Spectre.Console;

namespace DotnetTestRunnerCli.Tui;

public sealed class TuiApp
{
    private readonly ViewState _viewState;
    private readonly SearchState _searchState = new();
    private readonly TreeRenderer _renderer = new();
    private readonly InputHandler _inputHandler = new();
    private readonly TestRunnerService _runnerService;
    private readonly TestDiscoveryService _discoveryService;
    private readonly SolutionDiscoveryService _solutionDiscovery;
    private readonly string _solutionPath;

    private bool _isRunning = true;
    private volatile bool _dirty = true;
    private string _statusLine = BuildHintLine();

    private int _totalPassed = 0;
    private int _totalFailed = 0;
    private int _totalSkipped = 0;

    private int _detailScrollOffset  = 0;
    private int _detailHScrollOffset = 0;

    private CancellationTokenSource? _runCts;
    private bool _isTestRunning = false;
    private bool _isRefreshing = false;
    private bool _hasRunOnce = false;

    private readonly List<string> _outputLog = [];
    private const int OutputLogMaxLines = 500;

    public TuiApp(
        TestNode rootNode,
        TestRunnerService runnerService,
        TestDiscoveryService discoveryService,
        SolutionDiscoveryService solutionDiscovery,
        string solutionPath)
    {
        _viewState = new ViewState(rootNode);
        _runnerService = runnerService;
        _discoveryService = discoveryService;
        _solutionDiscovery = solutionDiscovery;
        _solutionPath = solutionPath;
    }

    public async Task RunAsync(CancellationToken appCancellationToken)
    {
        EnterAltScreen();
        Console.CursorVisible = false;

        try
        {
            while (_isRunning && !appCancellationToken.IsCancellationRequested)
            {
                if (_dirty)
                {
                    IReadOnlyList<string> logSnapshot;
                    lock (_outputLog) { logSnapshot = [.._outputLog]; }
                    _renderer.Render(_viewState, _searchState, _statusLine, _viewState.CurrentNode, ref _detailScrollOffset, ref _detailHScrollOffset, logSnapshot);
                    _dirty = false;
                }

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    await HandleInputAsync(_inputHandler.TranslateKey(key));
                }
                else
                {
                    await Task.Delay(50, appCancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _runCts?.Cancel();
            Console.CursorVisible = true;
            ExitAltScreen();
        }
    }

    private async Task HandleInputAsync(InputAction action)
    {
        switch (action)
        {
            case InputAction.MoveDown d:
                _viewState.MoveCursorDown(d.Delta);
                ResetDetailScroll();
                _dirty = true;
                break;

            case InputAction.MoveUp u:
                _viewState.MoveCursorUp(u.Delta);
                ResetDetailScroll();
                _dirty = true;
                break;

            case InputAction.MoveToTop:
                _viewState.MoveCursorToTop();
                ResetDetailScroll();
                _dirty = true;
                break;

            case InputAction.MoveToBottom:
                _viewState.MoveCursorToBottom();
                ResetDetailScroll();
                _dirty = true;
                break;

            case InputAction.PageDown:
                _viewState.PageDown();
                ResetDetailScroll();
                _dirty = true;
                break;

            case InputAction.PageUp:
                _viewState.PageUp();
                ResetDetailScroll();
                _dirty = true;
                break;

            case InputAction.ToggleExpand:
                if (_viewState.CurrentNode is { HasChildren: true } expandNode)
                    expandNode.IsExpanded = !expandNode.IsExpanded;
                _viewState.RebuildVisibleList();
                _searchState.UpdateMatches(_viewState.VisibleNodes);
                _dirty = true;
                break;

            case InputAction.Select:
                if (_viewState.CurrentNode is { } selNode)
                {
                    if (selNode.HasChildren)
                    {
                        selNode.IsExpanded = !selNode.IsExpanded;
                        _viewState.RebuildVisibleList();
                        _searchState.UpdateMatches(_viewState.VisibleNodes);
                    }
                    else
                    {
                        selNode.IsSelected = !selNode.IsSelected;
                    }
                }
                _dirty = true;
                break;

            case InputAction.Run:
                if (!_isTestRunning) await StartRunAsync();
                break;

            case InputAction.ActivateSearch:
                _searchState.Activate();
                _inputHandler.SetSearchMode(true);
                _statusLine = "/";
                _dirty = true;
                break;

            case InputAction.SearchChar c:
                _searchState.AppendChar(c.C);
                _searchState.UpdateMatches(_viewState.VisibleNodes);
                _statusLine = $"/{_searchState.Query}█";
                _dirty = true;
                break;

            case InputAction.SearchBackspace:
                _searchState.RemoveLastChar();
                _searchState.UpdateMatches(_viewState.VisibleNodes);
                _statusLine = _searchState.Query.Length > 0 ? $"/{_searchState.Query}█" : "/";
                _dirty = true;
                break;

            case InputAction.SearchConfirm:
                _searchState.Deactivate(clearQuery: false);
                _inputHandler.SetSearchMode(false);
                // Re-apply filter with the (possibly updated) query if filter mode was active.
                if (_searchState.IsFiltered)
                    _viewState.ApplyFilter(_searchState.HasQuery ? _searchState.Query : null);
                _searchState.UpdateMatches(_viewState.VisibleNodes);
                _statusLine = BuildQueryStatusLine();
                if (_searchState.MatchIndices.Count > 0)
                    _viewState.JumpTo(_searchState.MatchIndices[0]);
                _dirty = true;
                break;

            case InputAction.SearchCancel:
                _searchState.Deactivate(clearQuery: true); // also clears IsFiltered
                _inputHandler.SetSearchMode(false);
                _viewState.ApplyFilter(null);
                _searchState.UpdateMatches(_viewState.VisibleNodes);
                _statusLine = BuildHintLine();
                _dirty = true;
                break;

            case InputAction.NextMatch:
                if (_searchState.AdvanceToNextMatch() is { } nextIdx)
                {
                    _viewState.JumpTo(nextIdx);
                    _dirty = true;
                }
                break;

            case InputAction.PrevMatch:
                if (_searchState.AdvanceToPrevMatch() is { } prevIdx)
                {
                    _viewState.JumpTo(prevIdx);
                    _dirty = true;
                }
                break;

            case InputAction.DetailScrollDown:
                _detailScrollOffset++;
                _dirty = true;
                break;

            case InputAction.DetailScrollUp:
                _detailScrollOffset = Math.Max(0, _detailScrollOffset - 1);
                _dirty = true;
                break;

            case InputAction.DetailScrollRight:
                _detailHScrollOffset += 5;
                _dirty = true;
                break;

            case InputAction.DetailScrollLeft:
                _detailHScrollOffset = Math.Max(0, _detailHScrollOffset - 5);
                _dirty = true;
                break;

            case InputAction.ToggleFilter:
                if (_searchState.HasQuery)
                {
                    _searchState.ToggleFilter();
                    _viewState.ApplyFilter(_searchState.IsFiltered ? _searchState.Query : null);
                    _searchState.UpdateMatches(_viewState.VisibleNodes);
                    _statusLine = BuildQueryStatusLine();
                    _dirty = true;
                }
                break;

            case InputAction.Refresh:
                if (!_isTestRunning && !_isRefreshing)
                    _ = Task.Run(RefreshAsync);
                break;

            case InputAction.Quit:
                _isRunning = false;
                break;
        }
    }

    private async Task RefreshAsync()
    {
        _isRefreshing = true;
        _hasRunOnce = false; // next test run should build again

        // Step 1: build
        _statusLine = "[yellow]Building solution...[/]";
        _dirty = true;

        var (_, buildExit) = await ProcessRunner.RunAndCaptureAsync(
            "dotnet", $"build \"{_solutionPath}\" --nologo");

        if (buildExit != 0)
        {
            _statusLine = $"[red]Build failed.[/]  {BuildHintLine()}";
            _isRefreshing = false;
            _dirty = true;
            return;
        }

        // Step 2: rediscover tests
        _statusLine = "[yellow]Discovering tests...[/]";
        _dirty = true;

        var projectPaths = _solutionDiscovery.ExtractProjectPaths(_solutionPath);

        IReadOnlyDictionary<string, IReadOnlyList<string>> testsByProject;
        try
        {
            testsByProject = await _discoveryService.DiscoverTestsAsync(projectPaths);
        }
        catch (Exception ex)
        {
            _statusLine = $"[red]Discovery failed: {Markup.Escape(ex.Message)}[/]  {BuildHintLine()}";
            _isRefreshing = false;
            _dirty = true;
            return;
        }

        // Step 3: rebuild tree and reset view
        var solutionName = Path.GetFileNameWithoutExtension(_solutionPath);
        var newRoot = _discoveryService.BuildTree(solutionName, testsByProject);

        _viewState.Reset(newRoot);
        _searchState.Deactivate(clearQuery: true);
        _searchState.UpdateMatches(_viewState.VisibleNodes);
        _inputHandler.SetSearchMode(false);

        var total = testsByProject.Values.Sum(v => v.Count);
        _statusLine = $"[green]Refreshed — {total} test(s) found.[/]  {BuildHintLine()}";
        _isRefreshing = false;
        _dirty = true;
    }

    private async Task StartRunAsync()
    {
        // Priority: space-selected > active search matches > cursor node
        var selectedNodes = _viewState.VisibleNodes.Where(n => n.IsSelected).ToList();
        List<TestNode> targets;
        if (selectedNodes.Count > 0)
            targets = selectedNodes;
        else if (_searchState.HasQuery && _searchState.MatchIndices.Count > 0)
            targets = _searchState.MatchIndices.Select(i => _viewState.VisibleNodes[i]).ToList();
        else if (_viewState.CurrentNode != null)
            targets = [_viewState.CurrentNode];
        else
            targets = [];

        if (targets.Count == 0) return;

        _totalPassed = 0;
        _totalFailed = 0;
        _totalSkipped = 0;

        // Reset previous results on targeted tests
        foreach (var target in targets)
            foreach (var test in target.GetAllTestDescendants())
            {
                test.Status = TestStatus.Running;
                test.ErrorMessage = null;
                test.Duration = TimeSpan.Zero;
            }

        lock (_outputLog) { _outputLog.Clear(); }
        _isTestRunning = true;
        _statusLine = "[yellow]Running tests...[/]";
        _dirty = true;

        _runCts = new CancellationTokenSource();
        var cts = _runCts;
        var noBuild = _hasRunOnce;
        _hasRunOnce = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await _runnerService.RunAsync(targets, result =>
                {
                    ApplyResult(result);
                    _dirty = true;
                }, noBuild, line =>
                {
                    lock (_outputLog)
                    {
                        _outputLog.Add(line);
                        if (_outputLog.Count > OutputLogMaxLines)
                            _outputLog.RemoveAt(0);
                    }
                    _dirty = true;
                }, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _statusLine = $"[red]Error: {Markup.Escape(ex.Message)}[/]";
            }
            finally
            {
                _isTestRunning = false;
                _statusLine = BuildSummaryLine();
                _dirty = true;
            }
        }, cts.Token);

        await Task.CompletedTask;
    }

    private void ApplyResult(RunResult result)
    {
        var node = FindTestNode(_viewState.Root, result.FullyQualifiedName);
        if (node == null) return;

        var wasRunning = node.Status == TestStatus.Running;

        node.Status = result.Status;
        if (result.Duration > TimeSpan.Zero) node.Duration = result.Duration;
        if (result.ErrorMessage != null) node.ErrorMessage = result.ErrorMessage;

        // Only count the first transition out of Running — TRX may call this a second
        // time to add the error message, and we don't want to double-count.
        if (wasRunning)
        {
            switch (result.Status)
            {
                case TestStatus.Passed:  _totalPassed++;  break;
                case TestStatus.Failed:  _totalFailed++;  break;
                case TestStatus.Skipped: _totalSkipped++; break;
            }
        }
    }

    private static TestNode? FindTestNode(TestNode node, string fqn)
    {
        if (node.Type == TestNodeType.Test && node.FullyQualifiedName == fqn)
            return node;
        foreach (var child in node.Children)
        {
            var found = FindTestNode(child, fqn);
            if (found != null) return found;
        }
        return null;
    }

    private string BuildSummaryLine()
    {
        var parts = new List<string>();
        if (_totalPassed  > 0) parts.Add($"[green]✓ {_totalPassed}[/]");
        if (_totalFailed  > 0) parts.Add($"[red]✗ {_totalFailed}[/]");
        if (_totalSkipped > 0) parts.Add($"[blue]⊘ {_totalSkipped}[/]");
        var summary = parts.Count > 0 ? string.Join("  ", parts) : "[dim]No results[/]";
        return $"{summary}  {BuildHintLine()}";
    }

    private void ResetDetailScroll()
    {
        _detailScrollOffset  = 0;
        _detailHScrollOffset = 0;
    }

    private string BuildQueryStatusLine()
    {
        if (!_searchState.HasQuery) return BuildHintLine();
        var filterHint = _searchState.IsFiltered
            ? "  [yellow][filtered][/]  [dim]Tab[/]=unfilter"
            : "  [dim]Tab[/]=filter";
        return BuildHintLine($"  [dim]/[/][yellow]{_searchState.Query}[/]{filterHint}");
    }

    private static string BuildHintLine(string extra = "") =>
        $"[dim]q[/]uit [dim]r[/]un [dim]F5[/]refresh [dim]/[/]search [dim]e[/]xpand [dim]Space[/]sel [dim]j/k[/]↑↓ [dim][[[/]/[dim]]][/]↕ [dim]i[/]/[dim]o[/]↔{extra}";

    private static void EnterAltScreen()
    {
        Console.Write("\x1b[?1049h");
        Console.Write("\x1b[?7l");
        Console.SetCursorPosition(0, 0);
    }

    private static void ExitAltScreen()
    {
        Console.Write("\x1b[?7h");
        Console.Write("\x1b[?1049l");
    }
}
