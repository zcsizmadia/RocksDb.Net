namespace RocksDbNet.Tests;

public class RocksDbHandleTests
{
    private sealed class TestHandle : RocksDbHandle
    {
        public int DisposeHandleCalls { get; private set; }

        public TestHandle()
        {
        }

        public override void DisposeHandle()
        {
            DisposeHandleCalls++;
        }
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

        var self = RocksDbHandle.GetSelfFromPinnedIntPtr<TestHandle>(state);
        var recoveredNamePtr = RocksDbHandle.GetNameFromPinnedIntPtr(state);

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
        Assert.Throws<ArgumentNullException>(() => RocksDbHandle.GetSelfFromPinnedIntPtr<TestHandle>(IntPtr.Zero));
    }

    [Fact]
    public void GetSelfFromPinnedIntPtr_ThrowsOnWrongType()
    {
        using var opts = new DbOptions();
        opts.PinGarbageCollector();

        var state = opts.GetPinnedIntPtr();

        Assert.Throws<InvalidOperationException>(() => RocksDbHandle.GetSelfFromPinnedIntPtr<TestHandle>(state));

        opts.UnpinGarbageCollector();
    }
}
