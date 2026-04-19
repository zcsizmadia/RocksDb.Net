namespace RocksDbNet.Tests;

public class CompactionReasonTests
{
    [Fact]
    public void Unknown_IsZero()
    {
        Assert.Equal(0u, (uint)CompactionReason.Unknown);
    }

    [Fact]
    public void AllValues_AreDefined()
    {
        // Verify a few key values
        Assert.Equal(1u, (uint)CompactionReason.LevelL0FilesNum);
        Assert.Equal(10u, (uint)CompactionReason.ManualCompaction);
        Assert.Equal(13u, (uint)CompactionReason.Ttl);
    }
}

public class FlushReasonTests
{
    [Fact]
    public void Others_IsZero()
    {
        Assert.Equal(0u, (uint)FlushReason.Others);
    }

    [Fact]
    public void WriteBufferFull_HasExpectedValue()
    {
        Assert.Equal(0x09u, (uint)FlushReason.WriteBufferFull);
    }
}

public class BackgroundErrorReasonTests
{
    [Fact]
    public void Flush_IsZero()
    {
        Assert.Equal(0u, (uint)BackgroundErrorReason.Flush);
    }
}

public class WriteStallConditionTests
{
    [Fact]
    public void Normal_IsZero()
    {
        Assert.Equal(0, (int)WriteStallCondition.Normal);
    }

    [Fact]
    public void Delayed_IsOne()
    {
        Assert.Equal(1, (int)WriteStallCondition.Delayed);
    }

    [Fact]
    public void Stopped_IsTwo()
    {
        Assert.Equal(2, (int)WriteStallCondition.Stopped);
    }
}
