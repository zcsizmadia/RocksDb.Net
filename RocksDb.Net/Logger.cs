using RocksDbNet.Native;
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
/// User-defined info logger for RocksDB. Override <see cref="Log"/> to
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
    private GCHandle _gcHandle;       // strong root → object stays alive while native holds it
    
    private readonly LoggerDelegate _loggerDelegate;

    // ── Static callbacks ─────────────────────────────────────────────────────
    // Using static methods avoids unsafe-lambda syntax issues.

    private static unsafe void LoggerCallback(
        nint state,
        int level,
        nint msg,
        uint msg_len)
    {
        var self = SelfFromState(state);
        var message = Marshal.PtrToStringUTF8(msg, (int)msg_len) ?? string.Empty;

        self.Log((InfoLogLevel)level, message);
    }

    private static Logger SelfFromState(nint state) => (Logger)GCHandle.FromIntPtr(state).Target!;

    // ── Construction ─────────────────────────────────────────────────────────

    protected Logger(InfoLogLevel logLevel)
    {
        // Pin this instance so that the C++ callbacks can access it via the state pointer
        _gcHandle = GCHandle.Alloc(this);

        _loggerDelegate = LoggerCallback;

        Handle = NativeMethods.rocksdb_logger_create_callback_logger(
            (int)logLevel,
            Marshal.GetFunctionPointerForDelegate(_loggerDelegate),
            GCHandle.ToIntPtr(_gcHandle));
    }

    // ── Abstract methods ───────────────────────────────────────────────

    /// <summary>
    /// Called by RocksDB to log a message at the specified level.
    /// </summary>
    /// <param name="logLevel">The severity level of the message.</param>
    /// <param name="message">The log message text.</param>
    public abstract void Log(InfoLogLevel logLevel, string message);


    // ── Disposal ─────────────────────────────────────────────────────────────

    public override void DisposeUnmanagedResources()
    {
        NativeMethods.rocksdb_logger_destroy(Handle);

        _gcHandle.Free();
    }
}