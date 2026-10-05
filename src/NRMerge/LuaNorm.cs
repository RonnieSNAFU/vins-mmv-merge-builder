using System.Globalization;
using System.Text;
using LuaCompiler;
using LuaCompiler.Compilers;
using LuaDecompilerCore;
using LuaDecompilerCore.IR;
using LuaDecompilerCore.LanguageDecompilers;
using LuaDecompilerCore.Utilities;

namespace NRMerge;

/// <summary>
/// In-process port of tools\luanorm\LuaNorm.exe (src\LuaNorm\Program.cs) for Lua 5.0 FromSoft AI scripts.
/// Norm: plaintext (UTF-8, else Shift-JIS) or bytecode (leading 0x1B) -> compile if needed -> strip debug names
/// -> decompile; output is identical to `LuaNorm.exe norm` (write it with UTF-8 without BOM).
/// Check: compile only; null when OK, else the Lua 5.0 compiler message (what `LuaNorm.exe check` prints after "ERROR file: ").
/// Thread-safe: each call uses its own lua_State and its own decompiler objects; culture is forced to invariant per call.
/// </summary>
public static class LuaNorm
{
    static readonly Encoding Sjis;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    static LuaNorm()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Sjis = Encoding.GetEncoding("shift_jis");
        LuaNative.EnsureResolver();
    }

    /// <summary>Normalize a Lua 5.0 script given as file bytes (plaintext or bytecode).</summary>
    public static string Norm(byte[] input) => Invariant(() => Decompile(Compile(input)));

    /// <summary>Bytecode passes through; plaintext is decoded and compiled with the Lua 5.0 compiler.</summary>
    public static byte[] Compile(byte[] input)
    {
        if (input.Length > 0 && input[0] == 0x1B) return input;
        return CompileText(DecodeText(input));
    }

    /// <summary>Null if the text compiles as Lua 5.0, else the compiler error message.</summary>
    public static string Check(string text)
    {
        try { CompileText(text); return null; }
        catch (CompileException e) { return e.Message; }
    }

    /// <summary>UTF-8 (BOM stripped) when valid, else Shift-JIS; same rule as LuaNorm.exe.</summary>
    public static string DecodeText(byte[] raw)
    {
        try { return StrictUtf8.GetString(raw).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Sjis.GetString(raw); }
    }

    static byte[] CompileText(string text) => new Lua50Compiler().CompileSource(text, Sjis);

    static string Decompile(byte[] bytecode)
    {
        var lua = new LuaFile(new BinaryReaderEx(false, new MemoryStream(bytecode)));
        Strip(lua.MainFunction);
        var main = new Function(lua.MainFunction.FunctionId);
        var result = new LuaDecompiler(new DecompilationOptions()).DecompileLuaFunction(new Lua50Decompiler(), main, lua.MainFunction);
        return result.DecompiledSource;
    }

    // Drop debug names so hand-named and generated sources decompile to the same text.
    static void Strip(LuaFile.Function f)
    {
        f.Locals = Array.Empty<LuaFile.Local>();
        f.LocalMap = new Dictionary<int, List<LuaFile.Local>>();
        f.LocalVarsCount = 0;
        f.UpValueNames = Array.Empty<LuaFile.UpValueName>();
        f.UpValuesCount = 0;
        foreach (var c in f.ChildFunctions) Strip(c);
    }

    static T Invariant<T>(Func<T> f)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { return f(); }
        finally { CultureInfo.CurrentCulture = prev; }
    }
}
