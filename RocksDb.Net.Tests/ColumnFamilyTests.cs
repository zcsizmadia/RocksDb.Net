using System.Text;

namespace RocksDbNet.Tests;

public class ColumnFamilyTests
{
    [Fact]
    public void CreateColumnFamily_Works()
    {
        using var db = new TempDb();
        using var cfOpts = new DbOptions();

        using var cf = db.Db.CreateColumnFamily(cfOpts, "test_cf");

        Assert.NotNull(cf);
        Assert.Equal("test_cf", cf.Name);
    }

    /// <summary>
    /// A dropped family stops being one the database reports or resolves, and
    /// its name becomes free again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserted only that the call did not throw, which is what left the
    /// registry defect undetected: the name stayed in
    /// <see cref="RocksDb.ColumnFamilyNames"/>, the lookup went on returning a
    /// handle for a family that no longer existed, and the name could never be
    /// reused because creating it again threw a duplicate-key
    /// <see cref="ArgumentException"/> out of a private dictionary.
    /// </para>
    /// <para>
    /// Reading through <c>cf</c> after the drop is deliberately not asserted to
    /// fail. RocksDb keeps a dropped family's data alive until the last handle
    /// to it is destroyed, so that read still succeeds, and a test demanding
    /// otherwise would pin the opposite of the documented contract.
    /// </para>
    /// </remarks>
    [Fact]
    public void DropColumnFamily_DeregistersTheName()
    {
        using var db = new TempDb();
        using var cfOpts = new DbOptions();

        using (ColumnFamilyHandle cf = db.Db.CreateColumnFamily(cfOpts, "to_drop"))
        {
            db.Db.Put("k", "v", cf);

            Assert.Contains("to_drop", db.Db.ColumnFamilyNames);

            db.Db.DropColumnFamily(cf);

            Assert.DoesNotContain("to_drop", db.Db.ColumnFamilyNames);
            Assert.False(db.Db.TryGetColumnFamily("to_drop", out _));
            Assert.Throws<KeyNotFoundException>(() => db.Db.GetColumnFamily("to_drop"));
        }

        // The name is free again, and what comes back is a new empty family
        // rather than the dropped one reappearing.
        using ColumnFamilyHandle recreated = db.Db.CreateColumnFamily(cfOpts, "to_drop");

        Assert.Contains("to_drop", db.Db.ColumnFamilyNames);
        Assert.Null(db.Db.GetString("k", recreated));
    }

    [Fact]
    public void OpenWithColumnFamilies_Works()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
            new("cf2"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);

        var cf1 = db.GetColumnFamily("cf1");
        var cf2 = db.GetColumnFamily("cf2");

        Assert.NotNull(cf1);
        Assert.NotNull(cf2);
        Assert.Equal("cf1", cf1.Name);
        Assert.Equal("cf2", cf2.Name);
    }

    [Fact]
    public void PutGet_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("data"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var dataCf = db.GetColumnFamily("data");

        db.Put("key", "value", dataCf);
        var result = db.GetString("key", dataCf);

        Assert.Equal("value", result);
    }

    [Fact]
    public void KeyMayExist_ColumnFamily_ReturnsTrueForExistingKey()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("data"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var dataCf = db.GetColumnFamily("data");

        db.Put("key", "value", dataCf);
        db.Flush(dataCf);

        bool mayExist = db.KeyMayExist(Encoding.UTF8.GetBytes("key"), dataCf);

        Assert.True(mayExist);
    }

    [Fact]
    public void KeyMayExist_ColumnFamily_StringKey_ReturnsTrueForExistingKey()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("data"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var dataCf = db.GetColumnFamily("data");

        db.Put("key", "value", dataCf);
        db.Flush(dataCf);

        bool mayExist = db.KeyMayExist("key", dataCf);

        Assert.True(mayExist);
    }

    [Fact]
    public void ColumnFamilies_AreIsolated()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "default_value");
        db.Put("key", "cf1_value", cf1);

        Assert.Equal("default_value", db.GetString("key"));
        Assert.Equal("cf1_value", db.GetString("key", cf1));
    }

    [Fact]
    public void Delete_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "value", cf1);
        Assert.Equal("value", db.GetString("key", cf1));

        db.Delete("key", cf1);
        Assert.Null(db.GetString("key", cf1));
    }

    [Fact]
    public void DeleteRange_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("a", "1", cf1);
        db.Put("b", "2", cf1);
        db.Put("d", "4", cf1);

        db.DeleteRange(
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("c"),
            cf1);

        Assert.Null(db.GetString("a", cf1));
        Assert.Null(db.GetString("b", cf1));
        Assert.Equal("4", db.GetString("d", cf1));
    }

    [Fact]
    public void GetDefaultColumnFamily_Works()
    {
        using var db = new TempDb();

        using var defaultCf = db.Db.GetDefaultColumnFamily();
        Assert.NotNull(defaultCf);
        Assert.Equal("default", defaultCf.Name);
    }

    [Fact]
    public void ColumnFamilyHandle_Id_IsValid()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        uint id = cf1.Id;
        Assert.True(id > 0); // cf1 should have an id > 0 (default is 0)
    }

    [Fact]
    public void ColumnFamilyHandle_ToString_ReturnsName()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        Assert.Equal("cf1", cf1.ToString());
    }

    [Fact]
    public void ColumnFamilyDescriptor_DefaultOptions()
    {
        var desc = new ColumnFamilyDescriptor("test");

        Assert.Equal("test", desc.Name);
        Assert.NotNull(desc.Options);
    }

    [Fact]
    public void ListColumnFamilies_AfterCreate()
    {
        using var dir = new TempDir();

        using (var db = RocksDb.Open(new DbOptions { CreateIfMissing = true }, dir.Path))
        {
            using var cfOpts = new DbOptions();
            using var cf = db.CreateColumnFamily(cfOpts, "new_cf");
        }

        var families = RocksDb.ListColumnFamilies(new DbOptions(), dir.Path);
        Assert.Contains("default", families);
        Assert.Contains("new_cf", families);
    }

    [Fact]
    public void Flush_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("key", "value", cf1);
        db.Flush(cf1);
    }

    [Fact]
    public void Merge_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        opts.SetUInt64AddMergeOperator();

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default", opts),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);

        byte[] key = Encoding.UTF8.GetBytes("counter");
        byte[] val1 = BitConverter.GetBytes(1UL);
        byte[] val2 = BitConverter.GetBytes(2UL);

        db.Merge(key, val1);
        db.Merge(key, val2);

        byte[]? result = db.Get(key.AsSpan());
        Assert.NotNull(result);

        ulong merged = BitConverter.ToUInt64(result);
        Assert.Equal(3UL, merged);
    }

    [Fact]
    public void CompactRange_ColumnFamily()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };
        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        using var db = RocksDb.Open(opts, dir.Path, cfDescs);
        var cf1 = db.GetColumnFamily("cf1");

        db.Put("a", "1", cf1);
        db.Flush(cf1);

        db.CompactRange(cf1);
    }

    [Fact]
    public void OpenReadOnly_ColumnFamilies()
    {
        using var dir = new TempDir();
        using var opts = new DbOptions { CreateIfMissing = true, CreateMissingColumnFamilies = true };

        var cfDescs = new List<ColumnFamilyDescriptor>
        {
            new("default"),
            new("cf1"),
        };

        // Create and populate
        using (var db = RocksDb.Open(opts, dir.Path, cfDescs))
        {
            var cf1 = db.GetColumnFamily("cf1");
            db.Put("key", "value", cf1);
        }

        // Open read-only
        using var roOpts = new DbOptions();
        using var rodb = RocksDb.OpenReadOnly(roOpts, dir.Path, cfDescs);
        var roCf1 = rodb.GetColumnFamily("cf1");

        Assert.Equal("value", rodb.GetString("key", roCf1));
    }

    /// <summary>
    /// The default family stays in the listing after another family is created
    /// on a database that was opened without an explicit family list.
    /// </summary>
    /// <remarks>
    /// It did not. The dictionary behind the listing is empty for such a
    /// database, and the default name was substituted only while it stayed empty,
    /// so creating a family replaced the default in the listing instead of
    /// joining it. Found while adding GetAggregatedPropertyInt, which walks this
    /// listing and so reported a total that omitted the default family entirely.
    /// </remarks>
    [Fact]
    public void ColumnFamilyNames_KeepsTheDefaultAfterOneIsCreated()
    {
        using var db = new TempDb();

        Assert.Equal(["default"], db.Db.ColumnFamilyNames);

        using var cfOpts = new DbOptions();
        db.Db.CreateColumnFamily(cfOpts, "added");

        Assert.Contains("default", db.Db.ColumnFamilyNames);
        Assert.Contains("added", db.Db.ColumnFamilyNames);

        // And the lookup agrees with the listing, which is the inconsistency the
        // old behaviour created: this resolved a family the listing denied.
        Assert.All(
            db.Db.ColumnFamilyNames,
            name => Assert.Equal(name, db.Db.GetColumnFamily(name).Name));
    }

    /// <summary>
    /// The listing is not duplicated when the default family was named at open
    /// time, which is the case where it is already in the dictionary.
    /// </summary>
    [Fact]
    public void ColumnFamilyNames_DoesNotRepeatTheDefaultWhenItWasOpenedByName()
    {
        using var opts = new DbOptions
        {
            CreateIfMissing = true,
            CreateMissingColumnFamilies = true,
        };

        using RocksDb db = TestDb.OpenInMemory(opts, [new("default"), new("cf1")]);

        Assert.Equal(["default", "cf1"], db.ColumnFamilyNames);
    }
}
