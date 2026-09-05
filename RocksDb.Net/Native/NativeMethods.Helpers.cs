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
    internal static void ThrowFirstError(nint[] errs)
    {
        nint first = nint.Zero;

        for (int i = 0; i < errs.Length; i++)
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
