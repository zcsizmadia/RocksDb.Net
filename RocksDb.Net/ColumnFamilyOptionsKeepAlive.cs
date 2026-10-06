namespace RocksDbNet;

/// <summary>
/// Keeps the options of every column family a database has opened or created
/// alive until the database has closed.
/// </summary>
/// <remarks>
/// <para>
/// A column family keeps calling the comparator, merge operator, compaction
/// filter and similar objects attached to the options it was created with,
/// and those objects are released when the options are. The database's own
/// options were held from the start; column family options were not, so
/// disposing them after <c>CreateColumnFamily</c> returned, which a
/// <c>using</c> block does, destroyed a comparator the column family went on
/// calling. An abandoned database had the same hazard through finalization
/// order: <see cref="ColumnFamilyDescriptor"/> disposes the options it created
/// from its finalizer, and the descriptors and the database become unreachable
/// together.
/// </para>
/// <para>
/// A keep-alive rather than a holder, because the options still belong to the
/// caller. Closing one database must not dispose options the caller is about
/// to hand to the next: creating a database, closing it, then reopening it
/// read-only with the same descriptors is ordinary code, and disposing the
/// options on close was tried once and faulted for exactly that reason. A
/// disposal the caller asked for while the database was open is deferred and
/// then performed at <see cref="Release"/>; otherwise the options are left
/// alone.
/// </para>
/// </remarks>
internal sealed class ColumnFamilyOptionsKeepAlive
{
    private readonly List<DbOptions> _options = [];

    public void Add(DbOptions options)
    {
        options.AddKeepAlive();

        lock (_options)
        {
            _options.Add(options);
        }
    }

    public void Add(IReadOnlyList<ColumnFamilyDescriptor> descriptors)
    {
        foreach (ColumnFamilyDescriptor descriptor in descriptors)
        {
            Add(descriptor.Options);
        }
    }

    /// <summary>Lets go of every options object. Call after the database has closed.</summary>
    public void Release()
    {
        DbOptions[] held;

        lock (_options)
        {
            held = [.. _options];
            _options.Clear();
        }

        foreach (DbOptions options in held)
        {
            options.ReleaseKeepAlive();
        }
    }
}
