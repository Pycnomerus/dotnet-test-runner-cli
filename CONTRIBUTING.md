# Contributing

Contributions are welcome. This document covers how to get started, what's expected, and how to submit changes.

## Getting started

**Prerequisites:** .NET 10 SDK, a Linux x64 machine (or WSL2 on Windows).

```sh
git clone https://github.com/<your-org>/dotnet-test-runner-cli
cd dotnet-test-runner-cli
dotnet build
```

To run the tool against a real solution during development:

```sh
dotnet run --project src/DotnetTestRunnerCli -- /path/to/some.sln
```

To produce the release binary:

```sh
dotnet publish src/DotnetTestRunnerCli/DotnetTestRunnerCli.csproj -c Release
```

## Project structure

```
src/
  DotnetTestRunnerCli/
    Models/          — TestNode, TestStatus, RunResult, TestNodeType
    Services/        — SolutionDiscoveryService, TestDiscoveryService, TestRunnerService
    Tui/             — TuiApp, TreeRenderer, ViewState, SearchState, InputHandler, InputAction
    Utilities/       — ProcessRunner, FullyQualifiedNameParser
```

The TUI runs on a poll-based render loop (50 ms tick). Rendering is done by direct ANSI console writes via Spectre.Console; there is no retained-mode widget tree.

## Making changes

- **Keep the scope small.** Fix one thing per PR. Avoid reformatting unrelated code.
- **No speculative abstractions.** Don't add helpers, interfaces, or configuration hooks for hypothetical future use.
- **No comments on unchanged code.** Only add a comment when the logic isn't self-evident.
- **Test manually** against a real solution with xUnit, NUnit, or MSTest tests before submitting. There are no automated tests in this repo yet — be the change.
- **Check the TUI renders correctly** at a few different terminal widths (80, 120, 160 columns).

## Submitting a pull request

1. Fork the repo and create a branch off `main`.
2. Make your changes and verify the build passes (`dotnet build`).
3. Open a PR with a clear title and a short description of *why* the change is needed, not just what it does.
4. Keep PRs focused — one concern per PR makes review faster and history cleaner.

## Reporting issues

Open a GitHub issue with:
- The version of the binary (or commit SHA if built from source).
- The .NET SDK version (`dotnet --version`).
- The test framework and version used in the solution.
- Steps to reproduce, including the error message or unexpected behaviour.
