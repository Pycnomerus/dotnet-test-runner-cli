using DotnetTestRunnerCli.Services;
using DotnetTestRunnerCli.Tui;
using Spectre.Console;

var explicitSolutionPath = args.FirstOrDefault();
var searchDirectory = Directory.GetCurrentDirectory();

var solutionDiscovery = new SolutionDiscoveryService();

string solutionPath;
try
{
    solutionPath = solutionDiscovery.ResolveSolutionPath(explicitSolutionPath, searchDirectory);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
    return 1;
}

AnsiConsole.MarkupLine($"[dim]Solution:[/] [cyan]{solutionPath}[/]");

IReadOnlyList<string> testProjectPaths;
try
{
    testProjectPaths = solutionDiscovery.ExtractProjectPaths(solutionPath);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error reading solution:[/] {ex.Message}");
    return 1;
}

if (testProjectPaths.Count == 0)
{
    AnsiConsole.MarkupLine("[yellow]No test projects found in solution.[/]");
    return 1;
}

AnsiConsole.MarkupLine($"[dim]Found {testProjectPaths.Count} project(s) with test markers, probing...[/]");

var discoveryService = new TestDiscoveryService();
IReadOnlyDictionary<string, IReadOnlyList<string>>? testsByProject = null;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("Discovering tests...", async ctx =>
    {
        testsByProject = await discoveryService.DiscoverTestsAsync(
            testProjectPaths,
            new Progress<string>(msg => ctx.Status($"[dim]{msg}[/]")));
    });

if (testsByProject == null || testsByProject.Count == 0)
{
    AnsiConsole.MarkupLine("[yellow]No tests discovered. Make sure projects are built.[/]");
    AnsiConsole.MarkupLine("[dim]Tip: run [/][cyan]dotnet build[/][dim] first.[/]");
    return 1;
}

var totalTests = testsByProject.Values.Sum(v => v.Count);
AnsiConsole.MarkupLine($"[green]Discovered {totalTests} test(s)[/]");

var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
var rootNode = discoveryService.BuildTree(solutionName, testsByProject);

var runnerService = new TestRunnerService();
var app = new TuiApp(rootNode, runnerService, discoveryService, solutionDiscovery, solutionPath);

await app.RunAsync(CancellationToken.None);
return 0;
