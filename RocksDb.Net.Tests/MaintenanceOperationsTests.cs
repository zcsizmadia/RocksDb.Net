using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// Asserts what the maintenance operations actually do to the files on disk.
/// See issue #31.
/// </summary>
/// <remarks>
/// These replace earlier tests that only checked the calls did not throw. A call
/// that silently did nothing passed those, which matters here because every one
/// of these operations has a condition under which it is a no-op, and the
/// conditions are not obvious from the names.
/// </remarks>
public class MaintenanceOperationsTests
{
    /// <summary>Number of SST files RocksDb currently counts as live.</summary>
    private static int LiveFileCount(RocksDb db, ColumnFamilyHandle? cf = null)
    {
        ColumnFamilyMetadata? metadata = cf is null
            ? db.GetColumnFamilyMetadata()
            : db.GetColumnFamilyMetadata(cf);

        Assert.NotNull(metadata);
        return metadata.FileCount;
    }

    /// <summary>
    /// Fills a database with several small files spread across three key
    /// prefixes, compacted out of level 0 so that
    /// <see cref="RocksDb.DeleteFilesInRange(string, string)"/> can see them.
    /// </summary>
    private static void WriteCompactedFilesInThreeRanges(RocksDb db, ColumnFamilyHandle? cf = null)
    {
        foreach (char prefix in "amz")
        {
            for (int i = 0; i < 20; i++)
            {
                string key = $"{prefix}{i:D4}";
                string value = new('v', 512);

                if (cf is null)
                {
                    db.Put(key, value);
                }
                else
                {
                    db.Put(key, value, cf);
                }
            }

            if (cf is null)
            {
                db.Flush();
            }
            else
            {
                db.Flush(cf);
            }
        }

        if (cf is null)
        {
            db.CompactRange();
        }
        else
        {
            db.CompactRange(cf);
        }
    }

    /// <summary>Small target file size, so a compaction emits several files rather than one.</summary>
    private static DbOptions SplitFileOptions() => new()
    {
        CreateIfMissing = true,
        CreateMissingColumnFamilies = true,
        DisableAutoCompactions = true,
        TargetFileSizeBase = 8 * 1024,
        WriteBufferSize = 64 * 1024,
    };

    [Fact]
    public void DeleteFilesInRange_RemovesTheFilesCoveringThatRangeAndTheirKeys()
    {
        using DbOptions opts = SplitFileOptions();
        using var db = TestDb.OpenInMemory(opts);

        WriteCompactedFilesInThreeRanges(db);
        int before = LiveFileCount(db);
        Assert.True(before > 1, $"the fixture should produce several files, saw {before}");

        db.DeleteFilesInRange("m", "m9999");

        // The file count really dropped. This is the assertion the old
        // does-not-throw test was missing.
        Assert.True(LiveFileCount(db) < before, $"expected fewer than {before} files");

        // Deleting the file deleted its keys, with no tombstone involved.
        Assert.Null(db.GetString("m0000"));

        // Ranges outside the deleted files are untouched.
        Assert.NotNull(db.GetString("a0000"));
        Assert.NotNull(db.GetString("z0000"));
    }

    /// <summary>
    /// Level 0 is exempt. Worth pinning down, because a caller who deletes a
    /// range without compacting first sees nothing happen and no error.
    /// </summary>
    [Fact]
    public void DeleteFilesInRange_LeavesLevel0Alone()
    {
        using DbOptions opts = SplitFileOptions();
        using var db = TestDb.OpenInMemory(opts);

        db.Put("a0000", "1");
        db.Flush();
        db.Put("m0000", "2");
        db.Flush();

        ColumnFamilyMetadata? metadata = db.GetColumnFamilyMetadata();
        Assert.NotNull(metadata);
        ColumnFamilyLevelMetadata level0 = metadata.Levels.Single(l => l.Level == 0);
        Assert.Equal(2, level0.FileCount);

        db.DeleteFilesInRange("m", "m9999");

        Assert.Equal(2, LiveFileCount(db));
        Assert.Equal("2", db.GetString("m0000"));
    }

    [Fact]
    public void DeleteFilesInRange_ColumnFamily_RemovesFilesFromThatFamilyOnly()
    {
        using var dir = new TempDir();
        using DbOptions opts = SplitFileOptions();
        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        WriteCompactedFilesInThreeRanges(db);
        WriteCompactedFilesInThreeRanges(db, cf1);

        int defaultBefore = LiveFileCount(db);
        int cf1Before = LiveFileCount(db, cf1);
        Assert.True(cf1Before > 1, $"the fixture should produce several files, saw {cf1Before}");

        db.DeleteFilesInRange(cf1, "m", "m9999");

        Assert.True(LiveFileCount(db, cf1) < cf1Before, $"expected fewer than {cf1Before} files in cf1");
        Assert.Null(db.GetString("m0000", cf1));

        // The default family kept every file and every key.
        Assert.Equal(defaultBefore, LiveFileCount(db));
        Assert.NotNull(db.GetString("m0000"));
    }

    /// <summary>
    /// While deletions are disabled, the obsolete input files of a compaction
    /// have to stay on disk even though they are no longer live. Enabling
    /// deletions then removes them.
    /// </summary>
    [Fact]
    public void DisableFileDeletions_KeepsObsoleteFilesUntilDeletionsAreEnabled()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true };
        using var db = RocksDb.Open(opts, dir.Path);

        db.WriteOverlappingSstFiles();
        Assert.Equal(2, SstFilesOnDisk(dir.Path));

        db.DisableFileDeletions();
        db.CompactRange();

        // One live file now, the compaction output, but the two inputs are still
        // on disk because their cleanup is deferred.
        int liveWhileDisabled = db.LiveFileNames().Length;
        Assert.Equal(1, liveWhileDisabled);
        Assert.True(
            SstFilesOnDisk(dir.Path) > liveWhileDisabled,
            "obsolete inputs should still be on disk while deletions are disabled");

        db.EnableFileDeletions();

        // The deferred cleanup ran, leaving only the live file.
        Assert.Equal(liveWhileDisabled, SstFilesOnDisk(dir.Path));
        Assert.Equal("1-updated", db.GetString("a"));
    }

    private static int SstFilesOnDisk(string path) => Directory.GetFiles(path, "*.sst").Length;

    /// <summary>
    /// A suggestion should produce an actual background compaction, observed
    /// through an event listener rather than assumed.
    /// </summary>
    [Fact]
    public void SuggestCompactRange_CausesABackgroundCompaction()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            // High enough that the level 0 file count never triggers a compaction
            // by itself, so a compaction here is attributable to the suggestion.
            Level0FileNumCompactionTrigger = 100,
        };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        // Populate a level above 0, then leave a file in level 0. Only levels
        // below the highest non-empty one get marked, so without this the
        // suggestion has nothing to mark and is silently a no-op.
        WriteAndFlush(db, 'v');
        db.CompactRange();
        WriteAndFlush(db, 'w');

        int before = listener.CompactionCompleted.Count;

        db.SuggestCompactRange(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("kez"));

        Assert.True(
            WaitUntil(() => listener.CompactionCompleted.Count > before),
            $"expected a compaction after the suggestion, still at {before}");

        // The level 0 file was merged away.
        ColumnFamilyMetadata? metadata = db.GetColumnFamilyMetadata();
        Assert.NotNull(metadata);
        Assert.Equal(0, metadata.Levels.Single(l => l.Level == 0).FileCount);
    }

    [Fact]
    public void SuggestCompactRange_ColumnFamily_CausesABackgroundCompaction()
    {
        using var dir = new TempDir();
        var listener = new RecordingListener();

        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
            Level0FileNumCompactionTrigger = 100,
        };
        opts.AddEventListener(listener);

        var cfDescs = new List<ColumnFamilyDescriptor> { new("default"), new("cf1") };
        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        ColumnFamilyHandle cf1 = db.GetColumnFamily("cf1");

        WriteAndFlush(db, 'v', cf1);
        db.CompactRange(cf1);
        WriteAndFlush(db, 'w', cf1);

        int before = listener.CompactionCompleted.Count;

        db.SuggestCompactRange(cf1, Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("kez"));

        Assert.True(
            WaitUntil(() => listener.CompactionCompleted.Count > before),
            $"expected a compaction after the suggestion, still at {before}");

        ColumnFamilyMetadata? metadata = db.GetColumnFamilyMetadata(cf1);
        Assert.NotNull(metadata);
        Assert.Equal(0, metadata.Levels.Single(l => l.Level == 0).FileCount);
    }

    [Fact]
    public void SuggestCompactRange_WithAllDataInLevel0_IsANoOp()
    {
        var listener = new RecordingListener();

        using var opts = new DbOptions { CreateIfMissing = true, Level0FileNumCompactionTrigger = 100 };
        opts.AddEventListener(listener);

        using var db = TestDb.OpenInMemory(opts);

        WriteAndFlush(db, 'v');
        Assert.Empty(listener.CompactionCompleted);

        db.SuggestCompactRange(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("kez"));

        // Nothing to mark, so nothing happens and nothing complains.
        Assert.False(
            WaitUntil(() => listener.CompactionCompleted.Count > 0, TimeSpan.FromSeconds(2)),
            "a level-0-only database has no files the suggestion can mark");
    }

    private static void WriteAndFlush(RocksDb db, char fill, ColumnFamilyHandle? cf = null)
    {
        for (int i = 0; i < 50; i++)
        {
            string key = $"key{i:D4}";
            string value = new(fill, 256);

            if (cf is null)
            {
                db.Put(key, value);
            }
            else
            {
                db.Put(key, value, cf);
            }
        }

        if (cf is null)
        {
            db.Flush();
        }
        else
        {
            db.Flush(cf);
        }
    }

    /// <summary>Polls a background condition, since compaction is asynchronous.</summary>
    private static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);

        while (elapsed.Elapsed < limit)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return condition();
    }
}
