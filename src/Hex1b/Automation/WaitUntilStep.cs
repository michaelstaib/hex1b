namespace Hex1b.Automation;

/// <summary>
/// A step that waits until a condition is met on the terminal.
/// </summary>
public sealed record WaitUntilStep(
    Func<Hex1bTerminalSnapshot, bool> Predicate,
    TimeSpan Timeout,
    string? Description = null,
    string? PredicateExpression = null,
    string? CallerFilePath = null,
    int? CallerLineNumber = null) : TestStep
{
    internal override async Task ExecuteAsync(
        Hex1bTerminal terminal,
        Hex1bTerminalInputSequenceOptions options,
        CancellationToken ct)
    {
        var timeProvider = options.TimeProvider ?? TimeProvider.System;
        var effectiveTimeout = Timeout;

        // Measure against the monotonic clock so a wall-clock step (NTP correction, sleep and
        // resume) neither ends the wait early nor extends it.
        var start = timeProvider.GetTimestamp();

        while (timeProvider.GetElapsedTime(start) < effectiveTimeout)
        {
            ct.ThrowIfCancellationRequested();

            // CreateSnapshot auto-flushes pending output
            using var snapshot = terminal.CreateSnapshot();

            if (Predicate(snapshot))
                return;

            await DelayAsync(timeProvider, options.PollInterval, ct);
        }

        // Timeout - capture final state for diagnostics
        var finalSnapshot = terminal.CreateSnapshot();
        var description = Description ?? PredicateExpression ?? "condition";
        throw new WaitUntilTimeoutException(
            effectiveTimeout,
            timeProvider.GetElapsedTime(start),
            description,
            finalSnapshot,
            CallerFilePath,
            CallerLineNumber);
    }
}
