using Hex1b.Automation;
using Hex1b.Input;
using Hex1b.Widgets;

namespace Hex1b.Tests;

/// <summary>
/// Tests for WaitUntilTimeoutException diagnostics.
/// </summary>
[TestClass]
public class WaitUntilTimeoutTests
{
    [TestMethod]
    public async Task WaitUntil_Timeout_ThrowsWaitUntilTimeoutException()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("This text will never appear"), TimeSpan.FromMilliseconds(250))
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        // Should still be catchable as TimeoutException
        TestSeq.IsType<TimeoutException>(ex);
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_MessageContainsPredicateExpression()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("NonExistent"), TimeSpan.FromMilliseconds(250))
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        // CallerArgumentExpression should capture the predicate text
        Assert.Contains("ContainsText(\"NonExistent\")", ex.Message);
        Assert.Contains("ContainsText(\"NonExistent\")", ex.ConditionDescription);
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_MessageContainsExplicitDescription()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("Nope"), TimeSpan.FromMilliseconds(250), "my custom description")
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        // Explicit description should take priority over predicate expression
        Assert.Contains("my custom description", ex.Message);
        Assert.AreEqual("my custom description", ex.ConditionDescription);
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_MessageContainsCallerLocation()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("Nope"), TimeSpan.FromMilliseconds(250))
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        // Should contain the test file name and a line number
        Assert.Contains("WaitUntilTimeoutTests.cs", ex.Message);
        Assert.IsNotNull(ex.CallerFilePath);
        Assert.Contains("WaitUntilTimeoutTests.cs", ex.CallerFilePath);
        Assert.IsTrue(ex.CallerLineNumber > 0);
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_MessageContainsFullTerminalGrid()
    {
        using var workload = new Hex1bAppWorkloadAdapter();
        using var terminal = Hex1bTerminal.CreateBuilder()
            .WithWorkload(workload)
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        using var app = new Hex1bApp(
            ctx => Task.FromResult<Hex1bWidget>(new TextBlockWidget("Hello World")),
            new Hex1bAppOptions { WorkloadAdapter = workload }
        );

        var runTask = app.RunAsync(TestContext.Current.CancellationToken);

        // Wait for the app to render
        await new Hex1bTerminalInputSequenceBuilder()
            .WaitUntil(s => s.ContainsText("Hello World"), TimeSpan.FromSeconds(5))
            .Build()
            .ApplyAsync(terminal, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("Nope"), TimeSpan.FromMilliseconds(250))
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        // Message should contain the terminal content
        Assert.Contains("Hello World", ex.Message);
        // Message should contain terminal dimensions
        Assert.Contains("40x10", ex.Message);

        // Clean exit
        await new Hex1bTerminalInputSequenceBuilder()
            .Ctrl().Key(Hex1bKey.C)
            .Build()
            .ApplyAsync(terminal);
        await runTask;
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_ExposesTerminalSnapshot()
    {
        using var workload = new Hex1bAppWorkloadAdapter();
        using var terminal = Hex1bTerminal.CreateBuilder()
            .WithWorkload(workload)
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        using var app = new Hex1bApp(
            ctx => Task.FromResult<Hex1bWidget>(new TextBlockWidget("Snapshot Test")),
            new Hex1bAppOptions { WorkloadAdapter = workload }
        );

        var runTask = app.RunAsync(TestContext.Current.CancellationToken);

        // Wait for the app to render
        await new Hex1bTerminalInputSequenceBuilder()
            .WaitUntil(s => s.ContainsText("Snapshot Test"), TimeSpan.FromSeconds(5))
            .Build()
            .ApplyAsync(terminal, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("Nope"), TimeSpan.FromMilliseconds(250))
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        // Exception should expose the snapshot as a structured property
        Assert.IsNotNull(ex.TerminalSnapshot);
        Assert.AreEqual(40, ex.TerminalSnapshot.Width);
        Assert.AreEqual(10, ex.TerminalSnapshot.Height);
        Assert.IsTrue(ex.TerminalSnapshot.ContainsText("Snapshot Test"));

        // Clean exit
        await new Hex1bTerminalInputSequenceBuilder()
            .Ctrl().Key(Hex1bKey.C)
            .Build()
            .ApplyAsync(terminal);
        await runTask;
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_MessageContainsTimeoutDuration()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var timeout = TimeSpan.FromMilliseconds(250);
        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () =>
        {
            await new Hex1bTerminalInputSequenceBuilder()
                .WaitUntil(s => s.ContainsText("Nope"), timeout)
                .Build()
                .ApplyAsync(terminal, TestContext.Current.CancellationToken);
        });

        Assert.Contains(timeout.ToString(), ex.Message);
        Assert.AreEqual(timeout, ex.Timeout);
    }

    [TestMethod]
    public async Task WaitUntil_WallClockJumpsForward_KeepsWaiting()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var time = new SkewedTimeProvider();
        var options = new Hex1bTerminalInputSequenceOptions { TimeProvider = time };
        var automator = new Hex1bTerminalAutomator(terminal, options, TimeSpan.FromSeconds(30));

        var wait = automator.WaitUntilAsync(_ => false, TimeSpan.FromSeconds(30), "never");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // A wall-clock step (NTP correction, resume from sleep) must not end the wait.
        time.WallSkew = TimeSpan.FromSeconds(29);
        await Task.Delay(750, TestContext.Current.CancellationToken);

        Assert.IsFalse(wait.IsCompleted, "A forward wall-clock jump ended the wait early.");

        // Advance the monotonic clock past the budget so the wait finishes.
        time.StampSkew = 31 * TimeProvider.System.TimestampFrequency;
        await Assert.ThrowsExactlyAsync<Hex1bAutomationException>(async () =>
            await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [TestMethod]
    public async Task WaitUntil_Timeout_MessageContainsElapsedTime()
    {
        await using var terminal = Hex1bTerminal.CreateBuilder()
            .WithHex1bApp(ctx => new TextBlockWidget("Hello"))
            .WithHeadless()
            .WithDimensions(40, 10)
            .Build();

        var time = new SkewedTimeProvider();
        var options = new Hex1bTerminalInputSequenceOptions { TimeProvider = time };
        var timeout = TimeSpan.FromSeconds(1);

        var sequence = new Hex1bTerminalInputSequenceBuilder()
            .WithOptions(options)
            .WaitUntil(_ => false, timeout, "never")
            .Build();
        var apply = sequence.ApplyAsync(terminal, TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        time.StampSkew = 2 * TimeProvider.System.TimestampFrequency;

        var ex = await Assert.ThrowsExactlyAsync<WaitUntilTimeoutException>(async () => await apply);

        // The wait ran past its budget, so the message reports what really passed next to the budget.
        Assert.Contains("timed out after 00:00:02.", ex.Message);
        Assert.Contains($"timeout {timeout}", ex.Message);
        Assert.AreEqual(timeout, ex.Timeout);
    }

    private sealed class SkewedTimeProvider : TimeProvider
    {
        public TimeSpan WallSkew;
        public long StampSkew;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + WallSkew;

        public override long GetTimestamp() => System.GetTimestamp() + StampSkew;
    }
}
