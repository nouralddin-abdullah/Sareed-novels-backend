using Application.Reviews.Commands.CreateReview;
using Application.Reviews.Commands.UpdateReview;
using Domain.Entities;
using Domain.Reviews;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>What an edit of a review changes (#34), and that what it sends is checked as when writing a review.</summary>
public class ReviewEditTests
{
    private static Review Stored() => new()
    {
        WritingQualityScore = 4, UpdatingStabilityScore = 3, CharacterDevelopmentScore = 2, WorldBuildingScore = 5,
        Content = "رواية جميلة جداً", IsSpoiler = true
    };

    [Fact]
    public void Nothing_sent_or_the_same_values_is_no_change()
    {
        Assert.False(ReviewEdit.Of(null, null, null, null, null, null).Changes(Stored()));
        Assert.False(ReviewEdit.Of(4, 3, 2, 5, "رواية جميلة جداً", true).Changes(Stored()));
        Assert.False(ReviewEdit.Of(4.00m, 3.0m, null, null, null, null).Changes(Stored()));
    }

    [Fact]
    public void One_field_that_differs_is_a_change()
    {
        var edits = new[]
        {
            ReviewEdit.Of(5, null, null, null, null, null),
            ReviewEdit.Of(null, 4, null, null, null, null),
            ReviewEdit.Of(null, null, 1, null, null, null),
            ReviewEdit.Of(null, null, null, 4.5m, null, null),
            ReviewEdit.Of(null, null, null, null, "رواية جميلة جدا", null),
            ReviewEdit.Of(null, null, null, null, "", null),
            ReviewEdit.Of(null, null, null, null, null, false)
        };

        Assert.All(edits, edit => Assert.True(edit.Changes(Stored()), edit.ToString()));
    }

    [Fact]
    public void Text_left_out_stays_and_empty_text_removes_it()
    {
        var leftOut = ReviewEdit.Of(5, null, null, null, null, null);
        Assert.False(leftOut.ReplacesContent);

        foreach (var empty in new[] { "", "   " })
        {
            var edit = ReviewEdit.Of(null, null, null, null, empty, null);
            Assert.True(edit.ReplacesContent);
            Assert.Null(edit.Content);
            // A review without text already has none, whether it was stored as null or empty.
            Assert.False(edit.Changes(new Review { Content = null }));
            Assert.False(edit.Changes(new Review { Content = "" }));
        }

        var text = ReviewEdit.Of(null, null, null, null, " نص المراجعة ", null);
        Assert.Equal(" نص المراجعة ", text.Content);
    }

    [Fact]
    public void Writing_a_review_keeps_its_messages()
    {
        var result = new CreateReviewCommandValidator().Validate(new CreateReviewCommandrRequest
        {
            WritingQualityScore = 6, UpdatingStabilityScore = 0, CharacterDevelopmentScore = 9, WorldBuildingScore = -1, Content = "قصير"
        });

        Assert.Equal(new[]
        {
            ("WritingQualityScore", "تقييم جودة الكتابة يجب أن يكون من 1 إلى 5"),
            ("UpdatingStabilityScore", "تقييم استقرار التحديثات يجب أن يكون من 1 إلى 5"),
            ("CharacterDevelopmentScore", "تقييم بناء الشخصيات يجب أن يكون من 1 إلى 5"),
            ("WorldBuildingScore", "تقييم بناء العالم القصصي يجب أن يكون من 1 إلى 5"),
            ("Content", "المراجعة قصيرة جدًا. اكتب 5 أحرف على الأقل أو اتركها فارغة.")
        }, result.Errors.Select(e => (e.PropertyName, e.ErrorMessage)));
        Assert.Equal("يجب ألا تتجاوز المراجعة 2000 حرف", Assert.Single(new CreateReviewCommandValidator().Validate(new CreateReviewCommandrRequest
        {
            WritingQualityScore = 1, UpdatingStabilityScore = 1, CharacterDevelopmentScore = 1, WorldBuildingScore = 1, Content = new string('ن', 2001)
        }).Errors).ErrorMessage);
    }

    [Fact]
    public void An_edit_is_checked_as_writing_a_review_for_the_fields_it_sends()
    {
        var create = new CreateReviewCommandValidator().Validate(new CreateReviewCommandrRequest
        {
            WritingQualityScore = 6, UpdatingStabilityScore = 0, CharacterDevelopmentScore = 9, WorldBuildingScore = -1, Content = "قصير"
        });
        var edit = new UpdateReviewRequestValidator().Validate(new UpdateReviewRequest
        {
            WritingQualityScore = 6, UpdatingStabilityScore = 0, CharacterDevelopmentScore = 9, WorldBuildingScore = -1, Content = "قصير"
        });

        Assert.Equal(create.Errors.Select(e => (e.PropertyName, e.ErrorMessage)), edit.Errors.Select(e => (e.PropertyName, e.ErrorMessage)));

        var validator = new UpdateReviewRequestValidator();
        Assert.True(validator.Validate(new UpdateReviewRequest()).IsValid);
        Assert.True(validator.Validate(new UpdateReviewRequest { Content = "" }).IsValid);
        Assert.True(validator.Validate(new UpdateReviewRequest { WorldBuildingScore = 1, Content = "مراجعة جديدة" }).IsValid);
        Assert.Equal("WorldBuildingScore", Assert.Single(validator.Validate(new UpdateReviewRequest { WorldBuildingScore = 5.01m }).Errors).PropertyName);
    }
}
