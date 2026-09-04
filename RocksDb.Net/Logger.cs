using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Severity threshold for RocksDb's own informational log. Records below
/// the configured level are not written.
/// </summary>
public enum InfoLogLevel : int
{
    /// <summary>
    /// Detailed logs for debugging the database engine.
    /// </summary>
    Debug = 0,

    /// <summary>
    /// General information about database lifecycle events (default).
    /// </summary>
    Info = 1,

    /// <summary>
    /// Important events that aren't errors, but may require attention.
    /// </summary>
    Warn = 2,

    /// <summary>
    /// Errors that occurred during operations (e.g., failed compactions).
    /// </summary>
    Error = 3,

    /// <summary>
    /// Critical failures that may lead to service interruption.
    /// </summary>
    Fatal = 4,

    /// <summary>
    /// Specialized logs used for printing database headers/configuration.
    /// </summary>
    Header = 5
}

/// <summary>
/// User-defined info logger for RocksDb. Override <see cref="Log"/> to
/// receive log messages from the database engine.
/// </summary>
public abstract class Logger : RocksDbHandle
{
    // ── Unmanaged delegate types ─────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestructorDelegate(nint state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LoggerDelegate(
        nint state,
        int level,
        nint msg,
        uint msg_len);

    // ── Instance state ───────────────────────────────────────────────────────

    private readonly LoggerDelegate _loggerDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static unsafe void LoggerCallback(
        nint state,
        int level,
        nint msg,
        uint msg_len)
    {
        try
        {
            var self = GetSelfFromPinnedIntPtr<Logger>(state);
            var message = Marshal.PtrToStringUTF8(msg, (int)msg_len) ?? string.Empty;

            self.Log((InfoLogLevel)level, message);
        }
        catch (Exception ex)
        {
            // A dropped log line is the mildest possible consequence, and RocksDb
            // does not check the outcome. Reporting a logging failure through the
            // logger would recurse, so it only goes to the callback event.
            RocksDbCallbacks.Report(nameof(Log), ex, state);
        }
    }

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>Creates a logger that asks RocksDb for messages at or above a level.</summary>
    /// <param name="logLevel">The lowest level this logger wants.</param>
    /// <remarks>
    /// A request rather than a guarantee. RocksDb logs a great deal through a
    /// call that carries no level, and those messages arrive tagged
    /// <see cref="InfoLogLevel.Info"/> whatever was asked for: measured over a
    /// database open, write and flush, a logger constructed at
    /// <see cref="InfoLogLevel.Warn"/> received 354 of them. Only the calls that
    /// do carry a level are filtered. A logger that must not see the rest has to
    /// check <c>logLevel</c> in its own <see cref="Log"/>.
    /// </remarks>
    protected Logger(InfoLogLevel logLevel)
    {
        PinGarbageCollector();

        _loggerDelegate = LoggerCallback;

        Handle = NativeMethods.rocksdb_logger_create_callback_logger(
            (int)logLevel,
            Marshal.GetFunctionPointerForDelegate(_loggerDelegate),
            GetPinnedIntPtr());
    }

    // ── Abstract methods ───────────────────────────────────────────────

    /// <summary>
    /// Called by RocksDb to log a message at the specified level.
    /// </summary>
    /// <param name="logLevel">The severity level of the message.</param>
    /// <param name="message">The log message text.</param>
    public abstract void Log(InfoLogLevel logLevel, string message);


    // ── Disposal ─────────────────────────────────────────────────────────────

    protected override void DisposeHandle()
    {
        NativeMethods.rocksdb_logger_destroy(Handle);
    }

    protected override void DisposeUnmanagedResources()
    {
        base.DisposeUnmanagedResources();

        // Logger has no destructor callback, so we must unpin here.
        //
        // Only if the pin was taken, because this runs on the finalizer path and
        // UnpinGarbageCollector throws when it was not. A derived constructor
        // that throws while evaluating the arguments it passes to base(...)
        // leaves an allocated, finalizable object whose base fields are all at
        // their defaults, so this constructor never ran and never pinned. The
        // finalizer still runs, and an exception from a finalizer is unhandled:
        // it terminated the process, arbitrarily later than the catch block that
        // appeared to have handled the failed construction. DisposeChildren
        // guards the same case for its own field.
        if (IsPinned)
        {
            UnpinGarbageCollector();
        }
    }
}