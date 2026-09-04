using System.Runtime.InteropServices;

namespace RocksDbNet.Tests;

public class RocksDbHandleTests
{
    /// <summary>
    /// The pinning helpers are <c>protected</c>, so they are reachable only from
    /// a derived type. That is how a real callback wrapper uses them, and these
    /// pass-throughs let the tests exercise them the same way.
    /// </summary>
    private sealed class TestHandle : RocksDbHandle
    {
        public int DisposeHandleCalls { get; private set; }

        public TestHandle()
        {
        }

        protected override void DisposeHandle()
        {
            DisposeHandleCalls++;
        }

        public new GCHandle PinGarbageCollector(string? name = null) => base.PinGarbageCollector(name);

        public new nint GetPinnedIntPtr() => base.GetPinnedIntPtr();

        public new nint GetPinnedNameIntPtr() => base.GetPinnedNameIntPtr();

        public static TestHandle SelfFrom(nint state) => GetSelfFromPinnedIntPtr<TestHandle>(state);

        public static nint NameFrom(nint state) => GetNameFromPinnedIntPtr(state);
    }

    [Fact]
    public void Dispose_SetsIsDisposed()
    {
        var opts = new DbOptions();
        Assert.False(opts.IsDisposed);

        opts.Dispose();
        Assert.True(opts.IsDisposed);
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var opts = new DbOptions();

        opts.Dispose();
        opts.Dispose(); // Should not throw
    }

    [Fact]
    public void ThrowIfDisposed_ThrowsAfterDispose()
    {
        var opts = new DbOptions();
        opts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => opts.ThrowIfDisposed());
    }

    [Fact]
    public void ThrowIfDisposed_DoesNotThrowBeforeDispose()
    {
        using var opts = new DbOptions();

        opts.ThrowIfDisposed(); // Should not throw
    }

    /// <summary>
    /// Reading the handle after disposal throws rather than handing back a
    /// zero the C API would dereference.
    /// </summary>
    [Fact]
    public void Handle_ThrowsAfterDispose()
    {
        var opts = new DbOptions();
        opts.Dispose();

        var ex = Assert.Throws<ObjectDisposedException>(() => opts.Handle);

        // The message has to name the type, since that is all the caller gets
        // to identify which of their objects was already gone.
        Assert.Contains(nameof(DbOptions), ex.Message);
    }

    /// <summary>
    /// Zero before the native object exists is still a legitimate value, so
    /// only disposal throws.
    /// </summary>
    [Fact]
    public void Handle_IsZeroBeforeTheNativeObjectExists()
    {
        using var handle = new TestHandle();

        Assert.Equal(IntPtr.Zero, handle.Handle);
    }

    /// <summary>
    /// Disposal reads the handle to pass it to the native destructor, so the
    /// guard must not fire while the release is still running.
    /// </summary>
    [Fact]
    public void Handle_IsReadableWhileBeingReleased()
    {
        using var handle = new HandleReadingOnDispose();

        handle.Dispose();

        Assert.True(handle.ReadTheHandle);
        Assert.Null(handle.Failure);
    }

    /// <summary>
    /// A handle still held by a native object defers its disposal, and until
    /// the last holder lets go it has to stay fully usable.
    /// </summary>
    [Fact]
    public void Handle_StaysReadableWhileAHolderKeepsItAlive()
    {
        var opts = new DbOptions();
        nint before = opts.Handle;

        opts.AddHolder();
        opts.Dispose();

        // Deferred, so nothing has been released and the handle still works.
        Assert.False(opts.IsDisposed);
        Assert.Equal(before, opts.Handle);

        opts.ReleaseHolder();

        Assert.True(opts.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => opts.Handle);
    }

    /// <summary>Disposing twice is still a no-op, not a second release.</summary>
    [Fact]
    public void Handle_ThrowsAfterRepeatedDispose()
    {
        var opts = new DbOptions();

        opts.Dispose();
        opts.Dispose();

        Assert.Throws<ObjectDisposedException>(() => opts.Handle);
    }

    /// <summary>
    /// A wrapper that reads its own handle while releasing it, which is what
    /// every DisposeHandle override in the library does.
    /// </summary>
    private sealed class HandleReadingOnDispose : RocksDbHandle
    {
        public HandleReadingOnDispose()
            : base(new nint(0x1234))
        {
        }

        public bool ReadTheHandle { get; private set; }

        public Exception? Failure { get; private set; }

        protected override void DisposeHandle()
        {
            try
            {
                ReadTheHandle = Handle == new nint(0x1234);
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
        }
    }

    [Fact]
    public void PinAndUnpin_RoundTripsSelfAndName()
    {
        var handle = new TestHandle();

        handle.PinGarbageCollector("my-name");
        var state = handle.GetPinnedIntPtr();
        var namePtr = handle.GetPinnedNameIntPtr();

        var self = TestHandle.SelfFrom(state);
        var recoveredNamePtr = TestHandle.NameFrom(state);

        Assert.Same(handle, self);
        Assert.Equal(namePtr, recoveredNamePtr);

        handle.UnpinGarbageCollector();
    }

    [Fact]
    public void PinTwice_ReturnsSameGcHandle()
    {
        var handle = new TestHandle();

        var h1 = handle.PinGarbageCollector();
        var h2 = handle.PinGarbageCollector();

        Assert.Equal(h1, h2);

        handle.UnpinGarbageCollector();
    }

    [Fact]
    public void GetPinnedIntPtr_ThrowsWhenNotPinned()
    {
        var handle = new TestHandle();
        Assert.Throws<InvalidOperationException>(() => handle.GetPinnedIntPtr());
    }

    [Fact]
    public void GetPinnedNameIntPtr_ThrowsWhenNotPinned()
    {
        var handle = new TestHandle();
        Assert.Throws<InvalidOperationException>(() => handle.GetPinnedNameIntPtr());
    }

    [Fact]
    public void UnpinGarbageCollector_ThrowsWhenNotPinned()
    {
        var handle = new TestHandle();
        Assert.Throws<InvalidOperationException>(() => handle.UnpinGarbageCollector());
    }

    [Fact]
    public void GetSelfFromPinnedIntPtr_ThrowsOnZero()
    {
        Assert.Throws<ArgumentNullException>(() => TestHandle.SelfFrom(IntPtr.Zero));
    }

    [Fact]
    public void GetSelfFromPinnedIntPtr_ThrowsOnWrongType()
    {
        // Pin a handle of a different type, then ask for it as a TestHandle.
        var other = new OtherHandle();
        other.PinGarbageCollector();

        try
        {
            nint state = other.GetPinnedIntPtr();

            Assert.Throws<InvalidOperationException>(() => TestHandle.SelfFrom(state));
        }
        finally
        {
            other.UnpinGarbageCollector();
        }
    }

    private sealed class OtherHandle : RocksDbHandle
    {
        protected override void DisposeHandle()
        {
        }

        public new GCHandle PinGarbageCollector(string? name = null) => base.PinGarbageCollector(name);

        public new nint GetPinnedIntPtr() => base.GetPinnedIntPtr();
    }
}
