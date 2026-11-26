# Readme Compiler

A small .NET console app that collects the readme files from a folder of patch/release
subfolders and compiles them into a single text file. It can also fill in the
`@Coy`, `@loc` and `@location_code` placeholders used in the readmes' SQL snippets.

## What it does

1. Scans the **immediate subfolders** of a source directory (folders are sorted
   naturally, so `5.39.9` comes before `5.39.10`).
2. In each subfolder, picks up readme-style `.txt` files, for example `readme.txt`,
   `README.txt`, `read me.txt`, `R E A D M E.txt` or `readme (png only).txt`.
   Names such as `readmefile.txt` or `readme.md` are not matched.
3. Writes everything to `<source folder name>_README_compilation.txt` in the output
   directory, with a header for each file showing its folder and file name.

Readmes nested **more than one level** down (for example inside an `old` folder) are
ignored.

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download) (to build or run from source)

## Usage

```
dotnet run
```

or run the built `ReadmeCompiler.exe`. The app then asks for:

```
Enter source directory:
Enter output directory:
```

Type `quit` at any prompt to exit. The output directory is created if it does not exist.

Check the version with:

```
ReadmeCompiler.exe --version
```

## Placeholders

If the compiled text contains any of the placeholders below, the app asks for a value.
It only asks about the ones it actually finds.

| Prompt | Replaces |
|---|---|
| `Enter value for @Coy (OPTIONAL):` | `@Coy` |
| `Enter value for @loc / @location_code (OPTIONAL):` | `@loc` and `@location_code` (treated as the same thing) |

- **Quotes are added automatically.** Type `PMG`, not `'PMG'`. A quote inside the value
  is escaped (`O'Brien` becomes `'O''Brien'`), and a value you already quoted is not
  double-quoted.
- **Skipping:** press Enter to leave a placeholder unchanged in the output.
- Matching ignores case (`@coy`, `@Coy`, `@COY`).
- Only these exact tokens are replaced. `@location`, `@location_id`, `@Company_Name`
  and email addresses such as `user@coy.com` are left alone.
- If there are no placeholders, no prompt is shown.

## Behavior notes

- **Encoding:** files with a BOM are read as such. Otherwise the app tries UTF-8 and
  falls back to Windows-1252, so older ANSI readmes keep their accented characters.
  The output is UTF-8 with a BOM.
- **No readmes found:** if subfolders exist but none has a readme, the output file
  still contains `No readme exist.` plus the list of folders.
- **No subfolders:** if the source has no subfolders, nothing is written.
- **Unreadable folders:** a folder that cannot be read is skipped and listed, instead
  of stopping the whole run.
- Folders without a readme are listed in the console after the run.

## Tests and CI

The tests are end-to-end: each one runs the real app, feeds it console input and checks
the compiled file.

```
dotnet test tests/ReadmeCompiler.Tests -c Release
```

GitHub Actions (`.github/workflows/ci.yml`) builds and tests on Ubuntu and Windows for
every push to `main`/`master` and for every pull request.

## Versioning

The version is set in one place, `<Version>` in `ReadmeCompiler.csproj`. It is shown in
the startup banner and by `--version`.
