using System.Globalization;
using System.Text;
using LuaCompiler.Compilers;
using LuaDecompilerCore;
using LuaDecompilerCore.IR;
using LuaDecompilerCore.LanguageDecompilers;
using LuaDecompilerCore.Utilities;

// LuaNorm: Lua 5.0 (FromSoft AI) helper for the EV x MMV merge.
//   luanorm check <file.lua>...          compile each file; print OK or the compile error
//   luanorm norm <in> <out>              plaintext or bytecode -> compile if needed -> decompile (uniform style)
Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var sjis = Encoding.GetEncoding("shift_jis");

byte[] Compile(string path)
{
    var raw = File.ReadAllBytes(path);
    if (raw.Length > 0 && raw[0] == 0x1B) return raw;
    var text = DecodeText(raw);
    return new Lua50Compiler().CompileSource(text, sjis);
}

string DecodeText(byte[] raw)
{
    try { return new UTF8Encoding(false, true).GetString(raw).TrimStart('﻿'); }
    catch (DecoderFallbackException) { return sjis.GetString(raw); }
}

string Decompile(byte[] bytecode)
{
    var lua = new LuaFile(new BinaryReaderEx(false, new MemoryStream(bytecode)));
    Strip(lua.MainFunction);
    var main = new Function(lua.MainFunction.FunctionId);
    var result = new LuaDecompiler(new DecompilationOptions()).DecompileLuaFunction(new Lua50Decompiler(), main, lua.MainFunction);
    return result.DecompiledSource;
}

// Drop debug names so hand-named and generated sources decompile to the same text.
void Strip(LuaFile.Function f)
{
    f.Locals = Array.Empty<LuaFile.Local>();
    f.LocalMap = new Dictionary<int, List<LuaFile.Local>>();
    f.LocalVarsCount = 0;
    f.UpValueNames = Array.Empty<LuaFile.UpValueName>();
    f.UpValuesCount = 0;
    foreach (var c in f.ChildFunctions) Strip(c);
}

switch (args[0])
{
    case "check":
        int bad = 0;
        foreach (var p in args.Skip(1))
        {
            try { Compile(p); Console.WriteLine($"OK {p}"); }
            catch (Exception e) { bad++; Console.WriteLine($"ERROR {p}: {e.Message}"); }
        }
        return bad == 0 ? 0 : 1;
    case "norm":
        File.WriteAllText(args[2], Decompile(Compile(args[1])), new UTF8Encoding(false));
        return 0;
    default:
        Console.WriteLine("usage: luanorm check <files> | norm <in> <out>");
        return 1;
}
