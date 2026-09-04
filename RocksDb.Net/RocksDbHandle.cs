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

    // Three states rather than two, because the handle has to stay readable
    // while it is being released. Alive, releasing, released.
    private const int Alive = 0;
    private const int Releasing = 1;
    private const int Released = 2;

    private int _disposed;

    /// <summary>
    /// Gets the native handle associated with the underlying resource, for
    /// interoperability with unmanaged code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading this after disposal throws rather than returning
    /// <see cref="IntPtr.Zero"/>. The C API dereferences whatever it is given
    /// without a null check, so a zero handle reaching it is an access
    /// violation that takes the process down, with a stack that says nothing
    /// about the disposed object that caused it. Every use-after-dispose in
    /// the library passes through here, so one guard turns all of them into a
    /// named exception. Use <see cref="IsDisposed"/> to ask the question
    /// without throwing.
    /// </para>
    /// <para>
    /// The value is <see cref="IntPtr.Zero"/> before the native object has
    /// been created, which a wrapper constructed but not yet opened will show.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public nint Handle
    {
        get
        {
            // Releasing, not Released: disposal itself reads this to hand the
            // pointer to the native destructor, and every DisposeHandle
            // override would break if the guard fired then.
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == Released, this);
            return _handle;
        }

        protected set => _handle = value;
    }

    /// <summary>
    /// Indicating whether this instance is owned or managed by the current object.
    /// If true, the object is responsible for releasing the native handle during disposal;
    /// if false, the handle is managed externally and should not be released by this instance.
    /// </summary>
    public bool Owned { get => _owned != 0; protected init => _owned = value ? 1 : 0; }

    /// <summary>
    /// Gets a value indicating whether the object has been disposed.
    /// </summary>
    /// <remarks>
    /// True from the moment disposal begins, not from when it finishes, so a
    /// half-released object never looks usable. Reading <see cref="Handle"/> on
    /// one of these throws.
    /// </remarks>
    public bool IsDisposed => Volatile.Read(ref _disposed) != Alive;

    // Disposal finished, as opposed to merely started. The child guard needs
    // this rather than IsDisposed: a parent that is midway through its own
    // teardown is disposing its children on purpose, and they must still
    // release. Only once the parent has closed has RocksDb freed them for us.
    internal bool IsReleased => Volatile.Read(ref _disposed) == Released;

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
    /// Whether <see cref="PinGarbageCollector"/> has run, and so whether
    /// <see cref="UnpinGarbageCollector"/> can be called without throwing.
    /// </summary>
    /// <remarks>
    /// For the finalizer path. A derived constructor that throws while
    /// evaluating the arguments it passes to <c>base(...)</c> leaves an
    /// allocated, finalizable object on which no constructor ever ran, so
    /// nothing pinned it. Unpinning that throws, and an exception from a
    /// finalizer is unhandled and takes the process with it.
    /// </remarks>
    protected bool IsPinned => _gcHandle.IsAllocated;

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

        if (Interlocked.CompareExchange(ref _disposed, Releasing, Alive) != Alive)
        {
            // Already disposed, or being disposed, nothing to do
            return;
        }

        try
        {
            // Dispose unmanaged resources regardless of disposing value
            DisposeUnmanagedResources();
        }
        finally
        {
            // Only now does Handle start throwing. In a finally because a
            // release that threw halfway must still leave the object unusable
            // rather than stuck looking half-alive.
            Volatile.Write(ref _disposed, Released);
        }
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
        // Whatever lives inside this handle goes first. RocksDb requires it, and
        // for a handle being finalized it is the only chance they get: their own
        // finalizers may not have run yet and may never run in time.
        DisposeChildren();

        // Then this handle, if it owns one and if whatever it lives inside has
        // not already been closed. A parent that is midway through its own
        // teardown does not count as closed: it is disposing this handle on
        // purpose, and skipping the release there would leak. See SetParent.
        if (_owned != 0 && _handle != IntPtr.Zero && _parent?.IsReleased != true)
        {
            DisposeHandle();
        }

        Handle = IntPtr.Zero;

        // Out of the parent's list, so a long-lived database does not accumulate
        // every iterator and snapshot ever opened against it.
        RocksDbHandle? parent = _parent;
        _parent = null;
        parent?.RemoveChild(this);
    }

    // The object this handle lives inside, or null for a root handle. Held as a
    // strong reference on purpose: see SetParent.
    private RocksDbHandle? _parent;

    // The handles that live inside this one, held as strong references for as
    // long as they are open. See SetParent for why both directions are needed.
    private List<RocksDbHandle>? _children;
    private readonly object _childGate = new();

    private void AddChild(RocksDbHandle child)
    {
        lock (_childGate)
        {
            (_children ??= []).Add(child);
        }
    }

    private void RemoveChild(RocksDbHandle child)
    {
        lock (_childGate)
        {
            _children?.Remove(child);
        }
    }

    /// <summary>
    /// Releases every handle that lives inside this one, before this one goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Last opened, first released. Children nest: an iterator is opened over a
    /// column family that already existed, so releasing in reverse order takes
    /// the iterator down before the family it reads from.
    /// </para>
    /// <para>
    /// The list is taken and cleared under the lock and the children disposed
    /// outside it, because each of them calls back into
    /// <see cref="RemoveChild"/> as it goes.
    /// </para>
    /// </remarks>
    private void DisposeChildren()
    {
        RocksDbHandle[] open;

        // A handle whose base constructor never ran has no children, and
        // locking on its null gate would throw. That instance exists: a derived
        // constructor that throws while evaluating the arguments it passes to
        // base(...) leaves an allocated object with every base field at its
        // default, and the finalizer still runs on it. An exception from a
        // finalizer takes the process down, so this is checked rather than
        // assumed.
        object? gate = _childGate;

        if (gate is null)
        {
            return;
        }

        lock (gate)
        {
            if (_children is not { Count: > 0 })
            {
                return;
            }

            open = [.. _children];
            _children.Clear();
        }

        for (int i = open.Length - 1; i >= 0; i--)
        {
            open[i].Dispose();
        }
    }

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
    /// Three things fix that together. The strong reference upwards keeps the
    /// parent reachable for as long as this handle is, so the parent's finalizer
    /// cannot run first. The strong reference downwards, which the parent keeps,
    /// means the parent releases this handle as part of its own teardown rather
    /// than leaving it to a finalizer that may never run in time. And when the
    /// parent has already finished closing, the check in
    /// <see cref="DisposeUnmanagedResources"/> skips the native release
    /// entirely, because the parent's own close already reclaimed what it
    /// referred to.
    /// </para>
    /// <para>
    /// The downward reference is what makes the check safe. Without it, a
    /// snapshot nobody disposed could be finalized on the finalizer thread at
    /// the same moment the database was closing on another, and release itself
    /// against a database mid-close. That was an access violation, and it is why
    /// the check was once written against
    /// <see cref="IsDisposed"/> instead: skipping the release for every child of
    /// a closing parent avoided the race by leaking all of them. Holding the
    /// children reachable removes the race instead, because a handle the parent
    /// still points at cannot be collected while the parent is in use.
    /// </para>
    /// </remarks>
    internal void SetParent(RocksDbHandle parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        _parent = parent;
        parent.AddChild(this);
    }
}
