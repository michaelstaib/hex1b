using System.Text;
using Hex1b.Input;
using Hex1b.Widgets;

namespace Hex1b.Tests;

/// <summary>
/// A frame that is rendered as a "first frame" must not assume a blank terminal unless
/// the screen was cleared. These tests replay the raw output bytes into a plain character
/// grid so that a glyph the app never overwrites stays visible.
/// </summary>
[TestClass]
public class Hex1bAppFullRepaintTests
{
    private const int Width = 20;
    private const int Height = 4;

    [TestMethod]
    public async Task RequestFullRepaint_CellBecameBlank_BlanksCellOnTerminal()
    {
        await RunScenarioAsync(
            trigger: (workload, _) =>
            {
                workload.RequestFullRepaint();
                return Task.CompletedTask;
            });
    }

    [TestMethod]
    public async Task Resize_FinalSizeEqualsCurrentSize_BlanksCellOnTerminal()
    {
        await RunScenarioAsync(
            trigger: (workload, _) =>
            {
                // The same observable result as a resize A, B, A drained in one loop pass:
                // the app sees a resize event while the adapter already reports the old size.
                Assert.IsTrue(workload.TryWriteInputEvent(new Hex1bResizeEvent(Width, Height)));
                return Task.CompletedTask;
            });
    }

    [TestMethod]
    public async Task RequestFullRepaint_CellStillWritten_RedrawsContent()
    {
        var grid = await RunScenarioCoreAsync(
            movedGlyph: false,
            trigger: (workload, _) =>
            {
                workload.RequestFullRepaint();
                return Task.CompletedTask;
            });

        Assert.AreEqual('X', grid[0, 0]);
        Assert.AreEqual('Y', grid[0, 1]);
    }

    private static async Task RunScenarioAsync(Func<Hex1bAppWorkloadAdapter, CancellationToken, Task> trigger)
    {
        var grid = await RunScenarioCoreAsync(movedGlyph: true, trigger);

        // Frame 1 wrote "XY" at the top-left. Frame 2 only writes "Y" at column 1, so column 0
        // is unwritten and must read as blank. Frame 1's "X" must not survive.
        Assert.AreEqual(' ', grid[0, 0], "The glyph from the first frame survived in a cell that became blank.");
        Assert.AreEqual('Y', grid[0, 1]);
    }

    private static async Task<char[,]> RunScenarioCoreAsync(
        bool movedGlyph,
        Func<Hex1bAppWorkloadAdapter, CancellationToken, Task> trigger)
    {
        using var workload = new Hex1bAppWorkloadAdapter();
        await workload.ResizeAsync(Width, Height);

        var showBoth = true;
        using var app = new Hex1bApp(
            ctx => Task.FromResult<Hex1bWidget>(
                showBoth
                    ? ctx.Text("XY")
                    : ctx.Padding(1, 0, 0, 0, p => p.Text("Y"))),
            new Hex1bAppOptions
            {
                WorkloadAdapter = workload,
                EnableInputCoalescing = false,
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var captured = new StringBuilder();
        var outputPump = PumpOutputAsync(workload, captured, cts.Token);
        var runTask = app.RunAsync(cts.Token);

        try
        {
            await WaitForAsync(captured, "XY", cts.Token);

            // The cell at column 0 held "X" in frame 1. From here on the app no longer writes it.
            // Both variants keep "Y" at column 1 so the redraw is observable.
            if (movedGlyph)
            {
                showBoth = false;
            }

            var before = Length(captured);
            await trigger(workload, cts.Token);
            await WaitForGrowthAsync(captured, before, cts.Token);
            await Task.Delay(100, cts.Token);
        }
        finally
        {
            app.RequestStop();
            await runTask;
            cts.Cancel();
            await outputPump;
        }

        string raw;
        lock (captured)
        {
            raw = captured.ToString();
        }

        return Replay(raw);
    }

    private static async Task PumpOutputAsync(Hex1bAppWorkloadAdapter workload, StringBuilder captured, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var data = await workload.ReadOutputAsync(ct);
                if (data.IsEmpty)
                {
                    if (ct.IsCancellationRequested)
                        return;
                    await Task.Delay(5, CancellationToken.None);
                    continue;
                }

                lock (captured)
                {
                    captured.Append(Encoding.UTF8.GetString(data.Span));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static int Length(StringBuilder captured)
    {
        lock (captured)
        {
            return captured.Length;
        }
    }

    private static async Task WaitForAsync(StringBuilder captured, string text, CancellationToken ct)
    {
        while (true)
        {
            lock (captured)
            {
                if (captured.ToString().Contains(text, StringComparison.Ordinal))
                    return;
            }

            await Task.Delay(10, ct);
        }
    }

    private static async Task WaitForGrowthAsync(StringBuilder captured, int previousLength, CancellationToken ct)
    {
        while (Length(captured) <= previousLength)
        {
            await Task.Delay(10, ct);
        }
    }

    /// <summary>
    /// Applies the subset of ANSI the renderer emits (CUP, ED 2, printable text, CR/LF) to a
    /// character grid. Everything else is ignored. A cell that was never written nor cleared stays '\0'.
    /// </summary>
    private static char[,] Replay(string raw)
    {
        var grid = new char[Height, Width];
        int row = 0, col = 0;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\x1b')
            {
                if (i + 1 < raw.Length && raw[i + 1] == '[')
                {
                    var start = i + 2;
                    var end = start;
                    while (end < raw.Length && raw[end] < '@')
                        end++;
                    if (end >= raw.Length)
                        break;

                    var parameters = raw[start..end];
                    var final = raw[end];
                    if (final == 'H' && !parameters.StartsWith('?'))
                    {
                        var parts = parameters.Split(';');
                        row = parts.Length > 0 && int.TryParse(parts[0], out var r) ? r - 1 : 0;
                        col = parts.Length > 1 && int.TryParse(parts[1], out var cc) ? cc - 1 : 0;
                    }
                    else if (final == 'J' && parameters == "2")
                    {
                        for (var y = 0; y < Height; y++)
                        {
                            for (var x = 0; x < Width; x++)
                                grid[y, x] = ' ';
                        }
                    }

                    i = end;
                }
                else if (i + 1 < raw.Length && (raw[i + 1] == ']' || raw[i + 1] == '_'))
                {
                    // OSC / APC strings end at BEL or ST.
                    var end = raw.IndexOfAny(['\a', '\\'], i + 2);
                    if (end < 0)
                        break;
                    i = end;
                }
                else
                {
                    i++;
                }

                continue;
            }

            if (c == '\r')
            {
                col = 0;
            }
            else if (c == '\n')
            {
                row++;
            }
            else if (c >= ' ' && row >= 0 && row < Height && col >= 0 && col < Width)
            {
                grid[row, col++] = c;
            }
        }

        return grid;
    }
}
