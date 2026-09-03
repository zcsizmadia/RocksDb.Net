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
    /// Handlers run on the thread that raised the exception, which is a RocksDb
    /// background thread for flush, compaction and backup callbacks, so handlers
    /// must be thread-safe. A handler that throws is ignored, to avoid replacing
    /// the original failure with a second one.
    /// </remarks>
    public static event EventHandler<CallbackExceptionEventArgs>? UnhandledException;

    /// <summary>
    /// Reports an exception that escaped a callback which can report failure to
    /// RocksDb, or whose result RocksDb does not depend on.
    /// </summary>
    internal static void Report(string callbackName, Exception exception)
        => Raise(new CallbackExceptionEventArgs(callbackName, exception, isFatal: false));

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
    internal static void ReportFatal(string callbackName, Exception exception)
    {
        Raise(new CallbackExceptionEventArgs(callbackName, exception, isFatal: true));

        Environment.FailFast(
            $"A RocksDb '{callbackName}' callback threw {exception.GetType().FullName}. " +
            "This callback has no way to report failure to RocksDb, and continuing would risk " +
            "corrupting the database, so the process is being terminated. " +
            "Handle exceptions inside the callback to avoid this.",
            exception);
    }

    private static void Raise(CallbackExceptionEventArgs args)
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
                handler(null, args);
            }
            catch
            {
                // A failing reporter must not mask the exception being reported,
                // nor propagate into native code itself.
            }
        }
    }
}
