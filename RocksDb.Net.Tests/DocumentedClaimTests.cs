namespace RocksDbNet.Tests;

/// <summary>
/// Claims in the API documentation that were wrong, pinned so they cannot drift
/// back. See issue #124.
/// </summary>
/// <remarks>
/// The documentation pass that produced these was checked by measurement rather
/// than by reading, because the previous pass got two of its own corrections
/// wrong. What is asserted here is what was measured.
/// </remarks>
public class DocumentedClaimTests
{
    private sealed record Written(string PolicyName, ulong FileSize, int ReadableCount, int UserCount);

    private static Written WriteWith(FilterPolicy policy)
    {
        using var dir = new TempDir();

        var listener = new RecordingListener();

        using var tableOptions = new BlockBasedTableOptions();
        tableOptions.SetFilterPolicy(policy);

        var opts = new DbOptions { CreateIfMissing = true };
        opts.BlockBasedTableFactory = tableOptions;
        opts.EventListener = listener;

        using var db = RocksDb.Open(opts, dir.Path);

        for (int i = 0; i < 500; i++)
        {
            db.Put($"key{i:D5}", $"value{i}");
        }

        db.Flush();

        Assert.True(Wait.Until(() => listener.FlushCompleted.Count > 0), "no flush completed");

        LiveFileMetadata file = Assert.Single(db.GetLiveFiles());
        TableProperties props = Assert.IsType<TableProperties>(listener.FlushCompleted[0].TableProperties);

        return new Written(
            props.FilterPolicyName ?? string.Empty,
            file.Size,
            props.ReadableProperties.Count,
            props.UserCollectedProperties.Count);
    }

    /// <summary>
    /// <see cref="FilterPolicy.CreateBloom"/> and
    /// <see cref="FilterPolicy.CreateBloomFull"/> are the same policy.
    /// </summary>
    /// <remarks>
    /// The documentation said they differed in on-disk record format, one legacy
    /// and one current. They do not: RocksDb stopped honouring the parameter
    /// that chose between them in version 7.0. Ribbon is measured alongside as
    /// the control, since a comparison of two identical things proves nothing
    /// unless something different comes out different.
    /// </remarks>
    [Fact]
    public void TheTwoBloomPolicies_ProduceIdenticalFiles()
    {
        Written bloom = WriteWith(FilterPolicy.CreateBloom(10));
        Written bloomFull = WriteWith(FilterPolicy.CreateBloomFull(10));
        Written ribbon = WriteWith(FilterPolicy.CreateRibbon(10));

        Assert.Equal("bloomfilter", bloom.PolicyName);
        Assert.Equal(bloom.PolicyName, bloomFull.PolicyName);
        Assert.Equal(bloom.FileSize, bloomFull.FileSize);

        // The control: a genuinely different policy comes out different, so the
        // equality above is a result rather than an artefact of the measurement.
        Assert.Equal("ribbonfilter", ribbon.PolicyName);
        Assert.NotEqual(bloom.FileSize, ribbon.FileSize);
    }

    /// <summary>
    /// <see cref="TableProperties.ReadableProperties"/> is always empty.
    /// </summary>
    /// <remarks>
    /// It was documented as the human-readable rendering of the user properties.
    /// RocksDb fills it from collectors registered by the application, and the C
    /// API cannot create a collector factory, so nothing ever registers one.
    /// </remarks>
    [Fact]
    public void ReadableProperties_IsEmptyWhileUserPropertiesIsNot()
    {
        Written written = WriteWith(FilterPolicy.CreateBloomFull(10));

        Assert.Equal(0, written.ReadableCount);

        // Not vacuous: RocksDb does contribute entries of its own, and they
        // arrive on the other dictionary.
        Assert.True(written.UserCount > 0, "no user-collected properties either, so this proves nothing");
    }

    /// <summary>
    /// A per-transaction lock timeout replaces the database-wide one rather than
    /// being clamped by it.
    /// </summary>
    /// <remarks>
    /// Both <see cref="TransactionDbOptions.TransactionLockTimeout"/> and
    /// <see cref="TransactionOptions.LockTimeout"/> described the database value
    /// as a ceiling a transaction could shorten but not exceed. This asks for
    /// three seconds against a database that fails immediately: a ceiling would
    /// return at once, and it waits the full three.
    /// </remarks>
    [Fact]
    public void LockTimeout_ReplacesTheDatabaseValueRatherThanBeingCappedByIt()
    {
        using var dir = new TempDir();

        var dbOptions = new DbOptions { CreateIfMissing = true };

        // Fail immediately, database-wide.
        using var txnDbOptions = new TransactionDbOptions { TransactionLockTimeout = 0 };
        using var db = TransactionDb.Open(dbOptions, txnDbOptions, dir.Path);

        using Transaction holder = db.BeginTransaction();
        holder.Put("key", "held");

        using var patient = new TransactionOptions { LockTimeout = 3_000 };
        using Transaction waiter = db.BeginTransaction(transactionOptions: patient);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<RocksDbException>(() => waiter.Put("key", "blocked"));

        elapsed.Stop();

        Assert.True(
            elapsed.Elapsed > TimeSpan.FromSeconds(2),
            $"gave up after {elapsed.Elapsed.TotalSeconds:F1}s, so the database value capped the transaction's");
    }
}
