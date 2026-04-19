using System.Text;
using RocksDbNet;

namespace Simple;

class MyMergeOperator : MergeOperator
{
    public MyMergeOperator() : base("MyMergeOperator")
    {
    }

    public override bool FullMerge(ReadOnlySpan<byte> key, bool hasExistingValue, ReadOnlySpan<byte> existingValue, IEnumerable<byte[]> operands, out byte[] newValue)
    {
        newValue = Array.Empty<byte>();
        return true;
    }
}

class CaseInsensitiveComparator : Comparator
{
    public CaseInsensitiveComparator()
        : base(nameof(CaseInsensitiveComparator))
    {
    }

    public override int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int alen = a.Length;
        int blen = b.Length;
        int min_len = (alen < blen) ? alen : blen;
        for (int i = 0; i < min_len; i++)
        {
            char ca = char.ToLower((char)a[i]);
            char cb = char.ToLower((char)b[i]);
            if (ca < cb) return -1;
            if (ca > cb) return 1;
        }

        if (alen < blen) return -1;
        if (alen > blen) return 1;

        return 0;
    }
}

class MyLogger : Logger
{
    public MyLogger()
        : base (LogLevel.Debug)
    {
    }
    public override void Log(LogLevel logLevel, string message)
    {
        Console.WriteLine($"[{logLevel}] {message}");
    }
}

class MyEventListener : EventListener
{
    public override void OnFlushCompleted(FlushJobInfo flushJobInfo)
    {
        Console.WriteLine($"Flush completed: {flushJobInfo}");
    }
    public override void OnCompactionCompleted(CompactionJobInfo compactionJobInfo)
    {
        Console.WriteLine($"Compaction completed: {compactionJobInfo}");
    }
}

static class Program
{
    static void Main(string[] args)
    {
        using var logger = new MyLogger();

        var options = new DbOptions()
        {
            CreateIfMissing = true,
            InfoLogLevel = LogLevel.Debug,
            WriteBufferSize = 0
        }
        .SetComparator(new CaseInsensitiveComparator())
        .SetMergeOperator(new MyMergeOperator())
        .SetInfoLog(logger)
        .AddEventListener(new MyEventListener());

        var x = Environment.CurrentDirectory;

        using (var db = RocksDb.Open(options, "rocksdb"))
        {
            db.Put("user_tags", "active");
            db.Put(Encoding.UTF8.GetBytes("majom"), null!);

            db.Merge("user_tags", "premium");
            db.Merge("user_tags2", "verified");
            db.Merge("majom", "verified");


            db.Put("aladar", "active");
            db.Put("Aladar", "active");
            db.Put("User_tags", "active");
            db.Put("Bela", "active");
            db.Put("bela", "active");

            var value = db.Get("user_tags2");

            db.CompactRange();

            db.Flush();
        }
        Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!! Done...");
    }
}
