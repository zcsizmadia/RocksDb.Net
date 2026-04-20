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

    public GCHandle PinGarbageCollector(string? name = null)
    {
        if (_gcHandle.IsAllocated)
        {
            return _gcHandle;
        }

        _gcHandle = GCHandle.Alloc(this);

        _namePtr = name is not null ? Marshal.StringToCoTaskMemUTF8(name) : IntPtr.Zero;

        return _gcHandle;
    }

    public nint GetPinnedIntPtr()
    {
        if (!_gcHandle.IsAllocated)
        {
            throw new InvalidOperationException("The object is not pinned. Call PinGarbageCollector() first.");
        }
        return GCHandle.ToIntPtr(_gcHandle);
    }

    public nint GetPinnedNameIntPtr()
    {
        if (!_gcHandle.IsAllocated)
        {
            throw new InvalidOperationException("The object is not pinned. Call PinGarbageCollector() first.");
        }
        return _namePtr;
    }

    public void UnpinGarbageCollector()
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

    public static T GetSelfFromPinnedIntPtr<T>(nint state) where T : RocksDbHandle
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

    public static nint GetNameFromPinnedIntPtr(nint state)
    {
        var self = GetSelfFromPinnedIntPtr<RocksDbHandle>(state);
        return self._namePtr;
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
        GC.SuppressFinalize(this); // Don't bother calling the destructor
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            // Already disposed, nothing to do
            return;
        }

        // Dispose unmanaged resources regardless of disposing value
        DisposeUnmanagedResources();
    }

    /// <summary>
    /// Releases the native handle associated with the underlying resource. This method is called during disposal to free unmanaged resources.
    /// </summary>
    public abstract void DisposeHandle();

    /// <summary>
    /// Releases unmanaged resources used by the current instance.
    /// </summary>
    public virtual void DisposeUnmanagedResources()
    {
        // Dispose the native handle if this instance owns it
        if (_owned != 0 && _handle != IntPtr.Zero)
        {
            DisposeHandle();
        }

        Handle = IntPtr.Zero;
    }
}
