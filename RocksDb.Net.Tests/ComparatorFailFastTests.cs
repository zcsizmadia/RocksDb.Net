using System.Diagnostics;

namespace RocksDbNet.Tests;

/// <summary>
/// A comparator that throws takes the process down on purpose, so covering that
/// path means watching a process die.
/// </summary>
/// <remarks>
/// <para>
/// <c>Compare</c> is the one callback with no way to report failure and no safe
/// fallback: the ordering it returns is the order keys are written in, and every
/// later read depends on it, so a made-up answer corrupts the database rather
/// than failing an operation. The wrapper calls
/// <see cref="Environment.FailFast"/> instead.
/// </para>
/// <para>
/// It was the only path in the library with no coverage at all, for the obvious
/// reason. This runs the crash in a child process: the crashing half is gated
/// behind an environment variable so it never runs in an ordinary suite, and the
/// parent asserts the child died rather than returned, and died saying why.
/// </para>
/// </remarks>
public class ComparatorFailFastTests
{
    private const string ChildSwitch = "ROCKSDBNET_TEST_COMPARATOR_FAILFAST";

    private sealed class ThrowingComparator : Comparator
    {
        public ThrowingComparator()
            : base("throwing-comparator")
        {
        }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
            => throw new InvalidOperationException("the comparator cannot answer");
    }

    /// <summary>
    /// The child half. Does nothing unless the switch is set, which only the
    /// test below sets.
    /// </summary>
    [Fact]
    public void ComparatorThatThrows_TerminatesTheProcess()
    {
        if (Environment.GetEnvironmentVariable(ChildSwitch) != "1")
        {
            return;
        }

        using var dir = new TempDir();

        var comparator = new ThrowingComparator();

        var options = new DbOptions { CreateIfMissing = true };
        options.Comparator = comparator;

        using var db = RocksDb.Open(options, dir.Path);

        // Two keys, so RocksDb has to order them and must ask the comparator.
        db.Put("a", "1");
        db.Put("b", "2");
        db.Flush();

        // Unreachable: the process is gone by now. If it is ever reached, the
        // parent sees this text and fails.
        Assert.Fail("the comparator threw and the process survived");
    }

    [Fact]
    public async Task ComparatorThatThrows_KillsTheProcessRatherThanReturning()
    {
        string assembly = typeof(ComparatorFailFastTests).Assembly.Location;

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // The built assembly rather than the project, so the child runs the tests
        // already built rather than building them again.
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(assembly);
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(
            $"FullyQualifiedName={typeof(ComparatorFailFastTests).FullName}.{nameof(ComparatorThatThrows_TerminatesTheProcess)}");

        startInfo.Environment[ChildSwitch] = "1";

        using Process child = Process.Start(startInfo)!;

        // Both pipes read at once. Draining one to the end before starting the
        // other deadlocks as soon as the child fills the buffer of the one not
        // being read, which is what a crashing child does.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        Task<string> standardOutput = child.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> standardError = child.StandardError.ReadToEndAsync(timeout.Token);

        await child.WaitForExitAsync(timeout.Token);

        string output = await standardOutput + await standardError;

        Assert.NotEqual(0, child.ExitCode);

        // The message names the callback, the exception and why continuing is not
        // an option, which is the whole value of failing fast rather than letting
        // the native side crash somewhere unrelated later.
        Assert.Contains("Compare", output, StringComparison.Ordinal);
        Assert.Contains("no way to report failure", output, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), output, StringComparison.Ordinal);

        // Not an ordinary assertion failure: the run never got far enough to
        // report one.
        Assert.DoesNotContain("the comparator threw and the process survived", output, StringComparison.Ordinal);
    }
}
