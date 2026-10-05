using System;
using System.IO;
using System.Text;

namespace NRMerge;

/// <summary>Python 3 text semantics the ported scripts rely on.</summary>
public static class PyText
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary><c>open(path, encoding='utf-8').read()</c>: strict UTF-8 (a BOM stays as U+FEFF), universal newlines.</summary>
    public static string Read(string path) => UniversalNewlines(StrictUtf8.GetString(File.ReadAllBytes(path)));

    /// <summary>Text written with <c>open(path, 'w', encoding='utf-8', newline='\n')</c>.</summary>
    public static void Write(string path, string text) => File.WriteAllBytes(path, StrictUtf8.GetBytes(text));

    /// <summary>Universal-newline translation of text-mode reads: "\r\n" and lone "\r" become "\n".</summary>
    public static string UniversalNewlines(string s) => s.IndexOf('\r') < 0 ? s : s.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>str.isspace() for one character (adds U+001C..U+001F, which .NET does not treat as white space).</summary>
    public static bool IsSpace(char c) => char.IsWhiteSpace(c) || (c >= '\x1c' && c <= '\x1f');

    /// <summary>str.strip() with no arguments.</summary>
    public static string Strip(string s)
    {
        int i = 0, j = s.Length;
        while (i < j && IsSpace(s[i])) i++;
        while (j > i && IsSpace(s[j - 1])) j--;
        return s.Substring(i, j - i);
    }
}
