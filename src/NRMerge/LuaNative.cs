using System.Reflection;
using System.Runtime.InteropServices;
using LuaCompiler.Compilers;

namespace NRMerge;

/// <summary>
/// Locates lua502.dll for DSLua's LuaCompiler, whose [LibraryImport("LuaNative/lua502.dll")] is relative.
/// Default .NET probing already finds &lt;app&gt;\LuaNative\lua502.dll (LuaCompiler.csproj copies it there as Content,
/// transitively into referencing projects and publish output). The resolver additionally covers single-file publish
/// (assembly directory is empty there) and a flat layout (&lt;app&gt;\lua502.dll, like tools\luanorm).
/// </summary>
public static class LuaNative
{
    static int _installed;

    public static string FindLua502(string baseDir)
    {
        foreach (var p in new[] { Path.Combine(baseDir, "LuaNative", "lua502.dll"), Path.Combine(baseDir, "lua502.dll") })
            if (File.Exists(p)) return p;
        return null;
    }

    public static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        try { NativeLibrary.SetDllImportResolver(typeof(Lua50Compiler).Assembly, Resolve); }
        catch (InvalidOperationException) { /* host already installed a resolver for LuaCompiler */ }
    }

    static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (!name.Contains("lua502", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        var p = FindLua502(AppContext.BaseDirectory);
        return p != null && NativeLibrary.TryLoad(p, out var h) ? h : IntPtr.Zero; // Zero = fall back to default probing
    }
}
