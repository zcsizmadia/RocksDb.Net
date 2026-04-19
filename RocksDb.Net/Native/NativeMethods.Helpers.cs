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

        // Attempt to load the library from the assembly location directory
        string libPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", libraryNameExt);
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
        throw new RocksDbException(msg ?? "Unknown RocksDB error");
    }

    /// <summary>
    /// Reads a native UTF-8 string pointer (not owned) into a managed string.
    /// </summary>
    internal static string? PtrToStringUTF8(byte* ptr, nuint len)
    {
        if (ptr == null)
        {
            return null;
        }
        return System.Text.Encoding.UTF8.GetString(ptr, (int)len);
    }

    /// <summary>
    /// Reads a native UTF-8 string pointer (not owned) into a managed string.
    /// </summary>
    internal static string? PtrToStringUTF8(nint ptr, nuint len) => PtrToStringUTF8((byte*)ptr, len);
}
