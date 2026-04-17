using System.Reflection.Metadata;

using RocksDbNet.Native;

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

    public ColumnFamilyDescriptor(string name, DbOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        Name = name;
        Options = options;
    }

    public ColumnFamilyDescriptor(string name) : this(name, new DbOptions()) { }
}