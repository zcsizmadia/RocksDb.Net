using System.Diagnostics;
using System.Text;

using RocksDbNet;

namespace Simple;

static class Program
{
    static void Main(string[] args)
    {
        string temp = Path.GetTempPath();
        string path = Environment.ExpandEnvironmentVariables(Path.Combine(temp, "rocksdb_prefix_example"));
        var bbto = new BlockBasedTableOptions()
            {
                WholeKeyFiltering = false,
            }
            .SetFilterPolicy(FilterPolicy.CreateBloomFull(10));
        var options = new DbOptions()
            {
                CreateIfMissing = true,
                CreateMissingColumnFamilies = true
            };
        var columnFamilies = new List<ColumnFamilyDescriptor>()
        {
            new ("default", new DbOptions().OptimizeForPointLookup(256)),
            new ("test", new DbOptions()
                .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong)8))
                .SetBlockBasedTableFactory(bbto))
        };
        using (var db = RocksDb.Open(options, path, columnFamilies))
        {
            var cf = db.GetColumnFamily("test");

            db.Put("00000000Zero", "", cf: cf);
            db.Put("00000000One", "", cf: cf);
            db.Put("00000000Two", "", cf: cf);
            db.Put("00000000Three", "", cf: cf);
            db.Put("00000001Red", "", cf: cf);
            db.Put("00000001Green", "", cf: cf);
            db.Put("00000001Black", "", cf: cf);
            db.Put("00000002Apple", "", cf: cf);
            db.Put("00000002Cranberry", "", cf: cf);
            db.Put("00000002Banana", "", cf: cf);

            var readOptions = new ReadOptions();
            using (var iter = db.NewIterator(cf, readOptions))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var b = Encoding.UTF8.GetBytes("00000001");
                iter.Seek(b);
                while (iter.IsValid())
                {
                    Console.WriteLine(iter.KeyAsString());
                    iter.Next();
                }
            }
        }
        Console.WriteLine("Done...");
    }
}
