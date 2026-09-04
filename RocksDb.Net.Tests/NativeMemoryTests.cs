namespace RocksDbNet.Tests;

/// <summary>
/// The native memory findings from the pre-release review: issues #120 and
/// #121.
/// </summary>
/// <remarks>
/// A leak is not directly observable from a test, so these do the next best
/// thing. They call each fixed path enough times that a double free, which is
/// the risk the fixes introduce, faults rather than passing quietly, and they
/// assert the values are still correct, which a wrong free would corrupt.
/// </remarks>
public class NativeMemoryTests
{
    // ── #121: owned pointers that were treated as borrowed ──────────────────

    /// <summary>
    /// The native accessor returns a fresh copy the caller owns. Reading it
    /// repeatedly must return the same name and must not fault.
    /// </summary>
    [Fact]
    public void ColumnFamilyHandleName_IsStableAcrossManyReads()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        using var db = RocksDb.Open(opts, dir.Path, [new("default"), new("users")]);

        ColumnFamilyHandle users = db.GetColumnFamily("users");

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal("users", users.Name);
        }

        Assert.Equal("default", db.GetColumnFamily("default").Name);
    }

    /// <summary>
    /// Every string and key on the metadata graph is a fresh copy the caller
    /// owns, unlike the live-files accessors that look identical from C#.
    /// </summary>
    [Fact]
    public void ColumnFamilyMetadata_IsStableAcrossManyReads()
    {
        using var db = new TempDb();

        for (int i = 0; i < 50; i++)
        {
            db.Db.Put($"key{i:D3}", "value");
        }

        db.Db.Flush();

        for (int i = 0; i < 100; i++)
        {
            ColumnFamilyMetadata? metadata = db.Db.GetColumnFamilyMetadata();

            Assert.NotNull(metadata);
            Assert.Equal("default", metadata.Name);

            SstFileMetadata file = metadata.Levels.Single(l => l.Level == 0).Files[0];

            Assert.False(string.IsNullOrEmpty(file.RelativeFilename));
            Assert.False(string.IsNullOrEmpty(file.Directory));
            Assert.NotNull(file.SmallestKey);
            Assert.NotEmpty(file.SmallestKey);
            Assert.NotNull(file.LargestKey);
        }
    }

    /// <summary>
    /// The live-files accessors are the opposite case: borrowed pointers that
    /// must not be freed. Reading them repeatedly guards against someone
    /// "fixing" them the way the metadata side needed.
    /// </summary>
    [Fact]
    public void LiveFileMetadata_IsStableAcrossManyReads()
    {
        using var db = new TempDb();

        db.Db.Put("key", "value");
        db.Db.Flush();

        for (int i = 0; i < 100; i++)
        {
            IReadOnlyList<LiveFileMetadata> files = db.Db.GetLiveFiles();

            Assert.NotEmpty(files);
            Assert.False(string.IsNullOrEmpty(files[0].Name));
            Assert.False(string.IsNullOrEmpty(files[0].Directory));
        }
    }

    /// <summary>
    /// A filter produced by a factory is owned by RocksDb and never disposed by
    /// the wrapper, so its new-value buffers can only be released from the
    /// native destructor callback. Many compactions, each changing values.
    /// </summary>
    private sealed class RewritingFilter : CompactionFilter
    {
        public RewritingFilter()
            : base("RewritingFilter")
        {
        }

        protected override FilterDecision Filter(
            int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> existingValue, out byte[]? newValue)
        {
            newValue = new byte[existingValue.Length];
            existingValue.CopyTo(newValue);
            newValue[0] = (byte)'X';
            return FilterDecision.ChangeValue;
        }
    }

    private sealed class RewritingFilterFactory : CompactionFilterFactory
    {
        public RewritingFilterFactory()
            : base("RewritingFilterFactory")
        {
        }

        protected override CompactionFilter CreateFilter(CompactionFilterContext context)
            => new RewritingFilter();
    }

    [Fact]
    public void FactoryCreatedFilters_SurviveManyCompactions()
    {
        var options = new DbOptions { CreateIfMissing = true };
        options.CompactionFilterFactory = new RewritingFilterFactory();

        using var db = TestDb.OpenInMemory(options);

        for (int round = 0; round < 25; round++)
        {
            for (int i = 0; i < 20; i++)
            {
                db.Put($"key{i:D3}", $"value{round}");
            }

            db.Flush();
            db.CompactRange();
        }

        Assert.StartsWith("X", db.GetString("key010"));
    }

    // ── #120: CreateColumnFamilies ──────────────────────────────────────────

    /// <summary>
    /// Names are passed to RocksDb as NUL-terminated strings, so a name whose
    /// length lands badly must still come back intact rather than with adjacent
    /// heap bytes appended.
    /// </summary>
    [Fact]
    public void CreateColumnFamilies_NamesSurviveIntact()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        // Lengths either side of the 8-byte boundaries where an unterminated
        // read would most likely run into a neighbouring object.
        string[] names =
        [
            "a", "ab", "abcdefg", "abcdefgh", "abcdefghi",
            "abcdefghabcdefgh", "abcdefghabcdefghi",
        ];

        IReadOnlyList<ColumnFamilyHandle> created = db.Db.CreateColumnFamilies(cfOptions, names);

        Assert.Equal(names.Length, created.Count);

        for (int i = 0; i < names.Length; i++)
        {
            Assert.Equal(names[i], created[i].Name);
            Assert.Equal(names[i], db.Db.GetColumnFamily(names[i]).Name);
        }

        // And they are usable, which a corrupted name would prevent.
        db.Db.Put("key", "value", created[0]);
        Assert.Equal("value", db.Db.GetString("key", created[0]));
    }

    [Fact]
    public void CreateColumnFamilies_EmptyListCreatesNothing()
    {
        using var db = new TempDb();
        using var cfOptions = new DbOptions();

        Assert.Empty(db.Db.CreateColumnFamilies(cfOptions, []));
    }
}
