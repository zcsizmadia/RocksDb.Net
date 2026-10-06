using System.Diagnostics.CodeAnalysis;

namespace RocksDbNet;

/// <summary>
/// The column family handles a database knows by name, shared by
/// <see cref="RocksDb"/>, <see cref="TransactionDb"/> and
/// <see cref="OptimisticTransactionDb"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member takes the same lock. The databases are documented as safe to
/// call from several threads at once, and this used to be a bare
/// <see cref="Dictionary{TKey, TValue}"/> that creating or dropping a family
/// wrote while lookups on other threads read it. That is not a supported use
/// of a dictionary: a lookup could throw, miss, or spin forever on a bucket
/// chain a concurrent resize had left looping.
/// </para>
/// <para>
/// The default family is resolved here too, under the same lock, so two
/// threads asking for it at once create one handle rather than two.
/// </para>
/// </remarks>
internal sealed class ColumnFamilyRegistry
{
    internal const string DefaultName = "default";

    private readonly Dictionary<string, ColumnFamilyHandle> _handles = [];
    private readonly object _gate = new();

    // Cached, because each native lookup allocates a fresh
    // rocksdb_column_family_handle_t, so resolving it per call leaked one.
    private ColumnFamilyHandle? _default;

    /// <summary>Registers a family. Throws if the name is already registered.</summary>
    public void Add(string name, ColumnFamilyHandle handle)
    {
        lock (_gate)
        {
            _handles.Add(name, handle);
        }
    }

    /// <summary>Registers a family, replacing any handle already under the name.</summary>
    public void Set(string name, ColumnFamilyHandle handle)
    {
        lock (_gate)
        {
            _handles[name] = handle;
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            _handles.Remove(name);
        }
    }

    /// <summary>
    /// Looks up a family by name, resolving the default family through
    /// <paramref name="createDefault"/> when it was not registered by name.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// The handle registered under <paramref name="name"/> has been disposed.
    /// Handing it back would only move the same exception to the caller's next
    /// use of it, without saying where the handle came from, and RocksDb has
    /// no call that reopens a family's handle by name.
    /// </exception>
    public bool TryGet(string name, Func<ColumnFamilyHandle> createDefault,
        [NotNullWhen(true)] out ColumnFamilyHandle? handle)
    {
        lock (_gate)
        {
            if (_handles.TryGetValue(name, out ColumnFamilyHandle? registered))
            {
                if (!registered.IsDisposed)
                {
                    handle = registered;
                    return true;
                }

                if (name != DefaultName)
                {
                    throw new ObjectDisposedException(nameof(ColumnFamilyHandle),
                        $"The handle for column family '{name}' has been disposed. The handle a " +
                        "database registers for a family is the only one it has, so it must stay " +
                        "undisposed for as long as the family is looked up by name.");
                }
            }

            // Every database has a default family, even one opened without
            // naming any, so resolve it on demand rather than reporting it as
            // unknown.
            if (name == DefaultName)
            {
                handle = GetDefaultLocked(createDefault);
                return true;
            }

            handle = null;
            return false;
        }
    }

    /// <summary>The cached default family handle, created on first use.</summary>
    /// <remarks>
    /// Replaced rather than handed back when a caller has disposed it. Unlike
    /// a named family, the default one can always be resolved again, so a
    /// stray <c>using</c> around it need not break every later lookup.
    /// </remarks>
    public ColumnFamilyHandle GetDefault(Func<ColumnFamilyHandle> create)
    {
        lock (_gate)
        {
            return GetDefaultLocked(create);
        }
    }

    private ColumnFamilyHandle GetDefaultLocked(Func<ColumnFamilyHandle> create)
    {
        if (_default is { IsDisposed: false })
        {
            return _default;
        }

        return _default = create();
    }

    /// <summary>
    /// The registered names, with the default family always included whether
    /// or not it was named when the database was opened.
    /// </summary>
    public IReadOnlyCollection<string> Names
    {
        get
        {
            lock (_gate)
            {
                return _handles.ContainsKey(DefaultName)
                    ? [.. _handles.Keys]
                    : [DefaultName, .. _handles.Keys];
            }
        }
    }
}
