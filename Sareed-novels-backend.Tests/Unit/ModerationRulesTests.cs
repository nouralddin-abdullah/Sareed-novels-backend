using Application.Reports.Commands.CreateReport;
using Application.Reports.Commands.ResolveReport;
using Application.Users;
using Domain.Exceptions;
using Domain.Moderation;
using Infrastructure.Repositories;

namespace Sareed_novels_backend.Tests.Unit;

public class ModerationRulesTests
{
    [Fact]
    public void The_enum_names_are_the_ones_the_apps_send()
    {
        // The mobile app and the web send and read these names: renaming one breaks them.
        Assert.Equal(["Comment", "Review", "Post", "User", "Novel", "ReadingList"], Enum.GetNames<ReportTargetType>());
        Assert.Equal(["Spam", "Harassment", "Sexual", "Violence", "HateSpeech", "Spoiler", "Other"], Enum.GetNames<ReportReason>());
        Assert.Equal(["Dismiss", "RemoveContent", "SuspendUser"], Enum.GetNames<ReportAction>());
        Assert.Equal(["Open", "Resolved", "Dismissed"], Enum.GetNames<ReportStatus>());
    }

    [Theory]
    [InlineData("HateSpeech", true)]
    [InlineData("hatespeech", true)]
    [InlineData(" Spam ", true)]
    [InlineData("3", false)]
    [InlineData("-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Hate Speech", false)]
    [InlineData("Spam,Other", false)]
    public void Only_names_parse_and_numbers_do_not(string? value, bool parses)
    {
        Assert.Equal(parses, EnumNames.TryParse<ReportReason>(value, out _));
    }

    [Fact]
    public void A_complete_report_is_valid_and_a_broken_one_is_refused_in_arabic()
    {
        var validator = new CreateReportCommandValidator();
        Assert.True(validator.Validate(new CreateReportCommand
        {
            TargetType = "ReadingList", TargetId = Guid.NewGuid().ToString(), Reason = "Other", Details = null
        }).IsValid);

        var refused = validator.Validate(new CreateReportCommand { TargetType = "Chapter", TargetId = "x", Reason = "Rude", Details = new string('x', 1001) });

        Assert.Equal(["TargetType", "TargetId", "Reason", "Details"], refused.Errors.Select(e => e.PropertyName));
        Assert.All(refused.Errors, e => Assert.Matches(@"\p{IsArabic}", e.ErrorMessage));
    }

    [Theory]
    [InlineData("SuspendUser", 1, true)]
    [InlineData("SuspendUser", 3650, true)]
    [InlineData("SuspendUser", null, true)]
    [InlineData("dismiss", null, true)]
    [InlineData("SuspendUser", 0, false)]
    [InlineData("SuspendUser", 3651, false)]
    [InlineData("Ban", null, false)]
    public void Actions_and_suspension_days_are_checked(string action, int? days, bool valid)
    {
        Assert.Equal(valid, new ResolveReportRequestValidator().Validate(new ResolveReportRequest { Action = action, SuspensionDays = days }).IsValid);
    }

    [Fact]
    public void A_suspension_of_some_days_ends_then_and_one_without_days_never_does()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddDays(7), Suspension.Until(7, now));
        Assert.Equal(Suspension.Permanent, Suspension.Until(null, now));
        Assert.True(Suspension.IsPermanent(Suspension.Permanent));
        Assert.False(Suspension.IsPermanent(now.AddDays(Suspension.MaxDays)));
        Assert.True(Suspension.IsActive(now.AddSeconds(1), now));
        Assert.False(Suspension.IsActive(now, now));
        Assert.False(Suspension.IsActive(null, now));
    }

    [Fact]
    public void The_suspended_sign_in_message_gives_the_end_date_or_says_it_is_permanent()
    {
        var temporary = new AccountSuspendedException(new DateTime(2026, 10, 4, 18, 30, 0));
        var permanent = new AccountSuspendedException(Suspension.Permanent);

        Assert.Equal("AccountSuspended", temporary.Code);
        Assert.Contains("2026/10/04", temporary.Message);
        Assert.Contains("مؤقتاً", temporary.Message);
        Assert.False(temporary.Permanent);
        Assert.Equal(DateTimeKind.Utc, temporary.SuspendedUntil.Kind);
        Assert.True(permanent.Permanent);
        Assert.Contains("نهائياً", permanent.Message);
        Assert.Contains("support@sardnovels.com", permanent.Message);
        Assert.IsAssignableFrom<ForbidException>(permanent); // so the Google sign-in paths let it through
    }

    [Theory]
    [InlineData("blocked", false)]
    [InlineData("Blocked", false)]
    [InlineData("MY-PROFILE", false)]
    [InlineData("blocked1", true)]
    [InlineData("reader", true)]
    [InlineData(null, true)]
    public void User_names_that_are_routes_are_reserved(string? userName, bool allowed)
    {
        Assert.Equal(allowed, UserNameRules.IsNotReserved(userName));
    }

    [Fact]
    public void A_report_keeps_at_most_500_characters_of_the_text_without_splitting_an_emoji()
    {
        Assert.Null(ReportsRepository.Excerpt("   "));
        Assert.Equal("نص", ReportsRepository.Excerpt("  نص \n"));
        var exact = new string('a', 500);
        Assert.Equal(exact, ReportsRepository.Excerpt(exact));

        var cut = ReportsRepository.Excerpt(new string('a', 600))!;
        Assert.Equal(500, cut.Length);
        Assert.EndsWith("…", cut);

        // An emoji (two UTF-16 units) straddling the cut is dropped whole, not halved.
        var emoji = ReportsRepository.Excerpt(new string('a', 498) + "😀" + "tail")!;
        Assert.Equal(new string('a', 498) + "…", emoji);
    }
}
