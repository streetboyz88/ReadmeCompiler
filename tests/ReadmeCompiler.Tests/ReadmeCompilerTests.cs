using System.Diagnostics;
using System.Text;
using Xunit;

namespace ReadmeCompiler.Tests;

// End-to-end tests: each one runs the real app as a process, feeds it console
// input, and inspects the compiled file. The app is a console program with
// top-level statements, so this exercises exactly what a user would.
public class ReadmeCompilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rc-tests-" + Guid.NewGuid().ToString("N"));
    private string Src => Path.Combine(_root, "src");
    private string Out => Path.Combine(_root, "out");
    private string OutFile => Path.Combine(Out, "src_README_compilation.txt");

    public ReadmeCompilerTests() => Directory.CreateDirectory(Src);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Readme(string folder, string fileName, string content)
    {
        string dir = Path.Combine(Src, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    // Runs the app with the given console lines (source/output dirs are prepended,
    // "quit" is appended) and returns everything it printed.
    private string Run(params string[] answers)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "ReadmeCompiler.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };

        using var p = Process.Start(psi)!;
        var lines = new List<string> { Src, Out };
        lines.AddRange(answers);
        lines.Add("quit");
        foreach (var l in lines) p.StandardInput.WriteLine(l);
        p.StandardInput.Close();

        string stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(30_000), "app did not exit");
        Assert.Equal(0, p.ExitCode);
        return stdout;
    }

    private string Compiled() => File.ReadAllText(OutFile);

    [Fact]
    public void Version_IsShownInBannerAndViaFlag_AndIsV1()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "ReadmeCompiler.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" --version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string version = p.StandardOutput.ReadToEnd().Trim();
        Assert.True(p.WaitForExit(30_000));

        Assert.Matches(@"^1\.\d+\.\d+$", version);
        Assert.Contains($"=== Readme Compiler v{version} ===", Run());
    }

    [Fact]
    public void Placeholders_AreReplacedAndQuoted_LocAndLocationCodeShareOneValue()
    {
        Readme("p1", "readme.txt", "values (@Coy, @loc, @location_code, @LOC)");
        Run("PMG", "JKT");

        Assert.Contains("values ('PMG', 'JKT', 'JKT', 'JKT')", Compiled());
    }

    [Fact]
    public void SimilarButDifferentTokens_AreNotTouched()
    {
        Readme("p1", "readme.txt", "@location_id @location @Company_Name mail a@coy.com @coy");
        Run("PMG", "JKT"); // @loc/@location_code absent -> only the @coy prompt appears

        string text = Compiled();
        Assert.Contains("@location_id @location @Company_Name mail a@coy.com 'PMG'", text);
    }

    [Fact]
    public void Skipping_LeavesPlaceholdersUnchanged()
    {
        Readme("p1", "readme.txt", "values (@Coy, @loc)");
        string console = Run("", "");

        Assert.Contains("values (@Coy, @loc)", Compiled());
        Assert.Contains("Skipped: @Coy left unchanged.", console);
        Assert.Contains("Skipped: @loc / @location_code left unchanged.", console);
    }

    [Fact]
    public void NoPlaceholders_NoPromptIsShown()
    {
        Readme("p1", "readme.txt", "nothing to replace");
        string console = Run();

        Assert.DoesNotContain("(OPTIONAL)", console);
        Assert.Contains("nothing to replace", Compiled());
    }

    [Fact]
    public void ValueContainingAnotherPlaceholder_IsNotReplacedTwice()
    {
        Readme("p1", "readme.txt", "values (@Coy, @loc)");
        Run("A @loc B", "X");

        Assert.Contains("values ('A @loc B', 'X')", Compiled());
    }

    [Fact]
    public void QuotesInValue_AreEscapedAndNotDoubled()
    {
        Readme("p1", "readme.txt", "(@Coy, @loc)");
        Run("O'Brien", "'JKT'");

        Assert.Contains("('O''Brien', 'JKT')", Compiled());
    }

    [Fact]
    public void AnsiEncodedReadme_KeepsAccentedCharacters()
    {
        Directory.CreateDirectory(Path.Combine(Src, "p1"));
        File.WriteAllBytes(Path.Combine(Src, "p1", "readme.txt"), new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 });
        Run();

        Assert.Contains("café", Compiled());
    }

    [Fact]
    public void EmptySource_WritesNothing()
    {
        string console = Run();

        Assert.False(File.Exists(OutFile));
        Assert.Contains("No subfolders found", console);
    }

    [Fact]
    public void SubfoldersWithoutReadme_StillWritesNoReadmeExist()
    {
        Directory.CreateDirectory(Path.Combine(Src, "a"));
        Directory.CreateDirectory(Path.Combine(Src, "b"));
        File.WriteAllText(Path.Combine(Src, "a", "notes.txt"), "hi");
        Run();

        string text = Compiled();
        Assert.Contains("No readme exist.", text);
        Assert.Contains("  - a", text);
        Assert.Contains("  - b", text);
    }

    [Fact]
    public void ReadmeNestedOneLevelDeeper_IsIgnored()
    {
        Readme(Path.Combine("p1", "inner"), "readme.txt", "DEEP");
        Readme("p2", "readme.txt", "SHALLOW");
        Run();

        string text = Compiled();
        Assert.Contains("SHALLOW", text);
        Assert.DoesNotContain("DEEP", text);
    }

    [Fact]
    public void ReadmeFileNameMatching()
    {
        Readme("p1", "readme (png only).txt", "MATCH1");
        Readme("p2", "R E A D M E.txt", "MATCH2");
        Readme("p3", "readmefile.txt", "NOMATCH");
        Readme("p4", "readme.md", "NOMATCH2");
        Run();

        string text = Compiled();
        Assert.Contains("MATCH1", text);
        Assert.Contains("MATCH2", text);
        Assert.DoesNotContain("NOMATCH", text);
    }

    [Fact]
    public void Folders_AreSortedNaturally()
    {
        Readme("5.39.10", "readme.txt", "TEN");
        Readme("5.39.9", "readme.txt", "NINE");
        Run();

        string text = Compiled();
        Assert.True(text.IndexOf("NINE", StringComparison.Ordinal) < text.IndexOf("TEN", StringComparison.Ordinal));
    }
}
