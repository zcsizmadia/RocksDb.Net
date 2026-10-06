using Microsoft.Extensions.Logging;

namespace RocksDbNet;

/// <summary>
/// Sends RocksDb's info log to a <see cref="Microsoft.Extensions.Logging.ILogger"/>, so it lands in
/// the same pipeline as the rest of the application's logging.
/// </summary>
/// <remarks>
/// <para>
/// Usually attached through <see cref="RocksDbLoggingExtensions.UseLogging"/>.
/// RocksDb's levels map as follows:
/// </para>
/// <list type="table">
/// <listheader><term>RocksDb</term><description><see cref="LogLevel"/></description></listheader>
/// <item><term><see cref="InfoLogLevel.Debug"/></term><description><see cref="LogLevel.Debug"/></description></item>
/// <item><term><see cref="InfoLogLevel.Info"/></term><description><see cref="LogLevel.Information"/></description></item>
/// <item><term><see cref="InfoLogLevel.Warn"/></term><description><see cref="LogLevel.Warning"/></description></item>
/// <item><term><see cref="InfoLogLevel.Error"/></term><description><see cref="LogLevel.Error"/></description></item>
/// <item><term><see cref="InfoLogLevel.Fatal"/></term><description><see cref="LogLevel.Critical"/></description></item>
/// <item><term><see cref="InfoLogLevel.Header"/></term><description><see cref="LogLevel.Debug"/></description></item>
/// </list>
/// <para>
/// Expect volume at <see cref="LogLevel.Information"/>. Every time a database
/// opens, RocksDb writes several hundred lines: its version, a summary of the
/// files it found and every option it was opened with. It writes them without
/// a level, so they arrive as <see cref="InfoLogLevel.Info"/> and cannot be
/// told apart from its other informational messages. In production, set the
/// category's minimum level to <see cref="LogLevel.Warning"/>, as with any
/// chatty component; the warnings and errors are what is worth keeping.
/// </para>
/// <para>
/// RocksDb fixes a logger's level when it is created, so the level here is
/// the lowest the <see cref="Microsoft.Extensions.Logging.ILogger"/> had enabled at that moment,
/// and RocksDb does not format messages below it. Lowering the configured
/// level later does not reach a database already open with it; pass a
/// minimum level to the constructor to choose it explicitly instead.
/// </para>
/// <para>
/// That native level only filters what RocksDb logs with a level, so the
/// unlevelled lines above get through it whatever was asked for (see issue
/// #129). The <see cref="Microsoft.Extensions.Logging.ILogger"/>'s own filtering then decides; with an
/// explicit minimum level, they are also dropped here when it is higher.
/// </para>
/// <para>
/// Messages are logged with the template <c>{RocksDbMessage}</c>, because
/// RocksDb's own text contains braces that would otherwise be read as
/// placeholders. Logging runs on RocksDb's background threads, as
/// <see cref="Microsoft.Extensions.Logging.ILogger"/> implementations expect; an exception from the
/// logger is reported through <see cref="RocksDbCallbacks.UnhandledException"/>
/// and the line dropped.
/// </para>
/// </remarks>
public sealed partial class MicrosoftExtensionsLogger : Logger
{
    private readonly ILogger _logger;

    // Set only when the caller chose the level. Without it, the ILogger's own
    // IsEnabled is the filter, checked by the generated logging method.
    private readonly LogLevel? _minimum;

    /// <param name="logger">Where to send the log.</param>
    /// <param name="minimumLevel">
    /// The lowest RocksDb level to pass on, or <see langword="null"/> for the
    /// lowest level <paramref name="logger"/> has enabled now.
    /// </param>
    public MicrosoftExtensionsLogger(ILogger logger, InfoLogLevel? minimumLevel = null)
        : this(logger ?? throw new ArgumentNullException(nameof(logger)),
               minimumLevel ?? LowestEnabled(logger), minimumLevel)
    {
    }

    private MicrosoftExtensionsLogger(ILogger logger, InfoLogLevel nativeLevel, InfoLogLevel? minimumLevel)
        : base(nativeLevel)
    {
        NativeLevel = nativeLevel;
        _logger = logger;
        _minimum = minimumLevel is { } level ? ToLogLevel(level) : null;
    }

    /// <summary>The level RocksDb was asked to log at.</summary>
    internal InfoLogLevel NativeLevel { get; }

    /// <summary>The <see cref="LogLevel"/> a RocksDb level is logged at.</summary>
    public static LogLevel ToLogLevel(InfoLogLevel level) => level switch
    {
        InfoLogLevel.Debug => LogLevel.Debug,
        InfoLogLevel.Info => LogLevel.Information,
        InfoLogLevel.Warn => LogLevel.Warning,
        InfoLogLevel.Error => LogLevel.Error,
        InfoLogLevel.Fatal => LogLevel.Critical,
        InfoLogLevel.Header => LogLevel.Debug,
        _ => LogLevel.Information,
    };

    public override void Log(InfoLogLevel logLevel, string message)
    {
        LogLevel level = ToLogLevel(logLevel);

        // Also filtered here, because RocksDb's own filtering misses the
        // lines it logs without a level.
        if (level < _minimum)
        {
            return;
        }

        LogMessage(_logger, level, message);
    }

    [LoggerMessage(EventId = 1, EventName = "RocksDb", Message = "{RocksDbMessage}")]
    private static partial void LogMessage(ILogger logger, LogLevel level, string rocksDbMessage);

    // The lowest RocksDb level whose mapped level is enabled. Header is left
    // out: RocksDb writes it whatever the level, so it says nothing about
    // where to set the threshold.
    private static InfoLogLevel LowestEnabled(ILogger logger)
    {
        foreach (InfoLogLevel level in (ReadOnlySpan<InfoLogLevel>)
                 [InfoLogLevel.Debug, InfoLogLevel.Info, InfoLogLevel.Warn, InfoLogLevel.Error, InfoLogLevel.Fatal])
        {
            if (logger.IsEnabled(ToLogLevel(level)))
            {
                return level;
            }
        }

        // Nothing enabled: the fewest messages RocksDb will produce.
        return InfoLogLevel.Fatal;
    }
}

/// <summary>Attaches RocksDb's info log to Microsoft.Extensions.Logging.</summary>
public static class RocksDbLoggingExtensions
{
    /// <summary>
    /// Sends the info log of a database opened with these options to a logger
    /// from <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="options">The options to attach the logger to.</param>
    /// <param name="loggerFactory">The factory to create the logger from.</param>
    /// <param name="categoryName">The logger's category.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    /// <remarks>
    /// The options take the logger over and release it after the last
    /// database opened with them closes. See <see cref="MicrosoftExtensionsLogger"/>
    /// for how levels map and when the level is decided.
    /// </remarks>
    public static DbOptions UseLogging(this DbOptions options, ILoggerFactory loggerFactory, string categoryName = "RocksDb")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentException.ThrowIfNullOrEmpty(categoryName);

        // The options take a hold of their own, so this reference can go at
        // once and the release waits for them.
        using var logger = new MicrosoftExtensionsLogger(loggerFactory.CreateLogger(categoryName));
        options.InfoLog = logger;

        return options;
    }
}
