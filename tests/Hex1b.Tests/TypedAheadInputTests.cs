using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace Hex1b.Tests;

/// <summary>
/// Keys typed while an application starts, or while one console lifetime hands the
/// terminal to the next, must reach the application. These tests run Hex1b in a child
/// process whose stdin and stdout are a real PTY, because the console driver always
/// uses the process' own file descriptors 0 and 1. The child is Hex1b.TypedAheadHost
/// rather than this test host, which swallows stdin on its own.
/// </summary>
[TestClass]
[TestCategory("Unix")]
[DoNotParallelize]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public class TypedAheadInputTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task ConsoleLifetime_InputQueuedBeforeStart_ArrivesAsKeyEvents()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Requires a Unix PTY.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory("hex1b-typeahead-");
        try
        {
            using var cancellation = new CancellationTokenSource(Timeout);
            await using var child = StartHost("startup", directory.FullName);
            await child.StartAsync(cancellation.Token);
            var output = DrainOutputAsync(child, cancellation.Token);

            // The child is up but still in cooked mode and has not started a lifetime: the
            // line discipline queues what is typed here.
            await WaitForFileAsync(Path.Combine(directory.FullName, "ready-1"), cancellation.Token);
            await child.WriteInputAsync(Encoding.ASCII.GetBytes("hello"), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            File.WriteAllText(Path.Combine(directory.FullName, "go-1"), "");

            Assert.AreEqual(0, await child.WaitForExitAsync(cancellation.Token));
            await output;
            Assert.AreEqual("hello", File.ReadAllText(Path.Combine(directory.FullName, "result-1")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ConsoleLifetime_InputTypedBetweenLifetimes_ReachesNextLifetime()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Requires a Unix PTY.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory("hex1b-typeahead-");
        try
        {
            using var cancellation = new CancellationTokenSource(Timeout);
            await using var child = StartHost("handover", directory.FullName);
            await child.StartAsync(cancellation.Token);
            var output = DrainOutputAsync(child, cancellation.Token);

            await WaitForFileAsync(Path.Combine(directory.FullName, "running-1"), cancellation.Token);
            // Let the first lifetime finish starting, including its capability probe.
            await Task.Delay(500, cancellation.Token);
            File.WriteAllText(Path.Combine(directory.FullName, "stop-1"), "");
            await WaitForFileAsync(Path.Combine(directory.FullName, "exited-1"), cancellation.Token);

            // The first lifetime has left raw mode and not yet started the second one.
            await child.WriteInputAsync(Encoding.ASCII.GetBytes("hello"), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            File.WriteAllText(Path.Combine(directory.FullName, "go-2"), "");

            Assert.AreEqual(0, await child.WaitForExitAsync(cancellation.Token));
            await output;
            Assert.AreEqual("hello", File.ReadAllText(Path.Combine(directory.FullName, "result-2")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static Hex1bTerminalChildProcess StartHost(string scenario, string directory)
    {
        var host = typeof(TypedAheadInputTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "TypedAheadHostPath")
            .Value ?? throw new InvalidOperationException("The typed-ahead host path is unknown.");
        Assert.IsTrue(File.Exists(host), $"The typed-ahead host was not built: {host}");

        var dotnet = Environment.ProcessPath is { } path && Path.GetFileNameWithoutExtension(path) == "dotnet"
            ? path
            : "dotnet";
        return new Hex1bTerminalChildProcess(dotnet, [host, scenario, directory]);
    }

    private static async Task DrainOutputAsync(Hex1bTerminalChildProcess child, CancellationToken ct)
    {
        while (true)
        {
            var chunk = await child.ReadOutputAsync(ct);
            if (chunk.IsEmpty)
            {
                return;
            }
        }
    }

    private static async Task WaitForFileAsync(string path, CancellationToken ct)
    {
        while (!File.Exists(path))
        {
            await Task.Delay(20, ct);
        }
    }
}
