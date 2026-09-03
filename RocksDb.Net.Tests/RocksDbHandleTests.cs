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

    [Fact]
    public void Handle_ZeroAfterDispose()
    {
        var opts = new DbOptions();
        opts.Dispose();

        Assert.Equal(IntPtr.Zero, opts.Handle);
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
