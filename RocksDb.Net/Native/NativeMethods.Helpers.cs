using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RocksDbNet.Native;

internal static unsafe partial class NativeMethods
{
    internal const string LibName = "librocksdb";

    /// <summary>
    /// Registers a custom DLL import resolver to locate the librocksdb native library
    /// from the runtimes/{rid}/native directory structure at startup.
    /// </summary>
    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveRuntimeDll);
    }

    /// <summary>
    /// Custom DLL import resolver that locates the librocksdb native library
    /// from the runtimes/{os}-{arch}/native directory structure.
    /// </summary>
    /// <param name="libraryName">The name of the native library to resolve.</param>
    /// <param name="assembly">The assembly that triggered the load.</param>
    /// <param name="searchPath">The DLL import search path hint.</param>
    /// <returns>A handle to the loaded native library, or <see cref="IntPtr.Zero"/> to fall back to default loading.</returns>
    [ExcludeFromCodeCoverage]
    // IL3000 fires on any mention of Assembly.Location, and the analyser cannot
    // see that the empty string it warns about is handled three lines below.
    // Dropping the property instead would be a real regression: when this
    // assembly is loaded from somewhere other than the app directory — a plugin
    // folder, or a host resolving it out of a package cache — the runtimes
    // folder sits beside the assembly and not beside the executable, and
    // AppContext.BaseDirectory would not find it. So the property stays, the
    // empty case is handled, and this says why.
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "The empty Location a single-file app reports is checked for, and falls back to AppContext.BaseDirectory.")]
    public static IntPtr ResolveRuntimeDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only intercept the specific library
        if (libraryName != LibName)
        {
            return IntPtr.Zero; // Fallback to default loading logic
        }

        string os;
        string libraryNameExt;
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();

        string libMajorVersion = RocksDbVersion.Split(".")[0];

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = "win";
            libraryNameExt = $"{LibName}.{libMajorVersion}.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = "osx";
            libraryNameExt = $"{LibName}.{libMajorVersion}.dylib";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            os = "linux";
            libraryNameExt = $"{LibName}.so.{libMajorVersion}";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported OS platform");
        }

        // Attempt to load the library from the assembly location directory.
        //
        // Assembly.Location is an empty string for an assembly embedded in a
        // single-file app, which includes anything published with PublishAot.
        // Asking for the directory of an empty path happened to return null and
        // so fell through to the base directory below, but only by accident, and
        // the AOT analyser is right to object (IL3000). Say it deliberately.
        string assemblyDirectory = string.IsNullOrEmpty(assembly.Location)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;

        string libPath = Path.Combine(assemblyDirectory, "runtimes", $"{os}-{arch}", "native", libraryNameExt);
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt to load the library from the application base directory
        libPath = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", libraryNameExt);
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt to load the library directly from the application base directory
        libPath = Path.Combine(AppContext.BaseDirectory, libraryNameExt);
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt using the default search path
        if (NativeLibrary.TryLoad(libraryNameExt, assembly, searchPath, out var handle))
        {
            return handle;
        }

        return IntPtr.Zero; // Let the system try its default search paths
    }

    /// <summary>
    /// Throws a <see cref="RocksDbException"/> if <paramref name="errPtr"/> is non-zero,
    /// freeing the native error string in the process.
    /// </summary>
    internal static void ThrowOnError(nint errPtr)
    {
        if (errPtr == nint.Zero)
        {
            return;
        }

        string? msg = Marshal.PtrToStringUTF8(errPtr);
        rocksdb_free(errPtr);
        throw new RocksDbException(msg ?? "Unknown RocksDb error");
    }

    /// <summary>
    /// Throws for the first per-key error, having freed all of them.
    /// </summary>
    /// <remarks>
    /// The batched reads allocate one message per failing key and the caller
    /// owns each. Only the first becomes the exception, but every one has to be
    /// released, which is why this is not a loop that throws on the first
    /// non-zero entry.
    /// </remarks>
    internal static void ThrowFirstError(nint* errs, int count)
    {
        nint first = nint.Zero;

        for (int i = 0; i < count; i++)
        {
            if (errs[i] == nint.Zero)
            {
                continue;
            }

            if (first == nint.Zero)
            {
                first = errs[i];
            }
            else
            {
                rocksdb_free(errs[i]);
            }
        }

        // Frees the message it reports.
        ThrowOnError(first);
    }

    /// <summary>
    /// Copies a value the caller owns into a managed array and frees it.
    /// Returns null for a null pointer, which is how the reads report a missing key.
    /// </summary>
    /// <remarks>
    /// The free is in a <c>finally</c>. The copy can throw, through the
    /// checked length conversion for a value over 2 GB or an
    /// <see cref="OutOfMemoryException"/> from the allocation, and the native
    /// value used to leak when it did.
    /// </remarks>
    internal static byte[]? CopyAndFree(nint value, nuint length)
    {
        if (value == nint.Zero)
        {
            return null;
        }

        try
        {
            return new ReadOnlySpan<byte>((byte*)value, checked((int)length)).ToArray();
        }
        finally
        {
            rocksdb_free(value);
        }
    }

    /// <summary>
    /// Copies the value a pinned read returned into a managed array and
    /// destroys the slice. Returns null for a null slice, which is how the
    /// pinned reads report a missing key.
    /// </summary>
    /// <remarks>
    /// This is how the array-returning reads are implemented. <c>rocksdb_get</c>
    /// is a pinned read underneath that then copies the value into a
    /// <c>std::string</c>, and <c>c.cc</c> copies that again into a fresh
    /// <c>malloc</c> buffer before the wrapper copies it a third time. Reading
    /// pinned instead leaves one copy, straight from the block cache or
    /// memtable, and no value-sized allocation on the native side.
    /// </remarks>
    internal static byte[]? CopyPinnedAndDestroy(nint slice)
    {
        if (slice == nint.Zero)
        {
            return null;
        }

        try
        {
            byte* data = rocksdb_pinnableslice_value(slice, out nuint length);
            return data is null ? [] : new ReadOnlySpan<byte>(data, checked((int)length)).ToArray();
        }
        finally
        {
            rocksdb_pinnableslice_destroy(slice);
        }
    }

    /// <summary>
    /// Decodes the value a pinned read returned as UTF-8 and destroys the
    /// slice. Returns null for a null slice.
    /// </summary>
    /// <remarks>
    /// Decodes in place rather than copying to an array first, so the only
    /// allocation is the string itself.
    /// </remarks>
    internal static string? DecodePinnedAndDestroy(nint slice)
    {
        if (slice == nint.Zero)
        {
            return null;
        }

        try
        {
            byte* data = rocksdb_pinnableslice_value(slice, out nuint length);
            return data is null ? string.Empty : System.Text.Encoding.UTF8.GetString(data, checked((int)length));
        }
        finally
        {
            rocksdb_pinnableslice_destroy(slice);
        }
    }

    /// <summary>
    /// Destroys the slice a pinned read returned, and says whether there was
    /// one, which is whether the key was found.
    /// </summary>
    /// <remarks>
    /// The existence check. The value is never copied, which is what makes it
    /// cheaper than reading the value and throwing it away.
    /// </remarks>
    internal static bool DestroyPinned(nint slice)
    {
        if (slice == nint.Zero)
        {
            return false;
        }

        rocksdb_pinnableslice_destroy(slice);
        return true;
    }

    /// <summary>
    /// Hands the value a pinned read returned to <paramref name="decode"/> in
    /// place, then destroys the slice. Returns false for a null slice.
    /// </summary>
    /// <remarks>
    /// The slice is destroyed even when the decoder throws, and the exception
    /// reaches the caller unchanged: the decoder runs on the caller's thread,
    /// not inside a native callback, so nothing needs to stop it unwinding.
    /// </remarks>
    internal static bool DecodePinnedAndDestroy<T>(nint slice, ValueDecoder<T> decode, [MaybeNullWhen(false)] out T value)
    {
        if (slice == nint.Zero)
        {
            value = default;
            return false;
        }

        try
        {
            value = decode(PinnedValue(slice));
            return true;
        }
        finally
        {
            rocksdb_pinnableslice_destroy(slice);
        }
    }

    /// <inheritdoc cref="DecodePinnedAndDestroy{T}(nint, ValueDecoder{T}, out T)"/>
    internal static bool DecodePinnedAndDestroy<TState, T>(
        nint slice, TState state, ValueDecoder<TState, T> decode, [MaybeNullWhen(false)] out T value)
    {
        if (slice == nint.Zero)
        {
            value = default;
            return false;
        }

        try
        {
            value = decode(PinnedValue(slice), state);
            return true;
        }
        finally
        {
            rocksdb_pinnableslice_destroy(slice);
        }
    }

    private static ReadOnlySpan<byte> PinnedValue(nint slice)
    {
        byte* data = rocksdb_pinnableslice_value(slice, out nuint length);
        return data is null ? [] : new ReadOnlySpan<byte>(data, checked((int)length));
    }

    /// <summary>
    /// Decodes a UTF-8 string the caller owns and frees it. Returns null for a null pointer.
    /// </summary>
    /// <inheritdoc cref="CopyAndFree" path="/remarks"/>
    internal static string? CopyAndFreeUtf8(nint value, nuint length)
    {
        if (value == nint.Zero)
        {
            return null;
        }

        try
        {
            return System.Text.Encoding.UTF8.GetString((byte*)value, checked((int)length));
        }
        finally
        {
            rocksdb_free(value);
        }
    }

    /// <summary>
    /// Copies and frees every value a batched read returned, frees every error,
    /// and then throws for the first error if there was one.
    /// </summary>
    /// <remarks>
    /// Every native allocation is released whatever happens. Each value is
    /// copied and freed on its own, so a copy that throws part-way through
    /// still frees the values after it and the error strings, and the first
    /// exception is rethrown once they have been.
    /// </remarks>
    /// <param name="batch">The batch the read wrote its results into.</param>
    /// <param name="pinned">
    /// Whether the value slots hold pinned slices, from the batched read, or
    /// <c>malloc</c> buffers with their lengths, from <c>rocksdb_multi_get</c>.
    /// </param>
    internal static byte[]?[] CopyAndFreeBatch(in NativeKeyBatch batch, bool pinned)
    {
        var results = new byte[]?[batch.Count];
        Exception? copyFailure = null;

        for (int i = 0; i < batch.Count; i++)
        {
            try
            {
                results[i] = pinned
                    ? CopyPinnedAndDestroy(batch.Values[i])
                    : CopyAndFree(batch.Values[i], batch.ValueSizes[i]);
            }
            catch (Exception e)
            {
                copyFailure ??= e;
            }
        }

        if (copyFailure is not null)
        {
            for (int i = 0; i < batch.Count; i++)
            {
                if (batch.Errors[i] != nint.Zero)
                {
                    rocksdb_free(batch.Errors[i]);
                }
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(copyFailure);
        }

        ThrowFirstError(batch.Errors, batch.Count);
        return results;
    }

    /// <summary>
    /// Reads a native UTF-8 string pointer (not owned) into a managed string.
    /// </summary>
    internal static string? PtrToStringUTF8(byte* ptr, nuint len)
    {
        return ptr == null ? null : System.Text.Encoding.UTF8.GetString(ptr, (int)len);
    }

    /// <summary>
    /// Reads a native UTF-8 string pointer (not owned) into a managed string.
    /// </summary>
    internal static string? PtrToStringUTF8(nint ptr, nuint len) => PtrToStringUTF8((byte*)ptr, len);
}
