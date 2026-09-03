namespace RocksDbNet;

/// <summary>
/// How a manual compaction should treat the bottommost level, mapped from
/// <c>rocksdb::BottommostLevelCompaction</c> in <c>options.h</c>.
/// </summary>
/// <remarks>The values are positional in the native header.</remarks>
public enum BottommostLevelCompaction
{
    /// <summary>Never compact the bottommost level.</summary>
    Skip = 0,

    /// <summary>
    /// Compact the bottommost level only when a compaction filter is set. This
    /// is RocksDb's default. Like <see cref="ForceOptimized"/>, it avoids
    /// compacting a file twice within one manual compaction.
    /// </summary>
    IfHaveCompactionFilter = 1,

    /// <summary>Always compact the bottommost level.</summary>
    Force = 2,

    /// <summary>
    /// Always compact the bottommost level, but avoid compacting a file twice
    /// within the same compaction.
    /// </summary>
    ForceOptimized = 3,
}
