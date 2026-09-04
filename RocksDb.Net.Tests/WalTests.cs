namespace RocksDbNet.Tests;

/// <summary>
/// Covers the write-ahead log listing and iterator. See issue #26.
/// </summary>
public class WalTests
{
    // ── WalReadOptions ───────────────────────────────────────────────────────

    [Fact]
    public void WalReadOptions_VerifyChecksums_RoundTrips()
    {
        using var opts = new WalReadOptions();

        opts.VerifyChecksums = true;
        Assert.True(opts.VerifyChecksums);

        opts.VerifyChecksums = false;
        Assert.False(opts.VerifyChecksums);
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetSortedWalFiles_ReportsTheLiveLog()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        db.Db.Put("b", "2");

        IReadOnlyList<WalFile> files = db.Db.GetSortedWalFiles();

        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.Type == WalFileType.AliveLog);
        Assert.All(files, f => Assert.False(string.IsNullOrEmpty(f.PathName)));
        Assert.All(files, f => Assert.True(f.LogNumber > 0));
    }

    /// <summary>
    /// Both ordering tests need more than one WAL file to mean anything: a
    /// single-element list is trivially sorted and is trivially its own last
    /// entry, which is all the two tests below used to establish.
    /// </summary>
    /// <remarks>
    /// A flush rolls the WAL, but RocksDb deletes the retired file immediately
    /// unless something asks it to keep it. A TTL archives them instead.
    /// </remarks>
    private static RocksDb OpenWithSeveralWalFiles(string path)
    {
        using var opts = new DbOptions { CreateIfMissing = true, WalTtlSeconds = 600 };

        RocksDb db = RocksDb.Open(opts, path);

        for (int i = 0; i < 4; i++)
        {
            db.Put($"key{i}", $"value{i}");
            db.Flush();
        }

        return db;
    }

    [Fact]
    public void GetSortedWalFiles_IsOrderedByLogNumber()
    {
        using var dir = new TempDir();
        using RocksDb db = OpenWithSeveralWalFiles(dir.Path);

        IReadOnlyList<WalFile> files = db.GetSortedWalFiles();
        ulong[] logNumbers = [.. files.Select(f => f.LogNumber)];

        Assert.True(logNumbers.Length > 1, $"only {logNumbers.Length} WAL file, nothing to order");
        Assert.Equal(logNumbers.Distinct().Count(), logNumbers.Length);
        Assert.Equal(logNumbers.OrderBy(n => n), logNumbers);
    }

    [Fact]
    public void GetCurrentWalFile_MatchesTheLastSortedEntry()
    {
        using var dir = new TempDir();
        using RocksDb db = OpenWithSeveralWalFiles(dir.Path);

        // The flush that made the last file left the new live log empty, and an
        // empty live log is not listed. See the test below.
        db.Put("live", "1");

        WalFile? current = db.GetCurrentWalFile();
        IReadOnlyList<WalFile> sorted = db.GetSortedWalFiles();

        Assert.NotNull(current);
        Assert.True(sorted.Count > 1, $"only {sorted.Count} WAL file, so any entry is the last one");

        // The live log is the highest-numbered one, and the archived files
        // before it are genuinely older rather than the same file counted once.
        Assert.Equal(sorted[^1].LogNumber, current!.LogNumber);
        Assert.True(current.LogNumber > sorted[0].LogNumber);
        Assert.Equal(WalFileType.AliveLog, current.Type);
        Assert.Equal(WalFileType.ArchivedLog, sorted[0].Type);
    }

    /// <summary>
    /// A live log with nothing written to it yet does not appear in the sorted
    /// list at all, so the last entry there is an archived file and
    /// <c>GetCurrentWalFile</c> is the only way to see the live one.
    /// </summary>
    /// <remarks>
    /// Worth pinning because it contradicts the obvious reading of the two
    /// methods, and it only shows up once there is more than one WAL file: with
    /// one file the list and the current entry trivially agree. Anyone using
    /// the last sorted entry to find the live log gets the wrong file for as
    /// long as the new log stays empty.
    /// </remarks>
    [Fact]
    public void GetSortedWalFiles_OmitsTheLiveLogWhileItIsEmpty()
    {
        using var dir = new TempDir();

        // Ends with a flush, so the live log exists but holds nothing.
        using RocksDb db = OpenWithSeveralWalFiles(dir.Path);

        WalFile? current = db.GetCurrentWalFile();
        IReadOnlyList<WalFile> sorted = db.GetSortedWalFiles();

        Assert.NotNull(current);
        Assert.Equal(WalFileType.AliveLog, current!.Type);
        Assert.All(sorted, f => Assert.Equal(WalFileType.ArchivedLog, f.Type));
        Assert.DoesNotContain(sorted, f => f.LogNumber == current.LogNumber);
        Assert.True(current.LogNumber > sorted[^1].LogNumber);

        // One write is enough to bring it into the list.
        db.Put("live", "1");

        IReadOnlyList<WalFile> afterWriting = db.GetSortedWalFiles();

        Assert.Equal(current.LogNumber, afterWriting[^1].LogNumber);
        Assert.Equal(WalFileType.AliveLog, afterWriting[^1].Type);
    }

    /// <summary>
    /// The values are copied out of RocksDb's own structures, so they must stay
    /// readable after the call that produced them has released them. Getting
    /// this wrong would read freed memory, since list entries are borrowed from
    /// a vector while the current-file handle is owned.
    /// </summary>
    [Fact]
    public void WalFileInfo_SurvivesTheOwningStructures()
    {
        IReadOnlyList<WalFile> files;
        WalFile? current;

        using (var db = new TempDb())
        {
            db.Db.Put("a", "1");
            files = db.Db.GetSortedWalFiles();
            current = db.Db.GetCurrentWalFile();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.False(string.IsNullOrEmpty(f.PathName)));
        Assert.NotNull(current);
        Assert.False(string.IsNullOrEmpty(current!.PathName));
    }

    // ── Iterator ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetUpdatesSince_YieldsTheBatchesThatWereWritten()
    {
        using var db = new TempDb();

        for (int i = 0; i < 5; i++)
        {
            db.Db.Put($"key{i}", $"value{i}");
        }

        using WalIterator iter = db.Db.GetUpdatesSince(0);

        var sequences = new List<ulong>();
        int batches = 0;

        foreach ((WriteBatch batch, ulong sequence) in iter.AsEnumerable())
        {
            batches++;
            sequences.Add(sequence);
            Assert.True(batch.Count > 0);
        }

        Assert.Equal(5, batches);

        // Each Put is its own batch, so the sequence numbers strictly increase.
        Assert.Equal(sequences.OrderBy(s => s), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
    }

    [Fact]
    public void GetUpdatesSince_StartsFromTheGivenSequence()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");
        ulong afterFirst = db.Db.LatestSequenceNumber;
        db.Db.Put("b", "2");
        db.Db.Put("c", "3");

        using WalIterator iter = db.Db.GetUpdatesSince(afterFirst + 1);

        int batches = iter.AsEnumerable().Count();

        Assert.Equal(2, batches);
    }

    [Fact]
    public void GetUpdatesSince_WithOptions_Works()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");

        using var options = new WalReadOptions { VerifyChecksums = true };
        using WalIterator iter = db.Db.GetUpdatesSince(0, options);

        Assert.True(iter.AsEnumerable().Any());
    }

    /// <summary>
    /// RocksDb builds a fresh batch per call, so the returned batch is the
    /// caller's and outlives the iterator step.
    /// </summary>
    [Fact]
    public void GetBatch_ReturnsAnOwnedBatch()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");

        using WalIterator iter = db.Db.GetUpdatesSince(0);
        Assert.True(iter.IsValid());

        (WriteBatch batch, ulong sequence) = iter.GetBatch();
        using (batch)
        {
            Assert.True(sequence > 0);
            Assert.Equal(1, batch.Count);

            // Still usable after the iterator has moved on.
            iter.Next();
            Assert.Equal(1, batch.Count);
        }
    }

    /// <summary>
    /// The batches really do carry the writes, which is what makes this usable
    /// for replication: replaying them into an empty database reproduces it.
    /// </summary>
    [Fact]
    public void GetUpdatesSince_BatchesCanBeReplayedIntoAnotherDatabase()
    {
        using var source = new TempDb();
        using var target = new TempDb();

        source.Db.Put("a", "1");
        source.Db.Put("b", "2");
        source.Db.Delete("a");

        using WalIterator iter = source.Db.GetUpdatesSince(0);
        foreach ((WriteBatch batch, ulong _) in iter.AsEnumerable())
        {
            target.Db.Write(batch);
        }

        Assert.Null(target.Db.GetString("a"));
        Assert.Equal("2", target.Db.GetString("b"));
    }

    [Fact]
    public void GetUpdatesSince_OnAnEmptyDatabase_YieldsNothing()
    {
        using var db = new TempDb();

        using WalIterator iter = db.Db.GetUpdatesSince(0);

        Assert.Empty(iter.AsEnumerable());
    }

    [Fact]
    public void WalIterator_CheckForError_OnHealthyLog_DoesNotThrow()
    {
        using var db = new TempDb();

        db.Db.Put("a", "1");

        using WalIterator iter = db.Db.GetUpdatesSince(0);
        while (iter.IsValid())
        {
            iter.Next();
        }

        iter.CheckForError();
    }
}
