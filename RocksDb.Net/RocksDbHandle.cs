using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Abstract base class for all managed wrappers around native RocksDb handles.
/// Provides deterministic disposal via <see cref="IDisposable"/> and a GC
/// finalizer safety net.
/// </summary>
public abstract class RocksDbHandle : IDisposable
{
    private nint _handle;
    private int _owned = 1; // Default to owned, meaning this instance is responsible for releasing the native handle.

    private GCHandle _gcHandle; // Keep the object alive while native code holds a reference to it, if pinned
    private nint _namePtr; // Pointer to the name string in unmanaged memory. Used only when object is pinned for native callbacks.

    protected RocksDbHandle()
    {
    }

    /// <summary>Takes over an already-created native handle.</summary>
    /// <param name="handle">The native handle to own.</param>
    protected RocksDbHandle(nint handle)
    {
        _handle = handle;
    }

    private int _disposed;

    /// <summary>
    /// Gets the native handle associated with the underlying resource.
    /// </summary>
    /// <remarks>The handle is typically used for interoperability with unmanaged code or system APIs. The
    /// value may be IntPtr.Zero if the resource has not been initialized or has been released.</remarks>
    public nint Handle { get => _handle; protected set => _handle = value; }

    /// <summary>
    /// Indicating whether this instance is owned or managed by the current object.
    /// If true, the object is responsible for releasing the native handle during disposal;
    /// if false, the handle is managed externally and should not be released by this instance.
    /// </summary>
    public bool Owned { get => _owned != 0; protected init => _owned = value ? 1 : 0; }

    /// <summary>
    /// Gets a value indicating whether the object has been disposed.
    /// </summary>
    /// <remarks>Use this property to determine if the object is no longer usable due to disposal. Accessing
    /// members of a disposed object may result in exceptions.</remarks>
    public bool IsDisposed => _disposed != 0;

    ~RocksDbHandle()
    {
        Dispose(false);
    }

    /// <summary>
    /// Marks this handle as having its ownership transferred to a native
    /// object (e.g. when set on options). After this call, <see cref="Dispose()"/>
    /// will not destroy the native handle, preventing double-free crashes.
    /// </summary>
    internal void TransferOwnership() => Interlocked.Exchange(ref _owned, 0);

    // ── Shared attachment ────────────────────────────────────────────────────

    // How many live objects have attached this handle and will release it.
    //
    // RocksDb attaches callback objects in three different ways, and only one
    // of them is safe to treat as a straight transfer of ownership:
    //
    //   * A raw pointer, as for a comparator, compaction filter, env or WAL
    //     filter. RocksDb never frees these, so the wrapper must, and must not
    //     do so while anything still points at them.
    //   * A copy of an existing shared_ptr, as for a logger or rate limiter.
    //     The native object is genuinely shared and outlives any one holder.
    //   * A fresh shared_ptr or unique_ptr built from the raw pointer, as for a
    //     merge operator, event listener, compaction filter factory, prefix
    //     extractor or filter policy. Those really are transfers, and handing
    //     the same instance over twice creates two independent native owners
    //     that both delete it. Those setters reject a second attachment rather
    //     than count it; see AttachExclusively.
    //
    // For the first two, whoever attached the handle releases it, and the
    // native release happens when the last of them does. That is what stops
    // one options object destroying a comparator that another options object,
    // or an open database, is still calling.
    private int _holders;

    /// <summary>
    /// Records that a native object now holds this handle and will release it.
    /// </summary>
    internal void AddHolder()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _holders);
    }

    /// <summary>
    /// Releases one holder's claim, disposing the handle when it was the last.
    /// </summary>
    internal void ReleaseHolder()
    {
        int remaining = Interlocked.Decrement(ref _holders);

        if (remaining > 0)
        {
            // Something else still points at this, so releasing it now would
            // pull it out from under code still using it.
            return;
        }

        // The count is now zero, so the deferral in Dispose no longer applies
        // and this performs the real release.
        Dispose();
    }

    /// <summary>
    /// Attaches this handle to a native object that takes exclusive ownership
    /// of it, and rejects a second attempt.
    /// </summary>
    /// <param name="member">
    /// The member being assigned, used in the exception message.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The handle is already attached somewhere.
    /// </exception>
    /// <remarks>
    /// The native setters this guards each wrap the raw pointer in a
    /// <c>shared_ptr</c> or <c>unique_ptr</c> of their own, so two options
    /// objects given the same instance become two independent owners and both
    /// delete it. That corrupts the heap at teardown, a long way from the
    /// assignment that caused it, so this turns it into an exception naming the
    /// mistake instead.
    /// </remarks>
    internal void AttachExclusively(string member)
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(ref _holders, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                $"This {GetType().Name} is already attached to something else and cannot also be " +
                $"assigned to {member}. RocksDb takes exclusive ownership of it, so two owners " +
                "would each destroy it and corrupt the heap. Create a separate instance instead.");
        }

        TransferOwnership();
    }

    /// <summary>
    /// Pins this instance so native code can hold a pointer to it across
    /// callbacks, optionally alongside a stable copy of its name.
    /// </summary>
    /// <param name="name">
    /// A name to keep in unmanaged memory for the native name callback to
    /// return, or <see langword="null"/> if the type has no name callback.
    /// </param>
    /// <returns>The allocated handle, already stored on this instance.</returns>
    /// <remarks>
    /// Call this before handing any function pointer to RocksDb. Without it
    /// the garbage collector is free to move or collect the instance while
    /// native code still holds its address, and the callback then runs against
    /// freed memory. Release it from the native destructor callback with
    /// <see cref="UnpinGarbageCollector"/>.
    /// </remarks>
    protected GCHandle PinGarbageCollector(string? name = null)
    {
        if (_gcHandle.IsAllocated)
        {
            return _gcHandle;
        }

        _gcHandle = GCHandle.Alloc(this);

        _namePtr = name is not null ? Marshal.StringToCoTaskMemUTF8(name) : IntPtr.Zero;

        return _gcHandle;
    }

    /// <summary>
    /// The pointer to pass to RocksDb as the callback state, which comes back
    /// to <see cref="GetSelfFromPinnedIntPtr{T}"/> on every callback.
    /// </summary>
    /// <returns>A pointer to the pinned handle for this instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="PinGarbageCollector"/> has not been called.
    /// </exception>
    protected nint GetPinnedIntPtr()
    {
        if (!_gcHandle.IsAllocated)
        {
            throw new InvalidOperationException("The object is not pinned. Call PinGarbageCollector() first.");
        }
        return GCHandle.ToIntPtr(_gcHandle);
    }

    /// <summary>
    /// The unmanaged copy of this instance's name, for a native name callback
    /// to return directly.
    /// </summary>
    /// <returns>A pointer to a null-terminated copy of the name.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="PinGarbageCollector"/> has not been called.
    /// </exception>
    /// <remarks>
    /// The name has to live in unmanaged memory because RocksDb keeps the
    /// pointer it is given rather than copying the string.
    /// </remarks>
    protected nint GetPinnedNameIntPtr()
    {
        if (!_gcHandle.IsAllocated)
        {
            throw new InvalidOperationException("The object is not pinned. Call PinGarbageCollector() first.");
        }
        return _namePtr;
    }

    /// <summary>
    /// Releases the pin taken by <see cref="PinGarbageCollector"/> and frees
    /// the unmanaged name copy.
    /// </summary>
    /// <remarks>
    /// Call this from the native destructor callback, which RocksDb invokes
    /// when it is finished with the object. Unpinning earlier leaves native
    /// code holding a dangling state pointer.
    /// </remarks>
    protected internal void UnpinGarbageCollector()
    {
        if (!_gcHandle.IsAllocated)
        {
            throw new InvalidOperationException("The object is not pinned. Call PinGarbageCollector() first.");
        }

        _gcHandle.Free();

        if (_namePtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_namePtr);
            _namePtr = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Recovers the managed instance from the state pointer RocksDb passes to
    /// a callback.
    /// </summary>
    /// <typeparam name="T">The expected instance type.</typeparam>
    /// <param name="state">The state pointer given to the callback.</param>
    /// <returns>The instance the pointer refers to.</returns>
    /// <exception cref="InvalidOperationException">
    /// The pointer is null, the handle is no longer allocated, or the target is
    /// not a <typeparamref name="T"/>.
    /// </exception>
    protected static T GetSelfFromPinnedIntPtr<T>(nint state) where T : RocksDbHandle
    {
        if (state == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(state), "The pinned state pointer cannot be null.");
        }
        GCHandle handle = GCHandle.FromIntPtr(state);
        if (!handle.IsAllocated || handle.Target is not T self)
        {
            throw new InvalidOperationException("The pinned state does not reference a valid instance of the expected type.");
        }
        return self;
    }

    /// <summary>
    /// Recovers the unmanaged name pointer for the instance behind a callback
    /// state pointer.
    /// </summary>
    /// <param name="state">The state pointer given to the callback.</param>
    /// <returns>A pointer to the null-terminated name.</returns>
    protected static nint GetNameFromPinnedIntPtr(nint state)
    {
        var self = GetSelfFromPinnedIntPtr<RocksDbHandle>(state);
        return self._namePtr;
    }

    // RocksDb dereferences the name pointer without a null check, so there is no
    // safe way to report failure from a name callback. Hand out a fixed
    // placeholder instead of letting the exception reach native code. Allocated
    // once and never freed, which is fine for a process-lifetime constant.
    private static readonly nint FallbackNamePtr = Marshal.StringToCoTaskMemUTF8("rocksdbnet.unknown");

    /// <summary>
    /// Name callback used by every callback-based wrapper. Exceptions cannot
    /// cross into native code, so a failure yields a placeholder name.
    /// </summary>
    internal static nint GetNameFromPinnedIntPtrSafe(nint state)
    {
        try
        {
            nint name = GetNameFromPinnedIntPtr(state);
            return name != nint.Zero ? name : FallbackNamePtr;
        }
        catch (Exception ex)
        {
            RocksDbCallbacks.Report("Name", ex, state);
            return FallbackNamePtr;
        }
    }

    /// <summary>
    /// Throws an exception if the object has been disposed.
    /// </summary>
    /// <remarks>Call this method before performing operations that require the object to be in a valid,
    /// non-disposed state. This helps prevent accessing resources that have already been released.</remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the object has already been disposed.</exception>
    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    /// <remarks>Call this method when the instance is no longer needed to free unmanaged resources promptly.
    /// After calling this method, the instance should not be used.</remarks>
    public virtual void Dispose()
    {
        Dispose(true);

        // Only when the release actually happened. While a holder still has
        // this handle attached, Dispose defers and the finalizer has to stay
        // registered as the safety net for the case where no holder ever
        // releases it either.
        if (IsDisposed)
        {
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        // Deferred while a native object still holds this handle. The common
        // shape is `using var cmp = new MyComparator(); opts.Comparator = cmp;`,
        // where the using block ends long before the options do; releasing the
        // comparator there would leave RocksDb calling freed memory. Whichever
        // holder lets go last performs the real release, so nothing is freed
        // early and nothing is leaked. See AddHolder.
        //
        // Checked here rather than only in the public Dispose so that the
        // finalizer respects it too. That matters more than it looks: an
        // attached object and the options holding it become unreachable
        // together and are finalized in no particular order, so without this
        // the finalizer could release a comparator while the options still
        // pointed at it. That was an access violation, not a leak.
        if (Volatile.Read(ref _holders) > 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            // Already disposed, nothing to do
            return;
        }

        // Dispose unmanaged resources regardless of disposing value
        DisposeUnmanagedResources();
    }

    /// <summary>
    /// Releases the native handle. Called during disposal.
    /// </summary>
    /// <remarks>
    /// Protected rather than public: it destroys the native object without
    /// marking this instance disposed or clearing the handle, so calling it from
    /// outside and then disposing normally would free the same pointer twice.
    /// It was the most Dispose-looking member on the type. Callers want
    /// <see cref="Dispose()"/>.
    /// </remarks>
    protected abstract void DisposeHandle();

    /// <summary>
    /// Releases unmanaged resources used by the current instance.
    /// </summary>
    /// <remarks>Protected for the same reason as <see cref="DisposeHandle"/>.</remarks>
    protected virtual void DisposeUnmanagedResources()
    {
        // Dispose the native handle if this instance owns it, and if whatever it
        // lives inside is still open. See SetParent for why the second condition
        // exists.
        if (_owned != 0 && _handle != IntPtr.Zero && _parent?.IsDisposed != true)
        {
            DisposeHandle();
        }

        Handle = IntPtr.Zero;
        _parent = null;
    }

    // The object this handle lives inside, or null for a root handle. Held as a
    // strong reference on purpose: see SetParent.
    private RocksDbHandle? _parent;

    /// <summary>
    /// Records that this handle is only valid while <paramref name="parent"/> is
    /// open, and must be released before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RocksDb requires iterators, snapshots and column family handles to be
    /// destroyed before the database, because their native destructors reach into
    /// database internals. Nothing enforced that here, so forgetting to dispose
    /// one turned into a crash rather than a leak: the finalizer ran after the
    /// database had already been closed and dereferenced freed memory, or a null
    /// pointer, on the finalizer thread where nothing can catch it.
    /// </para>
    /// <para>
    /// Two things fix that together. The strong reference keeps the parent
    /// reachable for as long as this handle is, so the parent's finalizer cannot
    /// run first. And when the parent has already been disposed explicitly, the
    /// check in <see cref="DisposeUnmanagedResources"/> skips the native release
    /// entirely, because the parent's own close already reclaimed what it
    /// referred to. That leaks a small wrapper struct in a case that used to
    /// terminate the process.
    /// </para>
    /// </remarks>
    internal void SetParent(RocksDbHandle parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _parent = parent;
    }
}
