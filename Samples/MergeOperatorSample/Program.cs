using System.Text;
using RocksDbNet;

// ─── MergeOperator: read-modify-write without reading ────────────────────────
// Merge lets you append updates to a key without reading the current value.
// The merge operator defines how operands are combined with existing values.
// This is ideal for counters, append-only lists, and aggregations.

const string dbPath = "merge_operator_db";

// --- Counter merge: sums integer operands ---
Console.WriteLine("=== Counter (sum) merge operator ===");
{
    var options = new DbOptions
    {
        CreateIfMissing = true,
        MergeOperator = new CounterMergeOperator()
    };
    using var db = RocksDb.Open(options, dbPath + "_counter");

    // Initial value
    db.Put("page_views", "100");

    // Merge increments (no read needed!)
    db.Merge("page_views", "5");
    db.Merge("page_views", "10");
    db.Merge("page_views", "1");

    // Merge on non-existing key starts from 0
    db.Merge("new_counter", "42");
    db.Merge("new_counter", "8");

    Console.WriteLine($"  page_views  = {db.GetString("page_views")}");  // 116
    Console.WriteLine($"  new_counter = {db.GetString("new_counter")}"); // 50
}

// --- Append-list merge: builds comma-separated lists ---
Console.WriteLine("\n=== Append-list merge operator ===");
{
    var options = new DbOptions
    {
        CreateIfMissing = true,
        MergeOperator = new AppendListMergeOperator()
    };
    using var db = RocksDb.Open(options, dbPath + "_list");

    db.Put("tags:user1", "admin");
    db.Merge("tags:user1", "editor");
    db.Merge("tags:user1", "reviewer");

    // Build list from scratch
    db.Merge("tags:user2", "viewer");
    db.Merge("tags:user2", "commenter");

    Console.WriteLine($"  tags:user1 = {db.GetString("tags:user1")}");   // admin,editor,reviewer
    Console.WriteLine($"  tags:user2 = {db.GetString("tags:user2")}");   // viewer,commenter
}

Console.WriteLine("\nMergeOperator sample completed.");

// ── Counter merge: sums long values ────────────────────────────────────────

class CounterMergeOperator : MergeOperator
{
    public CounterMergeOperator() : base("CounterMerge", enablePartialMerge: true) { }

    public override bool FullMerge(
        ReadOnlySpan<byte> key,
        bool hasExistingValue,
        ReadOnlySpan<byte> existingValue,
        IEnumerable<byte[]> operands,
        out byte[] newValue)
    {
        long sum = 0;
        if (hasExistingValue && long.TryParse(Encoding.UTF8.GetString(existingValue), out long existing))
            sum = existing;

        foreach (var operand in operands)
        {
            if (long.TryParse(Encoding.UTF8.GetString(operand), out long val))
                sum += val;
        }

        newValue = Encoding.UTF8.GetBytes(sum.ToString());
        return true;
    }

    public override bool PartialMerge(
        ReadOnlySpan<byte> key,
        IEnumerable<byte[]> operands,
        out byte[] newValue)
    {
        long sum = 0;
        foreach (var operand in operands)
        {
            if (long.TryParse(Encoding.UTF8.GetString(operand), out long val))
                sum += val;
        }
        newValue = Encoding.UTF8.GetBytes(sum.ToString());
        return true;
    }
}

// ── Append-list merge: comma-separated concatenation ───────────────────────

class AppendListMergeOperator : MergeOperator
{
    public AppendListMergeOperator() : base("AppendListMerge") { }

    public override bool FullMerge(
        ReadOnlySpan<byte> key,
        bool hasExistingValue,
        ReadOnlySpan<byte> existingValue,
        IEnumerable<byte[]> operands,
        out byte[] newValue)
    {
        var sb = new StringBuilder();
        if (hasExistingValue)
            sb.Append(Encoding.UTF8.GetString(existingValue));

        foreach (var operand in operands)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(Encoding.UTF8.GetString(operand));
        }

        newValue = Encoding.UTF8.GetBytes(sb.ToString());
        return true;
    }
}
