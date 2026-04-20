using System.Runtime.InteropServices;

namespace RocksDbNet;

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
    public delegate void LoggerDelegate(
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
        var self = GetSelfFromPinnedIntPtr<Logger>(state);
        var message = Marshal.PtrToStringUTF8(msg, (int)msg_len) ?? string.Empty;

        self.Log((InfoLogLevel)level, message);
    }

    // ── Construction ─────────────────────────────────────────────────────────

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

    public override void DisposeHandle()
    {
        NativeMethods.rocksdb_logger_destroy(Handle);
    }

    public override void DisposeUnmanagedResources()
    {
        base.DisposeUnmanagedResources();

        // Logger has no destructor callback, so we must unpin here. 
        UnpinGarbageCollector();
    }
}