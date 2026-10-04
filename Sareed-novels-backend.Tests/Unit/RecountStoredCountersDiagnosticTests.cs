using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Infrastructure.Migrations;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The read-only diagnostic of <see cref="RecountStoredCounters"/> (#66), the .sql file next to the migration that the
/// owner runs on production before the deploy: it is the migration's own recount, generated from the same counter
/// definitions, and it has nothing but SELECT statements. What it reports is checked against what the migration does
/// in <c>RecountStoredCountersHttpTests</c>.
/// </summary>
public class RecountStoredCountersDiagnosticTests
{
    /// <summary>The checked-in file, copied next to the tests (the test project links it).</summary>
    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "Diagnostics", "RecountStoredCounters.Diagnostic.sql");

    /// <summary>The file's statements, without its comment lines.</summary>
    internal static List<string> Statements(string script) =>
        string.Join('\n', script.ReplaceLineEndings("\n").Split('\n').Where(line => !line.TrimStart().StartsWith("--")))
            .Split(';')
            .Select(statement => statement.Trim())
            .Where(statement => statement.Length > 0)
            .ToList();

    [Fact]
    public void The_file_is_the_migrations_recount_as_selects()
    {
        // SARD_WRITE_DIAGNOSTIC=1 writes the migration's text into the source file, for when the migration changes
        // before it ships; the test still compares the copy it was built with.
        if (Environment.GetEnvironmentVariable("SARD_WRITE_DIAGNOSTIC") == "1")
        {
            File.WriteAllText(SourceFile(), RecountStoredCounters.Diagnostic.ReplaceLineEndings("\n"));
        }

        Assert.True(File.Exists(FilePath), $"{FilePath} is missing: the test project should copy the diagnostic next to the tests.");
        Assert.True(RecountStoredCounters.Diagnostic.ReplaceLineEndings("\n") == File.ReadAllText(FilePath).ReplaceLineEndings("\n"),
            "The diagnostic file isn't the migration's RecountStoredCounters.Diagnostic: run this test once with " +
            "SARD_WRITE_DIAGNOSTIC=1 to write it, then build again.");
    }

    [Fact]
    public void The_file_only_reads()
    {
        var statements = Statements(File.ReadAllText(FilePath));

        // The summary, the rows, and the comment places.
        Assert.Equal(3, statements.Count);
        Assert.All(statements, statement =>
        {
            Assert.StartsWith("SELECT ", statement);
            Assert.DoesNotMatch(new Regex(
                @"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|SP_\w+|XP_\w+|DROP|ALTER|CREATE|TRUNCATE|GRANT|DENY|REVOKE|" +
                @"SET|DECLARE|BEGIN|COMMIT|ROLLBACK|SAVE|BACKUP|RESTORE|DBCC|KILL|SHUTDOWN|OPENROWSET|OPENQUERY|" +
                @"OPENDATASOURCE|BULK|WAITFOR|USE|HOLDLOCK|UPDLOCK|XLOCK|TABLOCKX)\b", RegexOptions.IgnoreCase), statement);
        });
    }

    [Fact]
    public void Every_counter_is_in_the_recount_once_and_in_both_parts_of_the_diagnostic()
    {
        var names = RecountStoredCounters.Counters.Select(c => c.Name).ToList();
        Assert.Equal(names.Distinct(), names);

        var statements = Statements(RecountStoredCounters.Diagnostic);
        foreach (var counter in RecountStoredCounters.Counters)
        {
            Assert.Single(Regex.Matches(RecountStoredCounters.Recount, $@"\bUPDATE t SET {counter.Column} = r\.Value\s+FROM {counter.Table} t\b"));
            Assert.Contains($"N'{counter.Name}' AS Counter", statements[0]);
            Assert.Contains($"N'{counter.Name}' AS Counter", statements[1]);
        }
        Assert.Equal(names.Count, Regex.Matches(RecountStoredCounters.Recount, @"\bUPDATE\b").Count);
    }

    private static string SourceFile([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Infrastructure", "Migrations",
            "20261004164302_RecountStoredCounters.Diagnostic.sql"));
}
