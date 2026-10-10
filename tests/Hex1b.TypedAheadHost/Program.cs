using Hex1b;
using Hex1b.Widgets;

// Usage: Hex1b.TypedAheadHost <startup|handover> <directory>
//
// The parent test coordinates with this process through marker files in <directory>.
// Each console lifetime shows a text box and records what the text box received in
// result-<n>.

var scenario = args[0];
var directory = args[1];
var timeout = TimeSpan.FromSeconds(30);

try
{
    if (scenario == "startup")
    {
        // Still in cooked mode, no console lifetime yet: the parent types now.
        File.WriteAllText(Path.Combine(directory, "ready-1"), "");
        await WaitForFileAsync(Path.Combine(directory, "go-1"));
        await RunLifetimeAsync(1, stopWhenTyped: true);
    }
    else
    {
        await RunLifetimeAsync(1, stopWhenTyped: false);
        File.WriteAllText(Path.Combine(directory, "exited-1"), "");
        await WaitForFileAsync(Path.Combine(directory, "go-2"));
        await RunLifetimeAsync(2, stopWhenTyped: true);
    }

    return 0;
}
catch (Exception ex)
{
    File.WriteAllText(Path.Combine(directory, "error"), ex.ToString());
    return 1;
}

async Task WaitForFileAsync(string path)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (!File.Exists(path))
    {
        await Task.Delay(20, cancellation.Token);
    }
}

// Runs one console lifetime. With stopWhenTyped it ends once the text box holds "hello"
// (or after a few seconds); otherwise it ends when the parent creates stop-<n>.
async Task RunLifetimeAsync(int number, bool stopWhenTyped)
{
    var text = "";
    Hex1bApp app = null!;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    app = new Hex1bApp(ctx =>
    {
        File.WriteAllText(Path.Combine(directory, $"running-{number}"), "");
        return ctx.TextBox(text).OnTextChanged(change =>
        {
            text = change.NewText;
            if (stopWhenTyped && text == "hello")
            {
                app.RequestStop();
            }
        });
    });

    if (!stopWhenTyped)
    {
        _ = Task.Run(async () =>
        {
            await WaitForFileAsync(Path.Combine(directory, $"stop-{number}"));
            app.RequestStop();
        });
    }

    await using (app)
    {
        await app.RunAsync(deadline.Token);
    }

    File.WriteAllText(Path.Combine(directory, $"result-{number}"), text);
}
