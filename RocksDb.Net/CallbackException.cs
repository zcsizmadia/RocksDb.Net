using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Describes an exception that escaped a managed callback invoked by native
/// RocksDb code.
/// </summary>
public sealed class CallbackExceptionEventArgs(string callbackName, Exception exception, bool isFatal)
    : EventArgs
{
    /// <summary>The name of the callback that threw, for example <c>Filter</c> or <c>OnFlushCompleted</c>.</summary>
    public string CallbackName { get; } = callbackName;

    /// <summary>The exception that escaped the callback.</summary>
    public Exception Exception { get; } = exception;

    /// <summary>
    /// <c>true</c> when the callback has no way to report failure to RocksDb, so
    /// continuing would risk silently corrupting the database. The process is
    /// terminated after the event handlers run.
    /// </summary>
    public bool IsFatal { get; } = isFatal;
}

/// <summary>
/// Process-wide reporting for exceptions that escape managed RocksDb callbacks.
/// </summary>
/// <remarks>
/// <para>
/// A managed exception must never propagate into native code: the runtime treats
/// that as unrecoverable and terminates the process. Every callback this library
/// installs therefore catches exceptions at the native boundary and reports them
/// here instead.
/// </para>
/// <para>
/// Subscribe to <see cref="UnhandledException"/> to log or surface these. Without
/// a subscriber the exception is not observable, which is why non-fatal callbacks
/// also fall back to a defined no-op behaviour rather than failing silently in a
/// way that changes data.
/// </para>
/// </remarks>
public static class RocksDbCallbacks
{
    /// <summary>
    /// Raised when a managed callback invoked by RocksDb throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sender is the wrapper instance whose callback threw, for example the
    /// <see cref="CompactionFilter"/> or <see cref="EventListener"/> that was
    /// installed. Use it to tell several installed wrappers apart, since the
    /// callback name alone does not identify which one failed. It is
    /// <see langword="null"/> only when the instance cannot be identified, which
    /// happens when resolving it is itself what failed.
    /// </para>
    /// <para>
    /// Handlers run on the thread that raised the exception, which is a RocksDb
    /// background thread for flush, compaction and backup callbacks, so handlers
    /// must be thread-safe. A handler that throws is ignored, to avoid replacing
    /// the original failure with a second one.
    /// </para>
    /// </remarks>
    public static event EventHandler<CallbackExceptionEventArgs>? UnhandledException;

    /// <summary>
    /// Reports an exception that escaped a callback which can report failure to
    /// RocksDb, or whose result RocksDb does not depend on.
    /// </summary>
    /// <param name="callbackName">Name of the callback that threw.</param>
    /// <param name="exception">The exception that escaped it.</param>
    /// <param name="state">
    /// The callback pinned state pointer, used to identify the instance that
    /// threw. Leave unset when the caller has no state pointer.
    /// </param>
    internal static void Report(string callbackName, Exception exception, nint state = 0)
        => Raise(new CallbackExceptionEventArgs(callbackName, exception, isFatal: false), TryResolveSource(state));

    /// <summary>
    /// Reports an exception that escaped a callback with no failure channel, then
    /// terminates the process.
    /// </summary>
    /// <remarks>
    /// A comparator that cannot answer has no safe answer: returning an arbitrary
    /// ordering corrupts the on-disk key order and every later read of it. Failing
    /// fast with a clear message is worse than working code and better than silent
    /// corruption or an undiagnosable native crash.
    /// </remarks>
    internal static void ReportFatal(string callbackName, Exception exception, nint state = 0)
    {
        Raise(new CallbackExceptionEventArgs(callbackName, exception, isFatal: true), TryResolveSource(state));

        Environment.FailFast(
            $"A RocksDb '{callbackName}' callback threw {exception.GetType().FullName}. " +
            "This callback has no way to report failure to RocksDb, and continuing would risk " +
            "corrupting the database, so the process is being terminated. " +
            "Handle exceptions inside the callback to avoid this.",
            exception);
    }

    /// <summary>
    /// Recovers the wrapper instance behind a callback pinned state pointer, so
    /// the event can name what threw.
    /// </summary>
    /// <remarks>
    /// Best effort by design. This runs while a failure is already being handled,
    /// so a state pointer that no longer resolves yields <see langword="null"/>
    /// rather than replacing the original exception with a second one.
    /// </remarks>
    private static object? TryResolveSource(nint state)
    {
        if (state == nint.Zero)
        {
            return null;
        }

        try
        {
            GCHandle handle = GCHandle.FromIntPtr(state);
            return handle.IsAllocated ? handle.Target : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Raise(CallbackExceptionEventArgs args, object? source)
    {
        EventHandler<CallbackExceptionEventArgs>? handlers = UnhandledException;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<CallbackExceptionEventArgs> handler in handlers.GetInvocationList().Cast<EventHandler<CallbackExceptionEventArgs>>())
        {
            try
            {
                handler(source, args);
            }
            catch
            {
                // A failing reporter must not mask the exception being reported,
                // nor propagate into native code itself.
            }
        }
    }
}
