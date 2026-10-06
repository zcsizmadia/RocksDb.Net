using NativeMethodsGenerator;

namespace NativeMethodsGenerator.Tests;

/// <summary>
/// Which bindings skip the GC transition. See issue #187.
/// </summary>
/// <remarks>
/// A function on this list that turns out to block, take a contended lock or
/// call back into managed code is undefined behaviour, not a slow call: no
/// thread can collect while it runs, and a callback into the runtime from a
/// suppressed call can deadlock or corrupt it. So the set is pinned exactly,
/// and adding to it has to be a deliberate edit of this test as well as of the
/// generator, with the reason checked against <c>db/c.cc</c>.
/// </remarks>
public class SuppressGCTransitionTests
{
    private static string Emit(string declaration)
    {
        List<CFunction> functions = CHeaderParser.Parse($"extern ROCKSDB_LIBRARY_API {declaration}");
        Assert.Single(functions);

        return PInvokeGenerator.Generate(functions, "11.8.1", "https://example.invalid/c.h");
    }

    [Fact]
    public void TheSetIsExactlyTheTrivialAccessors()
    {
        Assert.Equal(
            new[]
            {
                "rocksdb_iter_key",
                "rocksdb_iter_valid",
                "rocksdb_iter_value",
                "rocksdb_writebatch_count",
                "rocksdb_writebatch_data",
            },
            PInvokeGenerator.SuppressGCTransition.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("unsigned char rocksdb_iter_valid(const rocksdb_iterator_t* iter);")]
    [InlineData("const char* rocksdb_iter_key(const rocksdb_iterator_t* iter, size_t* klen);")]
    [InlineData("int rocksdb_writebatch_count(rocksdb_writebatch_t* b);")]
    public void AnAllowedFunction_IsEmittedWithTheAttribute(string declaration)
    {
        Assert.Contains("[SuppressGCTransition]", Emit(declaration), StringComparison.Ordinal);
    }

    /// <summary>
    /// The calls next to the accessors, which do real work: these must keep the
    /// transition, because they can do I/O, block, or run a comparator or merge
    /// operator.
    /// </summary>
    [Theory]
    [InlineData("void rocksdb_iter_next(rocksdb_iterator_t* iter);")]
    [InlineData("void rocksdb_iter_seek(rocksdb_iterator_t* iter, const char* k, size_t klen);")]
    [InlineData("void rocksdb_iter_destroy(rocksdb_iterator_t* iter);")]
    [InlineData("void rocksdb_writebatch_put(rocksdb_writebatch_t* b, const char* key, size_t klen, const char* val, size_t vlen);")]
    public void ANeighbouringFunction_KeepsTheTransition(string declaration)
    {
        Assert.DoesNotContain("[SuppressGCTransition]", Emit(declaration), StringComparison.Ordinal);
    }
}
