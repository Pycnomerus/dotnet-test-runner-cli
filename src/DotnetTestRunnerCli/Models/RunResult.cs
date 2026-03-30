namespace DotnetTestRunnerCli.Models;

public sealed record RunResult(
    string FullyQualifiedName,
    TestStatus Status,
    string? ErrorMessage,
    TimeSpan Duration
);
