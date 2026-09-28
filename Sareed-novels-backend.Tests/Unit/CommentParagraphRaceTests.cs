using System.Reflection;
using Application.Comments;
using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// A comment on a paragraph that an edit removes at the same moment fails in SQL Server with a foreign key violation
/// (547) or as the deadlock victim (1205). That is the paragraph being gone, a 404 ParagraphNotFound, not a 500.
/// </summary>
public class CommentParagraphRaceTests
{
    private static Comments OnParagraph() => new() { Id = Guid.NewGuid(), UserId = "u", Content = "تعليق", ParagraphId = Guid.NewGuid() };

    private static Comments OnChapter() => new() { Id = Guid.NewGuid(), UserId = "u", Content = "تعليق", ChapterId = Guid.NewGuid() };

    [Theory]
    [InlineData(547)]
    [InlineData(1205)]
    public void A_paragraph_comment_refused_by_the_paragraphs_removal_lost_its_paragraph(int number)
    {
        var sql = SqlErrors.Exception(number);

        // SaveChanges wraps it; the counter updates (ExecuteUpdate) throw it as is; and EF's execution strategy wraps
        // a transient error (a deadlock) once more, in an InvalidOperationException around the DbUpdateException.
        Assert.True(CommentsRepository.LostItsParagraph(OnParagraph(), new DbUpdateException("save failed", sql)));
        Assert.True(CommentsRepository.LostItsParagraph(OnParagraph(), sql));
        Assert.True(CommentsRepository.LostItsParagraph(OnParagraph(),
            new InvalidOperationException("transient failure", new DbUpdateException("save failed", sql))));
    }

    [Theory]
    [InlineData(547)]
    [InlineData(1205)]
    public void A_chapter_or_post_comment_failing_the_same_way_stays_a_server_error(int number)
    {
        Assert.False(CommentsRepository.LostItsParagraph(OnChapter(), new DbUpdateException("save failed", SqlErrors.Exception(number))));
    }

    [Theory]
    [InlineData(2627)] // duplicate key
    [InlineData(-2)] // timeout
    public void Other_sql_errors_on_a_paragraph_comment_stay_server_errors(int number)
    {
        Assert.False(CommentsRepository.LostItsParagraph(OnParagraph(), new DbUpdateException("save failed", SqlErrors.Exception(number))));
    }

    [Fact]
    public void Failures_that_are_not_sql_errors_stay_server_errors()
    {
        Assert.False(CommentsRepository.LostItsParagraph(OnParagraph(), new InvalidOperationException("broken")));
        Assert.False(CommentsRepository.LostItsParagraph(OnParagraph(), new DbUpdateException("save failed", new TimeoutException())));
    }

    [Fact]
    public void The_404_is_the_one_a_missing_paragraph_gets()
    {
        var gone = ParagraphGone.Exception();

        Assert.Equal("ParagraphNotFound", gone.Code);
        Assert.Matches(@"\p{IsArabic}", gone.Message);
    }
}

/// <summary>Builds a <see cref="SqlException"/> with an error number (SqlClient has no public constructor for it).</summary>
internal static class SqlErrors
{
    public static SqlException Exception(int number)
    {
        const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Instance;
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var constructor = typeof(SqlError).GetConstructors(Internal).OrderByDescending(c => c.GetParameters().Length).First();
        var error = constructor.Invoke(constructor.GetParameters().Select(p => p.Name == "infoNumber" ? number : Default(p)).ToArray());
        typeof(SqlErrorCollection).GetMethod("Add", Internal)!.Invoke(errors, [error]);
        var create = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(SqlErrorCollection), typeof(string)])!;
        var exception = (SqlException)create.Invoke(null, [errors, "16.0"])!;
        Assert.Equal(number, exception.Number);
        return exception;
    }

    private static object? Default(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var t when t == typeof(string) => "",
        var t when t.IsValueType => Activator.CreateInstance(t),
        _ => null
    };
}
