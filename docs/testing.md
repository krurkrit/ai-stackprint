# Testing and code coverage

Run everything: `dotnet test -v q --nologo`. Services: `dotnet test tests/AiStackprint.Core.Tests`. UI tests run headless (Avalonia.Headless.XUnit, Skia): see `AGENTS.md`.

## Coverage

`pwsh build/coverage.ps1 [-Threshold 95]` runs every test with coverlet (`build/coverage.runsettings`: tests and generated code are left out), merges the reports with reportgenerator (`dotnet tool install -g dotnet-reportgenerator-globaltool`), prints the line and branch coverage and the classes with the most uncovered lines, writes `artifacts/coverage/index.html`, and exits 1 under the threshold.

| Date | Lines | Core | App (UI) |
|---|---|---|---|
| 2026-11-20 start | 75.4% | 89% | 61% |
| 2026-11-20 now | 87.4% | 93% | 81.5% |

The goal is 95%. What is left is mostly UI that needs a scenario: the log viewer's raw and update paths, the config editor, the limits cards (they need a fake provider), the model compare tab and the dialogs the window opens. Add a test for the class at the top of the script's list, then run it again.

## Seams: how a test replaces what it must not touch

Nothing in a test may read this machine's home folder, open a program or reach the network. The code reaches those through small, replaceable seams (no framework; each is `internal` and visible to the test projects):

| Depends on | Seam | A test does |
|---|---|---|
| The home folder (`~/.claude`, `~/.ai-stackprint`) | `PathUtil.UseHome(folder)`; `DataFolder.Root` follows it | `new FakeHome()` (`tests/AiStackprint.App.Tests/FakeHome.cs`): a made-up home with skills, commands, settings, MCP servers, a project and two sessions |
| The network (update check, model data, benchmark scores from Artificial Analysis, live limits) | `UpdateCheck.Http`, `ModelDataUpdate.Http`, `BenchmarkUpdate.Http`, `LimitsService.Http`; every provider's `Fetch(HttpClient, …)` takes the client | give it `new HttpClient(handler)` with a handler that answers from a function (`StubHandler` in Core.Tests, `FakeNetwork` in App.Tests) |
| Opening a file or folder, the operating system | `PathUtil.Launcher`, `PathUtil.OsOverride` | record the `ProcessStartInfo` instead of starting it; run the macOS and Linux branches on Windows |
| Cursor's database | `CursorLimits.DbOverride` | point it at a temp SQLite file |
| Running a plugin's CLI | `PluginManager.Run` runs a program from PATH | run `dotnet --version` |
| The whole window | `new MainWindow()` on a `FakeHome` | drive it: side menu buttons, Ctrl+K, F5, `Ui.ClickText` for a row |

Restore a replaced seam in `Dispose` (UI tests run one at a time).

## Screenshots

`pwsh build/test-and-capture.ps1 -Open` runs all tests and writes PNGs of every page and the main features, light and dark, with an `index.html` gallery (`ScreenshotTests`; they write only when `STACKPRINT_SHOTS` is set).
