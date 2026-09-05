using System.Buffers.Binary;
using System.Text;

namespace RocksDbNet.Tests;

/// <summary>
/// The code from the documentation guides, compiled and run.
/// </summary>
/// <remarks>
/// Each guide states that its snippets are compiled and run as part of the test
/// suite, and this is what makes that true. The code here is kept identical to
/// what the guides show, so a snippet that stops compiling or stops behaving as
/// described fails CI rather than misleading a reader. Two things are added or
/// changed: paths, which point at an in-memory environment or at a temporary
/// directory where the test needs real files, and assertions, which a snippet
/// does not carry — including waits, because a guide showing an event listener
/// does not have to say that RocksDb delivers the callback on its own thread
/// while a test asserting on it does. If you change a guide, change the
/// matching test with it.
/// </remarks>
public class DocumentationGuideTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // getting-started.md
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GettingStarted_OpenPutGet()
    {
        var options = new DbOptions { CreateIfMissing = true };
        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("hello", "world");
        string? value = db.GetString("hello");

        Assert.Equal("world", value);
    }

    [Fact]
    public void GettingStarted_KeysAndValuesAreBytes()
    {
        using var db = new TempDb();
        byte[] bytes = "value"u8.ToArray();

        db.Db.Put("key1", "value");
        db.Db.Put("key2"u8, "value"u8);
        db.Db.Put(Encoding.UTF8.GetBytes("key3"), bytes);

        Assert.Equal("value", db.Db.GetString("key1"));
        Assert.Equal("value"u8.ToArray(), db.Db.Get("key2"u8));
        Assert.Equal("value", db.Db.GetString("key3"));
    }

    /// <summary>
    /// The guide claims a missing key and an empty value are distinguishable.
    /// </summary>
    [Fact]
    public void GettingStarted_MissingKeyIsNullAndEmptyValueIsNot()
    {
        using var db = new TempDb();

        db.Db.Put("empty"u8, []);

        byte[]? empty = db.Db.Get("empty"u8);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        Assert.Null(db.Db.Get("absent"u8));
    }

    [Fact]
    public void GettingStarted_Delete()
    {
        using var db = new TempDb();

        db.Db.Put("key", "value");
        db.Db.Delete("key");

        Assert.Null(db.Db.GetString("key"));
    }

    [Fact]
    public void GettingStarted_IterateWithAForLoop()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");

        var seen = new List<string>();

        using Iterator iter = db.Db.NewIterator();

        for (iter.SeekToFirst(); iter.IsValid(); iter.Next())
        {
            seen.Add($"{iter.KeyAsString()} = {iter.ValueAsString()}");
        }

        Assert.Equal(["a = 1", "b = 2"], seen);
    }

    [Fact]
    public void GettingStarted_IterateWithForeach()
    {
        using var db = new TempDb();
        db.Db.Put("a", "1");
        db.Db.Put("b", "2");

        using Iterator iter = db.Db.NewIterator();
        iter.SeekToFirst();

        var seen = new List<string>();

        foreach (Iterator.Entry entry in iter)
        {
            seen.Add(Encoding.UTF8.GetString(entry.Key) + "=" + Encoding.UTF8.GetString(entry.Value));
        }

        Assert.Equal(["a=1", "b=2"], seen);
    }

    [Fact]
    public void GettingStarted_WriteBatchIsAtomic()
    {
        using var db = new TempDb();
        db.Db.Put("c", "3");

        using var batch = new WriteBatch();
        batch.Put("a", "1");
        batch.Put("b", "2");
        batch.Delete("c");

        db.Db.Write(batch);

        Assert.Equal("1", db.Db.GetString("a"));
        Assert.Equal("2", db.Db.GetString("b"));
        Assert.Null(db.Db.GetString("c"));
    }

    [Fact]
    public void GettingStarted_Durability()
    {
        using var db = TempDb.OnDisk();

        using var sync = new WriteOptions { Sync = true };
        db.Db.Put("important", "value", sync);

        db.Db.FlushWal(sync: true);

        Assert.Equal("value", db.Db.GetString("important"));
    }

    [Fact]
    public void GettingStarted_ColumnFamilies()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        using var db = RocksDb.Open(options, dir.Path, [new("default"), new("users")]);
        ColumnFamilyHandle users = db.GetColumnFamily("users");

        db.Put("alice", "…", users);
        string? alice = db.GetString("alice", users);

        Assert.Equal("…", alice);

        // The guide warns that a bare null binds to the WriteOptions overload,
        // so this writes to the default family rather than throwing.
        db.Put("bob", "…", null);
        Assert.Equal("…", db.GetString("bob"));
        Assert.Null(db.GetString("bob", users));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // compaction-filters.md
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class ExpiryFilter : CompactionFilter
    {
        private readonly TimeSpan _retention;

        public ExpiryFilter(TimeSpan retention)
            : base("ExpiryFilter")
        {
            _retention = retention;
        }

        protected override FilterDecision Filter(
            int level,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> existingValue,
            out byte[]? newValue)
        {
            newValue = null;

            if (existingValue.Length < sizeof(long))
            {
                return FilterDecision.Keep;
            }

            long written = BinaryPrimitives.ReadInt64LittleEndian(existingValue);
            DateTimeOffset writtenAt = DateTimeOffset.FromUnixTimeSeconds(written);

            return DateTimeOffset.UtcNow - writtenAt > _retention
                ? FilterDecision.Remove
                : FilterDecision.Keep;
        }
    }

    private static byte[] Stamped(DateTimeOffset at)
    {
        var value = new byte[sizeof(long) + 4];
        BinaryPrimitives.WriteInt64LittleEndian(value, at.ToUnixTimeSeconds());
        "data"u8.CopyTo(value.AsSpan(sizeof(long)));
        return value;
    }

    [Fact]
    public void CompactionFilters_ExpiryFilterDropsOldEntries()
    {
        var filter = new ExpiryFilter(TimeSpan.FromDays(30));

        var options = new DbOptions { CreateIfMissing = true };
        options.CompactionFilter = filter;

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("fresh"u8, Stamped(DateTimeOffset.UtcNow));
        db.Put("stale"u8, Stamped(DateTimeOffset.UtcNow.AddDays(-90)));

        // The guide's central point: nothing is dropped until a rewrite.
        db.Flush();
        Assert.NotNull(db.Get("stale"u8));

        db.CompactRange();

        Assert.NotNull(db.Get("fresh"u8));
        Assert.Null(db.Get("stale"u8));
    }

    /// <summary>
    /// The guide says a value the filter cannot interpret is kept, not dropped.
    /// </summary>
    [Fact]
    public void CompactionFilters_ShortValuesAreKept()
    {
        var filter = new ExpiryFilter(TimeSpan.Zero);

        var options = new DbOptions { CreateIfMissing = true };
        options.CompactionFilter = filter;

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("short"u8, "ab"u8);
        db.Flush();
        db.CompactRange();

        Assert.NotNull(db.Get("short"u8));
    }

    private sealed class UppercaseFilter : CompactionFilter
    {
        public UppercaseFilter()
            : base("UppercaseFilter")
        {
        }

        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = new byte[existingValue.Length];

            for (int i = 0; i < existingValue.Length; i++)
            {
                newValue[i] = (byte)char.ToUpperInvariant((char)existingValue[i]);
            }

            return FilterDecision.ChangeValue;
        }
    }

    [Fact]
    public void CompactionFilters_UppercaseFilterRewritesValues()
    {
        using var filter = new UppercaseFilter();

        var options = new DbOptions { CreateIfMissing = true };
        options.CompactionFilter = filter;

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("key", "value");
        db.Flush();
        db.CompactRange();

        Assert.Equal("VALUE", db.GetString("key"));
    }

    private sealed class ExpiryFilterFactory : CompactionFilterFactory
    {
        private readonly TimeSpan _retention;
        private int _filtersCreated;

        public ExpiryFilterFactory(TimeSpan retention)
            : base("ExpiryFilterFactory")
        {
            _retention = retention;
        }

        /// <summary>
        /// How many filters the factory was asked for. Not part of the guide
        /// snippet; the test needs it to check the claim in its own name.
        /// </summary>
        public int FiltersCreated => Volatile.Read(ref _filtersCreated);

        protected override CompactionFilter CreateFilter(CompactionFilterContext context)
        {
            // Compaction jobs run on background threads.
            Interlocked.Increment(ref _filtersCreated);

            return new ExpiryFilter(_retention);
        }
    }

    [Fact]
    public void CompactionFilters_FactoryProducesOneFilterPerJob()
    {
        var factory = new ExpiryFilterFactory(TimeSpan.FromDays(30));

        var options = new DbOptions { CreateIfMissing = true };
        options.CompactionFilterFactory = factory;

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("fresh"u8, Stamped(DateTimeOffset.UtcNow));
        db.Put("stale"u8, Stamped(DateTimeOffset.UtcNow.AddDays(-90)));
        db.Flush();
        db.CompactRange();

        Assert.NotNull(db.Get("fresh"u8));
        Assert.Null(db.Get("stale"u8));

        // What the name of this test claims, and what it never checked: the
        // factory was actually asked for a filter. The two assertions above
        // would hold identically if RocksDb had used one long-lived filter.
        Assert.True(factory.FiltersCreated > 0, "the factory was never asked for a filter");

        // A second compaction asks again rather than reusing the first.
        int afterFirst = factory.FiltersCreated;

        db.Put("second"u8, Stamped(DateTimeOffset.UtcNow));
        db.Flush();
        db.CompactRange();

        Assert.True(
            factory.FiltersCreated > afterFirst,
            $"the second compaction reused a filter: {afterFirst} created, then {factory.FiltersCreated}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // writing-callbacks.md
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class ReverseComparator : Comparator
    {
        public ReverseComparator()
            : base("example.reverse")
        {
        }

        public override int Compare(ReadOnlySpan<byte> keyA, ReadOnlySpan<byte> keyB)
            => keyB.SequenceCompareTo(keyA);
    }

    [Fact]
    public void WritingCallbacks_ComparatorChangesScanOrder()
    {
        var comparator = new ReverseComparator();

        var options = new DbOptions { CreateIfMissing = true };
        options.Comparator = comparator;

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Put("a", "1");
        db.Put("b", "2");
        db.Put("c", "3");

        using Iterator iter = db.NewIterator();
        iter.SeekToFirst();

        Assert.Equal("c", iter.KeyAsString());
    }

    private sealed class CounterMergeOperator : MergeOperator
    {
        public CounterMergeOperator()
            : base("example.counter")
        {
        }

        public override bool FullMerge(
            ReadOnlySpan<byte> key,
            bool hasExistingValue,
            ReadOnlySpan<byte> existingValue,
            IReadOnlyList<byte[]> operands,
            out byte[]? newValue)
        {
            long total = hasExistingValue && existingValue.Length == sizeof(long)
                ? BinaryPrimitives.ReadInt64LittleEndian(existingValue)
                : 0;

            foreach (byte[] operand in operands)
            {
                if (operand.Length == sizeof(long))
                {
                    total += BinaryPrimitives.ReadInt64LittleEndian(operand);
                }
            }

            newValue = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(newValue, total);
            return true;
        }

        public override bool PartialMerge(
            ReadOnlySpan<byte> key, IReadOnlyList<byte[]> operands, out byte[]? newValue)
        {
            long sum = 0;

            foreach (byte[] operand in operands)
            {
                if (operand.Length != sizeof(long))
                {
                    newValue = [];
                    return false;
                }

                sum += BinaryPrimitives.ReadInt64LittleEndian(operand);
            }

            newValue = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(newValue, sum);
            return true;
        }
    }

    private static byte[] Delta(long by)
    {
        var operand = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(operand, by);
        return operand;
    }

    [Fact]
    public void WritingCallbacks_MergeOperatorAccumulates()
    {
        var options = new DbOptions { CreateIfMissing = true };
        options.MergeOperator = new CounterMergeOperator();

        string path = TestDb.InMemory(options);
        using var db = RocksDb.Open(options, path);

        db.Merge("visits"u8, Delta(1));
        db.Merge("visits"u8, Delta(5));

        long visits = BinaryPrimitives.ReadInt64LittleEndian(db.Get("visits"u8));

        Assert.Equal(6, visits);
    }

    /// <summary>
    /// The guide states that handing one merge operator to two options objects
    /// throws rather than corrupting the heap later.
    /// </summary>
    [Fact]
    public void WritingCallbacks_MergeOperatorCannotBeSharedBetweenOptions()
    {
        var op = new CounterMergeOperator();

        using var first = new DbOptions();
        first.MergeOperator = op;

        using var second = new DbOptions();
        Assert.Throws<InvalidOperationException>(() => second.MergeOperator = op);
    }

    private sealed class CollectingLogger : Logger
    {
        private readonly List<string> _lines = [];

        public CollectingLogger()
            : base(InfoLogLevel.Info)
        {
        }

        public int Count
        {
            get { lock (_lines) { return _lines.Count; } }
        }

        public override void Log(InfoLogLevel logLevel, string message)
        {
            lock (_lines)
            {
                _lines.Add($"[rocksdb {logLevel}] {message}");
            }
        }
    }

    [Fact]
    public void WritingCallbacks_LoggerReceivesDiagnostics()
    {
        var logger = new CollectingLogger();

        var options = new DbOptions { CreateIfMissing = true };
        options.InfoLog = logger;

        // The guide says a using on the logger is safe because disposal is
        // deferred while the database still holds it.
        logger.Dispose();
        Assert.False(logger.IsDisposed);

        string path = TestDb.InMemory(options);
        using (var db = RocksDb.Open(options, path))
        {
            db.Put("key", "value");
            db.Flush();
        }

        Assert.True(logger.IsDisposed);
        Assert.True(logger.Count > 0, "the logger should have received messages");
    }

    private sealed class FlushWatcher : EventListener
    {
        private long _flushes;

        public long Flushes => Interlocked.Read(ref _flushes);

        public override void OnFlushCompleted(FlushJobInfo info)
            => Interlocked.Increment(ref _flushes);

        public override void OnBackgroundError(BackgroundErrorInfo info)
        {
            // The guide writes this to stderr; swallowed here so a test run
            // stays quiet.
        }
    }

    [Fact]
    public void WritingCallbacks_EventListenerObservesFlushes()
    {
        var watcher = new FlushWatcher();

        var options = new DbOptions { CreateIfMissing = true };
        options.AddEventListener(watcher);

        string path = TestDb.InMemory(options);
        using (var db = RocksDb.Open(options, path))
        {
            db.Put("key", "value");
            db.Flush();
        }

        Assert.True(Wait.Until(() => watcher.Flushes > 0), "no flush was observed");
    }

    /// <summary>
    /// The guide states the event listener setter appends rather than replaces.
    /// </summary>
    [Fact]
    public void WritingCallbacks_AddEventListenerAccumulates()
    {
        var first = new FlushWatcher();
        var second = new FlushWatcher();

        var options = new DbOptions { CreateIfMissing = true };
        options.AddEventListener(first);
        options.AddEventListener(second);

        string path = TestDb.InMemory(options);
        using (var db = RocksDb.Open(options, path))
        {
            db.Put("key", "value");
            db.Flush();
        }

        Assert.True(
            Wait.Until(() => first.Flushes > 0 && second.Flushes > 0),
            "both listeners should receive events");
    }

    [Fact]
    public void GettingStarted_StoringLargeValues()
    {
        using var dir = new TempDir();
        string path = dir.Path;

        var options = new DbOptions
        {
            CreateIfMissing = true,
            EnableBlobFiles = true,
            MinBlobSize = 1024,
            EnableBlobGarbageCollection = true,
        };

        using var db = RocksDb.Open(options, path);

        db.Put("large", new string('v', 4096));

        db.Flush();

        Assert.NotEmpty(Directory.GetFiles(path, "*.blob"));
        Assert.Equal(new string('v', 4096), db.GetString("large"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // transactions.md
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Transactions_PessimisticReadModifyWrite()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        using var txnOptions = new TransactionDbOptions();
        using TransactionDb db = TransactionDb.Open(options, txnOptions, dir.Path);

        using Transaction txn = db.BeginTransaction();

        // GetForUpdate locks the key. A plain Get does not, and a decision based
        // on one is not protected against anything.
        string? balance = txn.GetStringForUpdate("account:1");
        txn.Put("account:1", (int.Parse(balance ?? "0") + 100).ToString());

        txn.Commit();

        Assert.Equal("100", db.GetString("account:1"));
    }

    /// <summary>
    /// The retry loop from the guide, driven into its interesting branch: a
    /// competing commit lands mid-transaction, so the first attempt fails and
    /// the second reads the newer value.
    /// </summary>
    [Fact]
    public void Transactions_OptimisticRetryLoop()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(options, dir.Path);

        bool interfered = false;

        for (int attempt = 0; ; attempt++)
        {
            using Transaction txn = db.BeginTransaction();

            string? balance = txn.GetStringForUpdate("account:1");
            txn.Put("account:1", (int.Parse(balance ?? "0") + 100).ToString());

            // Not in the guide: forces the conflict the loop exists to handle,
            // exactly once, so the catch is exercised rather than assumed.
            if (!interfered)
            {
                interfered = true;
                using Transaction other = db.BeginTransaction();
                other.Put("account:1", "500");
                other.Commit();
            }

            try
            {
                txn.Commit();
                break;
            }
            catch (RocksDbException) when (attempt < 5)
            {
                // Someone else committed first. Nothing was written, so start
                // again from what the database says now.
            }
        }

        // 500 from the interfering write, plus the 100 the retry added.
        using Transaction reader = db.BeginTransaction();
        Assert.Equal("600", reader.GetString("account:1"));
    }

    [Fact]
    public void Transactions_MultiGet()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(options, dir.Path);

        using (Transaction seed = db.BeginTransaction())
        {
            seed.Put("account:1", "1");
            seed.Put("account:3", "3");
            seed.Commit();
        }

        using Transaction txn = db.BeginTransaction();

        byte[]?[] values = txn.MultiGet([
            "account:1"u8.ToArray(),
            "account:2"u8.ToArray(),
            "account:3"u8.ToArray(),
        ]);

        // A missing key is null in the corresponding position.
        Assert.Equal("1", Encoding.UTF8.GetString(values[0]!));
        Assert.Null(values[1]);
        Assert.Equal("3", Encoding.UTF8.GetString(values[2]!));
    }

    [Fact]
    public void Transactions_GetPinned()
    {
        using var dir = new TempDir();

        var options = new DbOptions { CreateIfMissing = true };
        using OptimisticTransactionDb db = OptimisticTransactionDb.Open(options, dir.Path);

        using (Transaction seed = db.BeginTransaction())
        {
            seed.Put("account:1", "100");
            seed.Commit();
        }

        using Transaction txn = db.BeginTransaction();
        using PinnableSlice? slice = txn.GetPinned("account:1"u8.ToArray());

        Assert.NotNull(slice);

        ReadOnlySpan<byte> value = slice.Value;   // no copy
        Assert.Equal("100"u8.ToArray(), value.ToArray());
    }

    /// <summary>
    /// Both halves of the two-phase commit section, across a real close and
    /// reopen — which is the only way the recovery snippet means anything.
    /// </summary>
    [Fact]
    public void Transactions_PrepareAndRecover()
    {
        using var dir = new TempDir();
        using var txnOptions = new TransactionDbOptions();

        using (TransactionDb db = TransactionDb.Open(
            new DbOptions { CreateIfMissing = true }, txnOptions, dir.Path))
        {
            using Transaction txn = db.BeginTransaction();
            txn.Put("order:4711", "pending");

            txn.Name = "order-4711";
            txn.Prepare();          // durable, but not committed
        }

        DbOptions options = new() { CreateIfMissing = true };

        using (TransactionDb db = TransactionDb.Open(options, txnOptions, dir.Path))
        {
            foreach (Transaction recovered in db.GetPreparedTransactions())
            {
                using (recovered)
                {
                    // The name is how you decide. It is yours to choose, so make
                    // it mean something to whoever has to resolve it.
                    if (recovered.Name == "order-4711")
                    {
                        recovered.Commit();
                    }
                    else
                    {
                        recovered.Rollback();
                    }
                }
            }

            Assert.Equal("pending", db.GetString("order:4711"));
            Assert.Empty(db.GetPreparedTransactions());
        }
    }
}
