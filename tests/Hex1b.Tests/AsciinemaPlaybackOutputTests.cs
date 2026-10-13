using System.Text;
using System.Text.Json;

namespace Hex1b.Tests;

[TestClass]
public sealed class AsciinemaPlaybackOutputTests : IDisposable
{
    private readonly List<string> _temporaryFiles = [];

    [TestMethod]
    public async Task RunAsync_PlaybackOutputWithEmptyEvent_DrainsEveryVisibleEventBeforeCompleting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var presentation = new GatedPresentationAdapter();
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithAsciinemaPlayback(CreateCast("FIRST-PLAYBACK-OUTPUT", "", "FINAL-PLAYBACK-OUTPUT"))
            .WithPresentation(presentation)
            .WithDimensions(80, 10)
            .Build();

        var runTask = terminal.RunAsync(timeout.Token);
        try
        {
            await presentation.WriteStarted.Task.WaitAsync(timeout.Token);
            Assert.IsFalse(runTask.IsCompleted,
                "Playback completion must wait until queued output is applied and presented.");

            presentation.ReleaseWrite.TrySetResult();
            Assert.AreEqual(0, await runTask.WaitAsync(timeout.Token));

            Assert.Contains("FIRST-PLAYBACK-OUTPUT", presentation.Output.ToString());
            Assert.Contains("FINAL-PLAYBACK-OUTPUT", presentation.Output.ToString());
            using var snapshot = terminal.CreateSnapshot();
            Assert.IsTrue(snapshot.ContainsText("FIRST-PLAYBACK-OUTPUT"));
            Assert.IsTrue(snapshot.ContainsText("FINAL-PLAYBACK-OUTPUT"));
        }
        finally
        {
            presentation.ReleaseWrite.TrySetResult();
            await timeout.CancelAsync();
        }
    }

    [TestMethod]
    public async Task RunAsync_ZeroDelayPlayback_Completes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithAsciinemaPlayback(CreateCast("ZERO-DELAY-PLAYBACK"))
            .WithHeadless()
            .WithDimensions(80, 10)
            .Build();

        Assert.AreEqual(0, await terminal.RunAsync(timeout.Token));

        using var snapshot = terminal.CreateSnapshot();
        Assert.IsTrue(snapshot.ContainsText("ZERO-DELAY-PLAYBACK"));
    }

    [TestMethod]
    public async Task RunAsync_HeaderOnlyPlayback_Completes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithAsciinemaPlayback(CreateCast())
            .WithHeadless()
            .WithDimensions(80, 10)
            .Build();

        Assert.AreEqual(0, await terminal.RunAsync(timeout.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunAsync_WhileDrainingPlaybackOutput_PropagatesCancellationOrPumpFailure(bool failOutput)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var presentation = new GatedPresentationAdapter();
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithAsciinemaPlayback(CreateCast("BLOCKED-PLAYBACK-OUTPUT"))
            .WithPresentation(presentation)
            .WithDimensions(80, 10)
            .Build();

        var runTask = terminal.RunAsync(runCancellation.Token);
        try
        {
            await presentation.WriteStarted.Task.WaitAsync(timeout.Token);
            if (failOutput)
            {
                var error = new IOException("Synthetic playback output failure.");
                presentation.WriteError = error;
                presentation.ReleaseWrite.TrySetResult();
                var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                    () => runTask.WaitAsync(timeout.Token));
                Assert.AreSame(error, exception.InnerException);
                Assert.Contains("workload output pump", exception.Message);
            }
            else
            {
                await runCancellation.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(timeout.Token));
            }
        }
        finally
        {
            presentation.WriteError = null;
            presentation.ReleaseWrite.TrySetResult();
            await runCancellation.CancelAsync();
        }
    }

    public void Dispose()
    {
        foreach (var file in _temporaryFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    private string CreateCast(params string[] output)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"hex1b-playback-{Guid.NewGuid():N}.cast");
        _temporaryFiles.Add(filePath);
        var lines = new List<string>
        {
            "{\"version\":2,\"width\":80,\"height\":10}"
        };
        foreach (var item in output)
            lines.Add(JsonSerializer.Serialize(new object?[] { 0.0, "o", item }));
        File.WriteAllLines(filePath, lines);
        return filePath;
    }

    private sealed class GatedPresentationAdapter : IHex1bTerminalPresentationAdapter
    {
        private readonly HeadlessPresentationAdapter _headless = new(80, 10);

        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StringBuilder Output { get; } = new();
        public Exception? WriteError { get; set; }
        public int Width => _headless.Width;
        public int Height => _headless.Height;
        public TerminalCapabilities Capabilities => _headless.Capabilities;
        public event Action<int, int>? Resized { add { } remove { } }
        public event Action? Disconnected { add { } remove { } }

        public async ValueTask WriteOutputAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            WriteStarted.TrySetResult();
            await ReleaseWrite.Task.WaitAsync(ct);
            if (WriteError is { } error)
                throw error;
            Output.Append(Encoding.UTF8.GetString(data.Span));
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadInputAsync(CancellationToken ct = default)
            => _headless.ReadInputAsync(ct);
        public ValueTask FlushAsync(CancellationToken ct = default) => _headless.FlushAsync(ct);
        public ValueTask EnterRawModeAsync(CancellationToken ct = default) => _headless.EnterRawModeAsync(ct);
        public ValueTask ExitRawModeAsync(CancellationToken ct = default) => _headless.ExitRawModeAsync(ct);
        public (int Row, int Column) GetCursorPosition() => _headless.GetCursorPosition();
        public ValueTask DisposeAsync() => _headless.DisposeAsync();
    }
}
