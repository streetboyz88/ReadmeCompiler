using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;

// Version comes from <Version> in ReadmeCompiler.csproj (single source of truth).
// The SDK may append "+<commit hash>" to the informational version; drop it.
string appVersion = (Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
    .Split('+')[0];

if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
{
    Console.WriteLine(appVersion);
    return;
}

Console.WriteLine($"=== Readme Compiler v{appVersion} ===");
Console.WriteLine("Scans the immediate subfolders of a source directory for readme-style .txt files");
Console.WriteLine("(e.g. readme.txt, read me.txt, README.txt, R E A D M E.txt) and compiles their");
Console.WriteLine("content into a single output file.");
Console.WriteLine("Type 'quit' at any prompt to exit.");
Console.WriteLine();

while (true)
{
    string? sourceDir = PromptForDirectory("Enter source directory: ", mustExist: true);
    if (sourceDir is null) break;

    string? outputDir = PromptForDirectory("Enter output directory: ", mustExist: false);
    if (outputDir is null) break;

    try
    {
        CompileReadmes(sourceDir, outputDir);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
    }

    Console.WriteLine();
    Console.WriteLine(new string('-', 40));
    Console.WriteLine();
}

Console.WriteLine("Goodbye.");

static void CompileReadmes(string sourceDir, string outputDir)
{
    Directory.CreateDirectory(outputDir);

    string sourceName = new DirectoryInfo(sourceDir).Name;
    string outputFile = Path.Combine(outputDir, $"{sourceName}_README_compilation.txt");

    var folders = Directory.GetDirectories(sourceDir).OrderBy(f => f, new NaturalStringComparer()).ToList();
    var missing = new List<string>();
    var unreadable = new List<string>();
    var sb = new StringBuilder();
    int matchedCount = 0;

    foreach (var folder in folders)
    {
        string folderName = new DirectoryInfo(folder).Name;

        List<string> readmeFiles;
        try
        {
            readmeFiles = Directory.GetFiles(folder, "*.txt")
                .Where(f => IsReadmeStyleName(Path.GetFileName(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            unreadable.Add($"{folderName} ({ex.Message})");
            continue;
        }

        if (readmeFiles.Count == 0)
        {
            missing.Add(folderName);
            continue;
        }

        matchedCount++;
        foreach (var file in readmeFiles)
        {
            string content;
            try
            {
                content = ReadTextAuto(file);
            }
            catch (Exception ex)
            {
                content = $"[Could not read file: {ex.Message}]";
            }

            sb.AppendLine("-- " + new string('=', 80));
            sb.AppendLine($"-- Folder: {folderName}");
            sb.AppendLine($"-- File:   {Path.GetFileName(file)}");
            sb.AppendLine("-- " + new string('=', 80));
            sb.AppendLine(content);
            sb.AppendLine();
        }
    }

    if (folders.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine("No subfolders found in the source directory. Nothing was written.");
        return;
    }

    if (matchedCount == 0)
    {
        // Subfolders exist but none has a readme: still write a file saying so.
        sb.AppendLine("No readme exist.");
        sb.AppendLine();
        sb.AppendLine($"None of the {folders.Count} subfolders in \"{sourceName}\" has a readme-style file:");
        foreach (var name in missing) sb.AppendLine($"  - {name}");
        foreach (var name in unreadable) sb.AppendLine($"  - {name} [skipped: could not be read]");
    }

    string compiled = ApplyPlaceholders(sb.ToString());

    File.WriteAllText(outputFile, compiled, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    Console.WriteLine();
    Console.WriteLine($"Wrote: {outputFile}");
    Console.WriteLine($"{matchedCount} of {folders.Count} folders had a readme-style file.");
    PrintProblemFolders(missing, unreadable);
}

static void PrintProblemFolders(List<string> missing, List<string> unreadable)
{
    if (unreadable.Count > 0)
    {
        Console.WriteLine("Folders SKIPPED (could not be read):");
        foreach (var name in unreadable)
        {
            Console.WriteLine($"  - {name}");
        }
    }

    if (missing.Count > 0)
    {
        Console.WriteLine("Folders with NO readme-style file:");
        foreach (var name in missing)
        {
            Console.WriteLine($"  - {name}");
        }
    }
}

// Reads a text file honoring a BOM; without one, tries strict UTF-8 and falls back
// to Windows-1252 so legacy ANSI readmes keep their accented characters.
static string ReadTextAuto(string path)
{
    byte[] bytes = File.ReadAllBytes(path);

    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
    if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
    if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

    try
    {
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
    }
    catch (DecoderFallbackException)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(bytes);
    }
}

// Matches @Coy / @loc / @location_code (case-insensitive) as whole tokens only,
// so @location, @location_id or an email's "user@coy.com" are left alone.
// Prompts only for placeholders that actually occur in the compiled text.
// Empty input = skip (leave the placeholder untouched).
static string ApplyPlaceholders(string text)
{
    var pattern = new Regex(@"(?<!\w)@(?:(?<coy>coy)|(?<loc>location_code|loc))\b", RegexOptions.IgnoreCase);

    bool hasCoy = false, hasLoc = false;
    foreach (Match m in pattern.Matches(text))
    {
        if (m.Groups["coy"].Success) hasCoy = true;
        else hasLoc = true;
    }
    if (!hasCoy && !hasLoc) return text;

    string? coy = null, loc = null;

    Console.WriteLine();
    Console.WriteLine("Placeholders found in the compiled readme.");
    Console.WriteLine("  - Single quotes (') are added automatically, so type the value only (e.g. PMG, not 'PMG').");
    Console.WriteLine("  - Skipping: press Enter on a prompt to leave that placeholder unchanged in the output.");

    if (hasCoy)
    {
        Console.Write("Enter value for @Coy (OPTIONAL): ");
        coy = QuoteSql(Console.ReadLine());
        if (coy is null)
            Console.WriteLine("  Skipped: @Coy left unchanged.");
    }

    if (hasLoc)
    {
        Console.Write("Enter value for @loc / @location_code (OPTIONAL): ");
        loc = QuoteSql(Console.ReadLine());
        if (loc is null)
            Console.WriteLine("  Skipped: @loc / @location_code left unchanged.");
    }

    // Single pass over the original text, so a value that itself contains
    // something like "@loc" is never re-replaced.
    return pattern.Replace(text, m =>
    {
        string? value = m.Groups["coy"].Success ? coy : loc;
        return value ?? m.Value;
    });
}

// Wraps the value in single quotes (doubling any embedded quote).
// Returns null for empty input. A value the user already quoted is not double-quoted.
static string? QuoteSql(string? raw)
{
    string value = raw?.Trim() ?? "";
    if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        value = value.Substring(1, value.Length - 2).Replace("''", "'");
    if (value.Length == 0) return null;
    return "'" + value.Replace("'", "''") + "'";
}

static bool IsReadmeStyleName(string fileName)
{
    if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;

    string nameWithoutExt = fileName.Substring(0, fileName.Length - ".txt".Length);
    string normalized = new string(nameWithoutExt.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    if (!normalized.StartsWith("readme")) return false;

    // Require a word boundary after "readme" so names like "readme.txt",
    // "readme (png only).txt", "readme_v2.txt" match, but "readmefile.txt" does not.
    string remainder = normalized.Substring("readme".Length);
    return remainder.Length == 0 || !char.IsLetterOrDigit(remainder[0]);
}

// Returns null when the user types "quit" or input ends.
static string? PromptForDirectory(string prompt, bool mustExist)
{
    while (true)
    {
        Console.Write(prompt);
        string? rawInput = Console.ReadLine();

        if (rawInput is null)
        {
            Console.WriteLine();
            return null;
        }

        string input = rawInput.Trim('﻿', ' ', '\t').Trim('"').Trim();

        if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return null;

        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Please enter a path.");
            continue;
        }

        if (mustExist && !Directory.Exists(input))
        {
            Console.WriteLine($"Directory not found: {input}");
            continue;
        }

        return input;
    }
}

// Splits into digit / non-digit chunks and compares numeric chunks by value,
// so "5.39.9" sorts before "5.39.10" instead of after it.
class NaturalStringComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);

        var xChunks = Tokenize(x);
        var yChunks = Tokenize(y);

        int count = Math.Min(xChunks.Count, yChunks.Count);
        for (int i = 0; i < count; i++)
        {
            string a = xChunks[i];
            string b = yChunks[i];

            bool aIsNum = char.IsDigit(a[0]);
            bool bIsNum = char.IsDigit(b[0]);

            int cmp;
            if (aIsNum && bIsNum)
            {
                cmp = a.TrimStart('0').Length.CompareTo(b.TrimStart('0').Length);
                if (cmp == 0) cmp = string.CompareOrdinal(a.TrimStart('0'), b.TrimStart('0'));
            }
            else
            {
                cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }

            if (cmp != 0) return cmp;
        }

        return xChunks.Count.CompareTo(yChunks.Count);
    }

    private static List<string> Tokenize(string s)
    {
        var chunks = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            bool isDigit = char.IsDigit(s[i]);
            while (i < s.Length && char.IsDigit(s[i]) == isDigit) i++;
            chunks.Add(s.Substring(start, i - start));
        }
        return chunks;
    }
}
