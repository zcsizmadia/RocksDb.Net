using System.Runtime.InteropServices;

namespace RocksDbNet;

/// <summary>
/// Abstract base class for all managed wrappers around native RocksDB handles.
/// Provides deterministic disposal via <see cref="IDisposable"/> and a GC
/// finalizer safety net.
/// </summary>
public abstract class RocksDbHandle : IDisposable
{
    private IntPtr _handle;
    private int _disposed;
    private int _ownershipTransferred;

    /// <summary>
    /// Gets the native handle associated with the underlying resource.
    /// </summary>
    /// <remarks>The handle is typically used for interoperability with unmanaged code or system APIs. The
    /// value may be IntPtr.Zero if the resource has not been initialized or has been released.</remarks>
    public IntPtr Handle { get => _handle; protected set => _handle = value; }

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
    internal void TransferOwnership()
    {
        Interlocked.Exchange(ref _ownershipTransferred, 1);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Throws an exception if the object has been disposed.
    /// </summary>
    /// <remarks>Call this method before performing operations that require the object to be in a valid,
    /// non-disposed state. This helps prevent accessing resources that have already been released.</remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the object has already been disposed.</exception>
    public void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

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

        if (disposing)
        {
            DisposeManagedResources();
        }

        if (_ownershipTransferred == 0)
        {
            DisposeUnmanagedResources();
        }

        Handle = IntPtr.Zero;
    }

    /// <summary>
    /// Releases unmanaged resources used by the current instance.
    /// </summary>
    public abstract void DisposeUnmanagedResources();

    //public abstract void DisposeHandle();

    /// <summary>
    /// Releases managed resources used by the current instance.
    /// </summary>
    public virtual void DisposeManagedResources()
    {
    }
}
