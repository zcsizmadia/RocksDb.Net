using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The remaining API-shape items from issue #62.
/// </summary>
public class ApiShapeTests
{
    // ── FilterDecision.ChangeValue with an empty replacement ─────────────────

    private sealed class BlankingFilter : CompactionFilter
    {
        public BlankingFilter()
            : base("BlankingFilter")
        {
        }

        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            if (Encoding.UTF8.GetString(key).StartsWith("blank", StringComparison.Ordinal))
            {
                // An empty replacement. This used to be silently ignored, with
                // the old value kept, because the code required a positive
                // length.
                newValue = [];
                return FilterDecision.ChangeValue;
            }

            newValue = null;
            return FilterDecision.Keep;
        }
    }

    [Fact]
    public void ChangeValue_WithAnEmptyArray_ReplacesTheValue()
    {
        using var filter = new BlankingFilter();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

        db.Put("blank_one", "original");
        db.Put("keep_one", "original");

        db.Flush();
        db.CompactRange();

        // The key survives, since ChangeValue keeps it, but with an empty value
        // rather than the original.
        byte[]? blanked = db.Get("blank_one"u8.ToArray());
        Assert.NotNull(blanked);
        Assert.Empty(blanked);

        Assert.Equal("original", db.GetString("keep_one"));
    }

    private sealed class ReplacingFilter : CompactionFilter
    {
        public ReplacingFilter()
            : base("ReplacingFilter")
        {
        }

        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = "replaced"u8.ToArray();
            return FilterDecision.ChangeValue;
        }
    }

    /// <summary>
    /// A non-empty replacement still works, so the fix did not trade one case
    /// for the other.
    /// </summary>
    [Fact]
    public void ChangeValue_WithANonEmptyArray_StillReplacesTheValue()
    {
        using var filter = new ReplacingFilter();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key", "original");
        db.Flush();
        db.CompactRange();

        Assert.Equal("replaced", db.GetString("key"));
    }

    private sealed class NullReturningFilter : CompactionFilter
    {
        public NullReturningFilter()
            : base("NullReturningFilter")
        {
        }

        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            // ChangeValue with no value at all is a contradiction, so the entry
            // is left alone rather than blanked.
            newValue = null;
            return FilterDecision.ChangeValue;
        }
    }

    [Fact]
    public void ChangeValue_WithNull_LeavesTheValueAlone()
    {
        using var filter = new NullReturningFilter();

        var opts = new DbOptions { CreateIfMissing = true };
        opts.CompactionFilter = filter;

        using var db = TestDb.OpenInMemory(opts);

        db.Put("key", "original");
        db.Flush();
        db.CompactRange();

        Assert.Equal("original", db.GetString("key"));
    }

    // ── BackupEngine.AsEnumerable is read in full ───────────────────────────

    /// <summary>
    /// The native info snapshot no longer outlives the call, so an abandoned
    /// enumerator cannot leak it. Asserting the shape is what is checkable
    /// here; the leak itself was invisible from managed code.
    /// </summary>
    [Fact]
    public void BackupInfo_IsReadInFullRatherThanStreamed()
    {
        using var db = new TempDb();
        using var backupDir = new TempDir();

        db.Db.Put("k", "v");

        using var engine = BackupEngine.Open(db.Options, backupDir.Path);
        engine.CreateNewBackup(db.Db);
        engine.CreateNewBackup(db.Db);

        IReadOnlyList<BackupInfo> backups = engine.AsEnumerable();

        // A materialised list, not a lazy sequence: the count is available
        // without enumerating, and enumerating twice gives the same thing.
        Assert.Equal(2, backups.Count);
        Assert.Equal(backups.Select(b => b.BackupId), backups.Select(b => b.BackupId));

        // Taking one entry and abandoning the rest used to leak the native
        // info object. Now there is nothing left open to leak.
        BackupInfo first = engine.AsEnumerable().First();
        Assert.True(first.BackupId > 0);
        Assert.True(first.Timestamp > 0);
    }

    // ── ExclusiveManualCompaction is readable ───────────────────────────────

    [Fact]
    public void ExclusiveManualCompaction_RoundTrips()
    {
        using var opts = new CompactRangeOptions();

        opts.ExclusiveManualCompaction = true;
        Assert.True(opts.ExclusiveManualCompaction);

        opts.ExclusiveManualCompaction = false;
        Assert.False(opts.ExclusiveManualCompaction);
    }
}
