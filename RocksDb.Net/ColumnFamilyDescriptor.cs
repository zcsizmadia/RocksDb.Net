namespace RocksDbNet;

/// <summary>
/// Descriptor used when opening a database with multiple column families.
/// </summary>
public sealed class ColumnFamilyDescriptor
{
    /// <summary>Name of the column family (use <c>"default"</c> for the default CF).</summary>
    public string Name { get; }

    /// <summary>Options specific to this column family.</summary>
    public DbOptions Options { get; }

    internal bool OwnsOptions { get; }

    ~ColumnFamilyDescriptor()
    {
        if (OwnsOptions)
        {
            Options.Dispose();
        }
    }

    public ColumnFamilyDescriptor(string name, DbOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        Name = name;
        Options = options;
        OwnsOptions = false;
    }

    public ColumnFamilyDescriptor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Options = new DbOptions();
        OwnsOptions = true;
    }
}