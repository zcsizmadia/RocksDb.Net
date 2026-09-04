namespace RocksDbNet.Tests;

/// <summary>
/// Exporting one column family and importing it into a running database. See
/// issue #78.
/// </summary>
/// <remarks>
/// Same-process only, deliberately. Joining an export and an import in
/// separate processes would need RocksDb's file-list builder functions, which
/// this wrapper does not expose.
/// </remarks>
public class ColumnFamilyExportImportTests
{
    private static RocksDb OpenWith(string path, params string[] extraFamilies)
    {
        var options = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var descriptors = new List<ColumnFamilyDescriptor> { new("default") };
        descriptors.AddRange(extraFamilies.Select(n => new ColumnFamilyDescriptor(n)));

        return RocksDb.Open(options, path, descriptors);
    }

    [Fact]
    public void ImportColumnFamilyOptions_MoveFilesRoundTrips()
    {
        using var options = new ImportColumnFamilyOptions();

        Assert.False(options.MoveFiles);

        options.MoveFiles = true;
        Assert.True(options.MoveFiles);

        options.MoveFiles = false;
        Assert.False(options.MoveFiles);
    }

    [Fact]
    public void ExportColumnFamily_ProducesMetadataDescribingTheFiles()
    {
        using var dir = new TempDir();
        using RocksDb db = OpenWith(dir.Path, "source");
        ColumnFamilyHandle source = db.GetColumnFamily("source");

        for (int i = 0; i < 50; i++)
        {
            db.Put($"key{i:D3}", $"value{i}", source);
        }

        db.Flush(source);

        using Checkpoint checkpoint = Checkpoint.Create(db);

        // A path that does not exist yet. RocksDb creates the directory and
        // refuses one that is already there, even an empty one.
        using ExportImportFilesMetadata metadata =
            checkpoint.ExportColumnFamily(source, Path.Combine(dir.Path, "export"));

        Assert.False(string.IsNullOrEmpty(metadata.DbComparatorName));

        Assert.NotEmpty(metadata.GetFiles());
    }

    /// <summary>
    /// The point of the feature: move a column family into a database that is
    /// already open, which neither a checkpoint nor a backup can do.
    /// </summary>
    [Fact]
    public void ImportedColumnFamily_CarriesTheDataIntoAnotherDatabase()
    {
        using var sourceDir = new TempDir();
        using var targetDir = new TempDir();
        using var exportDir = new TempDir();

        string exportPath = Path.Combine(exportDir.Path, "cf");

        using (RocksDb source = OpenWith(sourceDir.Path, "payload"))
        {
            ColumnFamilyHandle cf = source.GetColumnFamily("payload");

            for (int i = 0; i < 100; i++)
            {
                source.Put($"key{i:D3}", $"value{i}", cf);
            }

            source.Flush(cf);

            using Checkpoint checkpoint = Checkpoint.Create(source);
            using ExportImportFilesMetadata metadata = checkpoint.ExportColumnFamily(cf, exportPath);

            using RocksDb target = OpenWith(targetDir.Path);
            using var cfOptions = new DbOptions();

            ColumnFamilyHandle imported = target.CreateColumnFamilyWithImport("payload", cfOptions, metadata);

            Assert.Equal("value0", target.GetString("key000", imported));
            Assert.Equal("value99", target.GetString("key099", imported));

            // Registered like any other, so it is findable by name.
            Assert.Same(imported, target.GetColumnFamily("payload"));
            Assert.Contains("payload", target.ColumnFamilyNames);

            // And absent from the default family.
            Assert.Null(target.GetString("key000"));
        }

        // Survives reopening the target, so the import is durable rather than
        // only visible to the session that did it.
        using RocksDb reopened = OpenWith(targetDir.Path, "payload");
        Assert.Equal("value50", reopened.GetString("key050", reopened.GetColumnFamily("payload")));
    }

    /// <summary>
    /// Moving rather than copying consumes the export, which is cheaper but
    /// single-use. The data must still arrive.
    /// </summary>
    [Fact]
    public void ImportWithMoveFiles_StillCarriesTheData()
    {
        using var sourceDir = new TempDir();
        using var targetDir = new TempDir();
        using var exportDir = new TempDir();

        using RocksDb source = OpenWith(sourceDir.Path, "payload");
        ColumnFamilyHandle cf = source.GetColumnFamily("payload");

        source.Put("key", "value", cf);
        source.Flush(cf);

        using Checkpoint checkpoint = Checkpoint.Create(source);
        using ExportImportFilesMetadata metadata =
            checkpoint.ExportColumnFamily(cf, Path.Combine(exportDir.Path, "cf"));

        using RocksDb target = OpenWith(targetDir.Path);
        using var cfOptions = new DbOptions();
        using var importOptions = new ImportColumnFamilyOptions { MoveFiles = true };

        ColumnFamilyHandle imported =
            target.CreateColumnFamilyWithImport("payload", cfOptions, metadata, importOptions);

        Assert.Equal("value", target.GetString("key", imported));
    }

    /// <summary>
    /// The imported family can be written to afterwards, so it is a real column
    /// family rather than a read-only attachment.
    /// </summary>
    [Fact]
    public void ImportedColumnFamily_AcceptsFurtherWrites()
    {
        using var sourceDir = new TempDir();
        using var targetDir = new TempDir();
        using var exportDir = new TempDir();

        using RocksDb source = OpenWith(sourceDir.Path, "payload");
        ColumnFamilyHandle cf = source.GetColumnFamily("payload");
        source.Put("existing", "value", cf);
        source.Flush(cf);

        using Checkpoint checkpoint = Checkpoint.Create(source);
        using ExportImportFilesMetadata metadata =
            checkpoint.ExportColumnFamily(cf, Path.Combine(exportDir.Path, "cf"));

        using RocksDb target = OpenWith(targetDir.Path);
        using var cfOptions = new DbOptions();
        ColumnFamilyHandle imported = target.CreateColumnFamilyWithImport("payload", cfOptions, metadata);

        target.Put("added", "later", imported);
        target.Delete("existing", imported);
        target.Flush(imported);

        Assert.Equal("later", target.GetString("added", imported));
        Assert.Null(target.GetString("existing", imported));
    }

    /// <summary>
    /// A name already in use must be rejected rather than silently replacing
    /// the existing family.
    /// </summary>
    [Fact]
    public void Import_IntoAnExistingName_Fails()
    {
        using var sourceDir = new TempDir();
        using var targetDir = new TempDir();
        using var exportDir = new TempDir();

        using RocksDb source = OpenWith(sourceDir.Path, "payload");
        ColumnFamilyHandle cf = source.GetColumnFamily("payload");
        source.Put("key", "value", cf);
        source.Flush(cf);

        using Checkpoint checkpoint = Checkpoint.Create(source);
        using ExportImportFilesMetadata metadata =
            checkpoint.ExportColumnFamily(cf, Path.Combine(exportDir.Path, "cf"));

        // The target already has a family of that name.
        using RocksDb target = OpenWith(targetDir.Path, "payload");
        using var cfOptions = new DbOptions();

        RocksDbException ex = Assert.Throws<RocksDbException>(
            () => target.CreateColumnFamilyWithImport("payload", cfOptions, metadata));

        // Named, not just any exception: a NullReferenceException from a wrapper
        // bug would have satisfied ThrowsAny just as well.
        Assert.Contains("Column family already exists", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An existing directory is rejected, even an empty one. Worth pinning: the
    /// natural thing to do is create the directory first, and that fails.
    /// </summary>
    [Fact]
    public void ExportColumnFamily_ToAnExistingDirectory_Fails()
    {
        using var dir = new TempDir();
        using RocksDb db = OpenWith(dir.Path, "cf1");
        ColumnFamilyHandle cf = db.GetColumnFamily("cf1");

        db.Put("key", "value", cf);
        db.Flush(cf);

        using Checkpoint checkpoint = Checkpoint.Create(db);

        RocksDbException ex = Assert.Throws<RocksDbException>(
            () => checkpoint.ExportColumnFamily(cf, dir.Sub("already-here")));

        Assert.Contains("exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportColumnFamily_RejectsNullArguments()
    {
        using var dir = new TempDir();
        using RocksDb db = OpenWith(dir.Path, "cf1");
        ColumnFamilyHandle cf = db.GetColumnFamily("cf1");

        using Checkpoint checkpoint = Checkpoint.Create(db);

        Assert.Throws<ArgumentNullException>(() => checkpoint.ExportColumnFamily(null!, dir.Path));
        Assert.Throws<ArgumentNullException>(() => checkpoint.ExportColumnFamily(cf, null!));
        Assert.Throws<ArgumentException>(() => checkpoint.ExportColumnFamily(cf, string.Empty));
    }

    [Fact]
    public void CreateColumnFamilyWithImport_RejectsNullArguments()
    {
        using var dir = new TempDir();
        using var exportDir = new TempDir();
        using RocksDb db = OpenWith(dir.Path, "cf1");
        ColumnFamilyHandle cf = db.GetColumnFamily("cf1");

        db.Put("key", "value", cf);
        db.Flush(cf);

        using Checkpoint checkpoint = Checkpoint.Create(db);
        using ExportImportFilesMetadata metadata =
            checkpoint.ExportColumnFamily(cf, Path.Combine(exportDir.Path, "cf"));

        using var cfOptions = new DbOptions();

        Assert.Throws<ArgumentNullException>(() => db.CreateColumnFamilyWithImport(null!, cfOptions, metadata));
        Assert.Throws<ArgumentException>(() => db.CreateColumnFamilyWithImport(string.Empty, cfOptions, metadata));
        Assert.Throws<ArgumentNullException>(() => db.CreateColumnFamilyWithImport("new", null!, metadata));
        Assert.Throws<ArgumentNullException>(() => db.CreateColumnFamilyWithImport("new", cfOptions, null!));
    }

    /// <summary>
    /// The metadata is independent of the database that produced it, so it can
    /// outlive both the checkpoint and the source.
    /// </summary>
    [Fact]
    public void Metadata_OutlivesTheSourceDatabase()
    {
        using var sourceDir = new TempDir();
        using var targetDir = new TempDir();
        using var exportDir = new TempDir();

        ExportImportFilesMetadata metadata;

        using (RocksDb source = OpenWith(sourceDir.Path, "payload"))
        {
            ColumnFamilyHandle cf = source.GetColumnFamily("payload");
            source.Put("key", "value", cf);
            source.Flush(cf);

            using Checkpoint checkpoint = Checkpoint.Create(source);
            metadata = checkpoint.ExportColumnFamily(cf, Path.Combine(exportDir.Path, "cf"));
        }

        // Source database and checkpoint are both closed.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        try
        {
            Assert.False(string.IsNullOrEmpty(metadata.DbComparatorName));

            using RocksDb target = OpenWith(targetDir.Path);
            using var cfOptions = new DbOptions();
            ColumnFamilyHandle imported = target.CreateColumnFamilyWithImport("payload", cfOptions, metadata);

            Assert.Equal("value", target.GetString("key", imported));
        }
        finally
        {
            metadata.Dispose();
        }
    }
}
