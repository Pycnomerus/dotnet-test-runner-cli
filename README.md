# dotnet-test-runner-cli

A terminal UI for running and inspecting .NET tests. Navigates your solution tree, runs tests with live output, and lets you drill into stack traces — all without leaving the terminal.

## Installation

Download the latest `dotnet-test-runner-cli-*-linux-x64.tar.gz` from the [releases page](../../releases), extract it, and put the binary somewhere on your `PATH`:

```sh
tar -xzf dotnet-test-runner-cli-v1.0.0-linux-x64.tar.gz
mv dotnet-test-runner-cli ~/.local/bin/
```

The binary is self-contained — no .NET runtime required on the target machine.

## Usage

Run from your solution directory:

```sh
dotnet-test-runner-cli
```

Or pass a path to a `.sln` / `.slnx` file explicitly:

```sh
dotnet-test-runner-cli /path/to/MyApp.sln
```

The tool discovers all test projects in the solution, runs `dotnet test --list-tests` to enumerate tests, then opens the interactive TUI. Make sure the solution is built first (`dotnet build`).

## Interface

```
dotnet test runner  42/312  18 match(es)                          [filtered]
├─ MyApp                                                │ MyServiceTests
│  ├─ MyApp.Domain.Tests                                │ ──────────────────
│  │  └─ MyApp.Domain.Tests.Unit                        │ ✗ Failed  1.23 s
│  │     └─ Services                                    │ ──────────────────
│  │        └─ MyServiceTests                           │ Assert.Equal() fail
│  │           ├─ ✓  Constructor_SetsDefaults           │   Expected: 42
│  │           ├─ ✗  Process_WhenInvalid_Throws         │   Actual:   0
│  │           └─ ○  Add(x: 1, y: 2, expected: 3)      │
│  └─ MyApp.Web.Tests                                   │ ── Output ─────────
│     └─ ...                                            │ [xUnit] Starting...
```

The screen is split into three sections:

- **Left** — collapsible test tree (solution → project → namespace → class → test)
- **Top-right** — detail pane for the focused node: status, duration, and full stack trace
- **Bottom-right** — live output from the last test run

## Keybindings

### Navigation

| Key | Action |
|-----|--------|
| `j` / `k` or `↓` / `↑` | Move cursor down / up |
| `G` | Jump to bottom |
| `gg` | Jump to top |
| `Ctrl+D` / `Ctrl+U` | Page down / up |
| `e` | Expand or collapse the focused node |
| `Enter` or `Space` (on a group) | Expand / collapse |

### Running tests

| Key | Action |
|-----|--------|
| `r` or `Enter` (on a test/group) | Run focused node (or all selected/matched) |
| `Space` (on a leaf test) | Toggle selection mark |

**Run priority:** space-selected tests → active search matches → focused node.

After the first run the solution is not rebuilt (`--no-build`) for speed. Press `F5` to force a full rebuild and rediscover.

### Search & filter

| Key | Action |
|-----|--------|
| `/` | Open search prompt |
| *(type)* | Filter matches highlighted in yellow |
| `Enter` | Confirm search, exit search prompt |
| `Escape` | Cancel search and clear query |
| `n` / `N` | Jump to next / previous match |
| `Tab` | Toggle **filter mode** — hides all non-matching nodes |

Filter mode collapses the tree to show only nodes (and their ancestors) that match the current query. Press `Tab` again to return to the full tree. The header shows `[filtered]` when active.

### Detail pane scrolling

| Key | Action |
|-----|--------|
| `[` / `]` | Scroll detail pane up / down |
| `i` / `o` | Scroll detail pane left / right (5 chars/step) |

The separator line shows the current position (`1-10/47`) and horizontal offset (`→45`) when the content overflows. Both axes reset when moving to a different node.

### Other

| Key | Action |
|-----|--------|
| `F5` | Rebuild solution and rediscover tests |
| `q` or `Escape` | Quit |

## Test framework support

| Framework | Discovery | Results | Error messages |
|-----------|-----------|---------|----------------|
| xUnit v2 (VSTest) | ✓ | ✓ | ✓ |
| xUnit v3 (MTP) | ✓ | ✓ | ✓ |
| NUnit 3 (VSTest) | ✓ | ✓ | ✓ |
| MSTest (VSTest) | ✓ | ✓ | ✓ |

`[Theory]` / `[InlineData]`, `[TestCase]`, and `[DataRow]` parameterized tests are listed as individual nodes in the tree, each with its own pass/fail status.

## Building from source

```sh
dotnet publish src/DotnetTestRunnerCli/DotnetTestRunnerCli.csproj -c Release
```

Output is written to `src/DotnetTestRunnerCli/bin/Release/net10.0/linux-x64/publish/`.
