namespace NativeMethodsGenerator.Tests;

/// <summary>
/// The six ABI defect classes that have reached the generated file before, each
/// pinned against the declaration that produced it.
/// </summary>
/// <remarks>
/// <para>
/// The generator had no tests at all until now, which made it the least
/// verified and most consequential code in the repository: it emits all 1,745
/// P/Invoke declarations, and every marshalling defect this project has shipped
/// came through it. The 11.8.1.1 changelog lists five of them under Fixed, all
/// found by reading the output rather than by anything failing.
/// </para>
/// <para>
/// These tests are written against the *shape* of a declaration rather than
/// against any particular function, so they keep holding when the pinned RocksDb
/// version changes and the header does not look the same.
/// </para>
/// </remarks>
public class AbiMappingTests
{
    /// <summary>Generates for one declaration and returns the emitted line.</summary>
    private static string Emit(string declaration)
    {
        List<CFunction> functions = CHeaderParser.Parse(
            $"extern ROCKSDB_LIBRARY_API {declaration}");

        Assert.Single(functions);

        return PInvokeGenerator.Generate(functions, "11.8.1", "https://example.invalid/c.h");
    }

    /// <summary>
    /// A struct returned by value keeps its type, rather than becoming a
    /// pointer.
    /// </summary>
    /// <remarks>
    /// `rocksdb_slice_t` is 16 bytes, which Windows x64 returns through a hidden
    /// pointer argument. Declaring it as a returned pointer meant the call wrote
    /// over the caller's memory and read the wrong register as its first
    /// argument. Three functions had this.
    /// </remarks>
    [Fact]
    public void StructReturnedByValue_KeepsItsType()
    {
        string emitted = Emit("rocksdb_slice_t rocksdb_iter_key_slice(const rocksdb_iterator_t* iter);");

        Assert.Contains("rocksdb_slice_t rocksdb_iter_key_slice", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("nint rocksdb_iter_key_slice", emitted, StringComparison.Ordinal);
    }

    /// <summary>A C <c>bool</c> is one byte, in both directions.</summary>
    /// <remarks>
    /// Declared pointer-sized, a return reads register bits no ABI requires the
    /// callee to define, so a false can arrive as true. Four returns and seven
    /// parameters had this.
    /// </remarks>
    [Theory]
    [InlineData("bool rocksdb_options_get_error_if_exists(rocksdb_options_t* opt);")]
    [InlineData("unsigned char rocksdb_iter_valid(const rocksdb_iterator_t* iter);")]
    public void BooleanReturn_IsAByte(string declaration)
    {
        string emitted = Emit(declaration);

        Assert.Contains("byte ", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("nint rocksdb_", emitted, StringComparison.Ordinal);

        // Never the managed bool either: its marshalling is 4 bytes by default,
        // which is not what the header says.
        Assert.DoesNotContain(" bool ", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void BooleanParameter_IsAByte()
    {
        string emitted = Emit(
            "void rocksdb_options_set_error_if_exists(rocksdb_options_t* opt, unsigned char v);");

        Assert.Contains("byte v", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>size_t</c> stays pointer-sized rather than becoming a fixed 64 bits.
    /// </summary>
    /// <remarks>
    /// The defect this guards was silent on x64 and wrong only on win-x86: merge
    /// operand lengths are a <c>size_t</c> array, read as 64-bit, so every index
    /// after the first came from the wrong offset and the last ran off the end.
    /// </remarks>
    [Theory]
    [InlineData("void rocksdb_test(rocksdb_t* db, size_t n);", "nuint n")]
    [InlineData("void rocksdb_test(rocksdb_t* db, const size_t* lens);", "nuint* lens")]
    [InlineData("size_t rocksdb_test(rocksdb_t* db);", "nuint rocksdb_test")]
    public void SizeT_IsPointerSized(string declaration, string expected)
    {
        string emitted = Emit(declaration);

        Assert.Contains(expected, emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("ulong", emitted, StringComparison.Ordinal);
    }

    /// <summary>An array parameter keeps a typed pointer.</summary>
    /// <remarks>
    /// Thirteen functions took arrays as untyped integers, including
    /// <c>rocksdb_open_column_families</c> and <c>rocksdb_multi_get_cf</c>. They
    /// worked — a pointer is a pointer — but nothing stopped the wrong thing
    /// being passed.
    /// </remarks>
    [Theory]
    [InlineData("void rocksdb_test(rocksdb_t* db, const char* const* keys);", "byte**")]
    [InlineData("void rocksdb_test(rocksdb_t* db, const char* const keys[]);", "byte**")]
    [InlineData("void rocksdb_test(rocksdb_t* db, char*** out);", "byte***")]
    public void ArrayParameter_IsTyped(string declaration, string expected)
    {
        Assert.Contains(expected, Emit(declaration), StringComparison.Ordinal);
    }

    /// <summary>
    /// A by-value integer stays a value, rather than becoming a pointer.
    /// </summary>
    /// <remarks>
    /// Restoring a backup by id could not have worked: the <c>const uint32_t</c>
    /// id mapped to an untyped <c>nint</c>, so the id was passed where a pointer
    /// was expected.
    /// </remarks>
    [Theory]
    [InlineData("void rocksdb_test(rocksdb_t* db, const uint32_t id);", "uint id")]
    [InlineData("void rocksdb_test(rocksdb_t* db, uint64_t seq);", "ulong seq")]
    [InlineData("void rocksdb_test(rocksdb_t* db, const int level);", "int level")]
    public void ByValueInteger_StaysAValue(string declaration, string expected)
    {
        string emitted = Emit(declaration);

        Assert.Contains(expected, emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("nint id", emitted, StringComparison.Ordinal);
    }

    /// <summary>The error out-parameter is by reference.</summary>
    /// <remarks>
    /// 194 declarations end with <c>char** errptr</c>, and every one of them has
    /// to be <c>ref nint</c> for <c>ThrowOnError</c> to see what RocksDb wrote.
    /// </remarks>
    [Fact]
    public void ErrorOutParameter_IsByReference()
    {
        string emitted = Emit("void rocksdb_put(rocksdb_t* db, char** errptr);");

        Assert.Contains("ref nint errptr", emitted, StringComparison.Ordinal);
    }

    /// <summary>Every declaration is cdecl.</summary>
    /// <remarks>
    /// RocksDb's C API is cdecl throughout, and the default on some targets is
    /// not, so an omission here would be a stack imbalance rather than a
    /// compile error.
    /// </remarks>
    [Fact]
    public void EveryDeclaration_IsCdecl()
    {
        string emitted = PInvokeGenerator.Generate(
            CHeaderParser.Parse("""
                extern ROCKSDB_LIBRARY_API void rocksdb_a(rocksdb_t* db);
                extern ROCKSDB_LIBRARY_API size_t rocksdb_b(rocksdb_t* db);
                extern ROCKSDB_LIBRARY_API unsigned char rocksdb_c(rocksdb_t* db);
                """),
            "11.8.1",
            "https://example.invalid/c.h");

        int declarations = emitted.Split("LibraryImport").Length - 1;
        int conventions = emitted.Split("CallConvCdecl").Length - 1;

        Assert.Equal(3, declarations);
        Assert.Equal(declarations, conventions);
    }

    /// <summary>
    /// A type the generator does not know fails the build rather than becoming
    /// <c>nint</c>.
    /// </summary>
    /// <remarks>
    /// This is the property that makes an exhaustive audit of the generated file
    /// meaningful, and it is the reason every defect above was findable at all:
    /// each of them reached the output through a silent fallback. If this
    /// regresses, the next header addition can reintroduce any of them quietly.
    /// </remarks>
    [Fact]
    public void UnmappedType_ThrowsRatherThanFallingBackToNint()
    {
        NotSupportedException ex = Assert.Throws<NotSupportedException>(
            () => Emit("void rocksdb_test(rocksdb_t* db, struct timespec deadline);"));

        // The message has to name the type and say where to fix it, because the
        // person hitting this is mid-upgrade and has no other clue.
        Assert.Contains("timespec", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MapCTypeToManaged", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A string parameter is marshalled as UTF-8, which is what RocksDb reads.
    /// </summary>
    [Fact]
    public void StringParameter_IsUtf8()
    {
        string emitted = Emit("rocksdb_t* rocksdb_open(const rocksdb_options_t* o, const char* name);");

        Assert.Contains("StringMarshalling.Utf8", emitted, StringComparison.Ordinal);
        Assert.Contains("string name", emitted, StringComparison.Ordinal);
    }
}
