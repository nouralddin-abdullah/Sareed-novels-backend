using Application.Users;
using Application.Users.Commands.UpdateMe;
using Domain.Entities;
using Domain.Repositories;
using Google.Apis.Auth;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// A new Google account's user name (#69): from the Google name when every letter of it is Latin, numbered when taken,
/// "sarduser" and six digits otherwise. Each one meets update-me's rules.
/// </summary>
public class GoogleUserNamesTests
{
    // ---- The handle a name gives (no lookup) ----

    [Theory]
    [InlineData("Shahd Elattar", "shahd-elattar")]
    [InlineData("shahd elattar", "shahd-elattar")]
    [InlineData("SHAHD ELATTAR", "shahd-elattar")]
    [InlineData("  Shahd \t  Elattar  ", "shahd-elattar")]
    [InlineData("Jean-Luc Picard", "jean-luc-picard")]
    [InlineData("Mary_Jane Watson", "mary-jane-watson")]
    [InlineData("J. R. R. Tolkien", "j-r-r-tolkien")]
    [InlineData("Jean – Luc", "jean-luc")]
    [InlineData("Noor", "noor")]
    public void A_latin_name_is_lower_case_words_joined_with_a_dash(string name, string handle)
    {
        Assert.Equal(handle, GoogleUserNames.FromName(name));
    }

    [Theory]
    [InlineData("Agent 47", "agent-47")]
    [InlineData("R2D2 Fan", "r2d2-fan")]
    [InlineData("Ｓａｒａ ２", "sara-2")]
    public void Digits_stay_in_the_words(string name, string handle)
    {
        Assert.Equal(handle, GoogleUserNames.FromName(name));
    }

    [Theory]
    [InlineData("Zoë Saldaña", "zoe-saldana")]
    [InlineData("José Ñúñez", "jose-nunez")]
    [InlineData("Nguyễn Văn An", "nguyen-van-an")]
    [InlineData("İlkay Gündoğan", "ilkay-gundogan")]
    [InlineData("Łukasz Żółć", "lukasz-zolc")]
    [InlineData("Søren Kierkegaard", "soren-kierkegaard")]
    [InlineData("Hans Straße", "hans-strasse")]
    [InlineData("ẞIG Ægir Œdipe", "ssig-aegir-oedipe")]
    [InlineData("Đorđe Þór", "dorde-thor")]
    [InlineData("Ħamrun Kılıç", "hamrun-kilic")]
    [InlineData("Zoë Decomposed", "zoe-decomposed")]
    public void Accents_are_stripped(string name, string handle)
    {
        Assert.Equal(handle, GoogleUserNames.FromName(name));
    }

    [Theory]
    [InlineData("𝓢𝓱𝓪𝓱𝓭 𝓔𝓵𝓪𝓽𝓽𝓪𝓻", "shahd-elattar")]
    [InlineData("Ｓｈａｈｄ", "shahd")]
    public void Styled_letters_are_plain_ones(string name, string handle)
    {
        Assert.Equal(handle, GoogleUserNames.FromName(name));
    }

    [Theory]
    [InlineData("Shahd ✨ Elattar 🌸", "shahd-elattar")]
    [InlineData("✨Shahd✨", "shahd")]
    [InlineData("Noor 👩‍💻", "noor")]
    [InlineData("O'Brien", "obrien")]
    [InlineData("D’Angelo Russo", "dangelo-russo")]
    [InlineData("Hawaiʻi Kai", "hawaii-kai")]
    [InlineData("John (JJ) Smith", "john-jj-smith")]
    [InlineData("Brand™ Name!", "brand-name")]
    [InlineData("Ahmed² @home", "ahmed-home")]
    [InlineData("Ahmed ٢٠٠٥", "ahmed")]
    public void Symbols_emoji_and_other_characters_are_dropped(string name, string handle)
    {
        Assert.Equal(handle, GoogleUserNames.FromName(name));
    }

    [Theory]
    [InlineData("Maria de los Angeles Garcia", "maria-de-los-angeles")]
    [InlineData("Abdelrahman Mohamed Mostafa", "abdelrahman-mohamed")]
    [InlineData("Christopher Alexander", "christopher")]
    [InlineData("Wolfeschlegelsteinhausenbergerdorff", "wolfeschlegelsteinha")]
    // Cutting at the word would leave "jo", too short for a user name: cut in the word.
    [InlineData("Jo Wolfeschlegelsteinhausen", "jo-wolfeschlegelstei")]
    public void A_long_name_is_cut_to_20_characters_at_a_word_where_possible(string name, string handle)
    {
        Assert.Equal(handle, GoogleUserNames.FromName(name));
        Assert.True(handle.Length <= UserNameRules.MaxLength);
    }

    [Theory]
    [InlineData("شهد العطار")]
    [InlineData("قارئة من Google")]
    [InlineData("Shahd شهد")]
    [InlineData("Shahd Elattar (شهد)")]
    [InlineData("Иван Петров")]
    [InlineData("Γιώργος")]
    [InlineData("Αlex")] // a Greek capital alpha
    [InlineData("山田太郎")]
    [InlineData("Shahd 山")]
    [InlineData("Shahd 𐐀")] // a Deseret letter, outside the BMP
    public void A_name_with_a_letter_that_is_not_latin_gives_no_handle(string name)
    {
        Assert.Null(GoogleUserNames.FromName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("✨🌸")]
    [InlineData("!!! ---")]
    [InlineData("12345")]
    [InlineData("٣٤٥")]
    public void A_name_without_a_latin_letter_gives_no_handle(string? name)
    {
        Assert.Null(GoogleUserNames.FromName(name));
    }

    // ---- Numbered handles ----

    [Fact]
    public void A_taken_handle_is_followed_by_the_same_with_2_to_20()
    {
        var numbered = GoogleUserNames.Numbered("shahd-elattar").ToList();

        Assert.Equal(GoogleUserNames.NumberedUpTo, numbered.Count);
        Assert.Equal(["shahd-elattar", "shahd-elattar-2", "shahd-elattar-3"], numbered.Take(3));
        Assert.Equal("shahd-elattar-20", numbered[^1]);
    }

    [Fact]
    public void A_long_handle_gives_up_letters_at_its_end_to_fit_its_number_in_20_characters()
    {
        var numbered = GoogleUserNames.Numbered("mohamed-abdelrahman").ToList();

        Assert.Equal("mohamed-abdelrahman", numbered[0]);
        Assert.Equal("mohamed-abdelrahma-2", numbered[1]);
        Assert.Equal("mohamed-abdelrahma-9", numbered[8]);
        Assert.Equal("mohamed-abdelrahm-10", numbered[9]);
        Assert.All(numbered, handle => Assert.InRange(handle.Length, UserNameRules.MinLength, UserNameRules.MaxLength));
        Assert.Equal(numbered.Count, numbered.Distinct().Count());

        // No dash is left before the number when the cut falls on one.
        Assert.Equal("abcdefghijklmnopq-2", GoogleUserNames.Numbered("abcdefghijklmnopq-rs").ElementAt(1));
    }

    // ---- With the lookup: the user names a new account tries ----

    private readonly IUsersRepository users = Substitute.For<IUsersRepository>();

    private GoogleUserNames Component() => new(Check(), users);

    /// <summary>update-me's checks as the API builds them: its validator, Identity's options and Arabic messages.</summary>
    internal static UserNameCheck Check()
    {
        var options = Options.Create(new IdentityOptions { User = { AllowedUserNameCharacters = UserNameRules.AllowedCharacters } });
        var userManager = new UserManager<User>(Substitute.For<IUserStore<User>>(), options, null!, null!, null!,
            new UpperInvariantLookupNormalizer(), new ArabicIdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
        return new UserNameCheck(new UpdateMeCommandValidator(), userManager);
    }

    private void Taken(params string[] userNames) =>
        users.GetTakenUserNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<string>>().Where(userNames.Contains).ToHashSet());

    private static GoogleJsonWebSignature.Payload Google(string? name, string? givenName = null, string? familyName = null) => new()
    {
        Subject = "google-subject-" + Guid.NewGuid().ToString("N"),
        Email = "shahd.elattar.private@example.test",
        Name = name,
        GivenName = givenName,
        FamilyName = familyName
    };

    private static string[] Fallbacks(GoogleJsonWebSignature.Payload payload) =>
        Enumerable.Range(0, GoogleUserNames.FallbackAttempts).Select(attempt => GoogleUserNames.Fallback(payload.Subject, attempt)).ToArray();

    [Fact]
    public async Task A_free_handle_comes_first_then_its_numbers_then_the_sarduser_ones()
    {
        Taken();
        var payload = Google("Shahd Elattar");

        var candidates = await Component().CandidatesAsync(payload, CancellationToken.None);

        Assert.Equal([.. GoogleUserNames.Numbered("shahd-elattar"), .. Fallbacks(payload)], candidates);
    }

    [Fact]
    public async Task Taken_handles_are_skipped_and_all_twenty_are_looked_up_in_one_query()
    {
        Taken("shahd-elattar", "shahd-elattar-2");

        var candidates = await Component().CandidatesAsync(Google("Shahd Elattar"), CancellationToken.None);

        Assert.Equal("shahd-elattar-3", candidates[0]);
        await users.Received(1).GetTakenUserNamesAsync(
            Arg.Is<IReadOnlyCollection<string>>(names => names.SequenceEqual(GoogleUserNames.Numbered("shahd-elattar"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_all_twenty_are_taken_only_the_sarduser_ones_are_left()
    {
        Taken([.. GoogleUserNames.Numbered("shahd-elattar")]);
        var payload = Google("Shahd Elattar");

        Assert.Equal(Fallbacks(payload), await Component().CandidatesAsync(payload, CancellationToken.None));
    }

    [Theory]
    [InlineData("شهد العطار")]
    [InlineData("Shahd شهد")]
    [InlineData("✨")]
    [InlineData(null)]
    [InlineData("Al")] // too short for a user name
    [InlineData("Blocked")] // reserved: a route name
    [InlineData("Username Available")]
    [InlineData("Deleted Smith")] // deleted accounts' prefix
    public async Task A_name_that_gives_no_usable_handle_gets_a_sarduser_one_without_a_lookup(string? name)
    {
        var payload = Google(name);

        Assert.Equal(Fallbacks(payload), await Component().CandidatesAsync(payload, CancellationToken.None));
        await users.DidNotReceiveWithAnyArgs().GetTakenUserNamesAsync(default!, default);
    }

    [Fact]
    public async Task Numbers_that_break_a_rule_are_skipped()
    {
        // "deleted" is a name like any other, but "deleted-2" starts like a deleted account's.
        Taken();
        var payload = Google("Deleted");

        Assert.Equal(["deleted", .. Fallbacks(payload)], await Component().CandidatesAsync(payload, CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_full_name_the_given_and_family_names_are_used_and_never_the_email()
    {
        Taken();

        var given = await Component().CandidatesAsync(Google(null, "Shahd", "Elattar"), CancellationToken.None);
        var none = Google(null);
        var neither = await Component().CandidatesAsync(none, CancellationToken.None);

        Assert.Equal("shahd-elattar", given[0]);
        Assert.Equal(Fallbacks(none), neither);
        Assert.DoesNotContain(neither, candidate => candidate.Contains("shahd") || candidate.Contains("private"));
    }

    public static TheoryData<string?> Names() => new()
    {
        "Shahd Elattar", "Maria de los Angeles Garcia", "Jo Wolfeschlegelsteinhausen", "Wolfeschlegelsteinhausenbergerdorff",
        "Zoë Saldaña", "R2D2", "Bob", "Deleted", "Mohamed Abdelrahman", "abc-defghijklmnop-qr", "شهد", null
    };

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Every_user_name_tried_meets_update_mes_rules(string? name)
    {
        Taken();
        var updateMe = new UpdateMeCommandValidator();

        var candidates = await Component().CandidatesAsync(Google(name), CancellationToken.None);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, userName =>
        {
            Assert.True(updateMe.Validate(new UpdateMeCommand { UserName = userName }).IsValid, userName);
            Assert.False(UserNameRules.LooksDeleted(userName), userName);
            Assert.All(userName, c => Assert.Contains(c, UserNameRules.AllowedCharacters));
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", userName);
        });
    }

    // ---- update-me's validator says which code each of its user name rules is ----

    [Fact]
    public void Every_user_name_rule_of_update_me_has_a_code_the_availability_check_answers()
    {
        var components = new UpdateMeCommandValidator().CreateDescriptor()
            .GetRulesForMember(nameof(UpdateMeCommand.UserName))
            .SelectMany(rule => rule.Components)
            .ToList();

        Assert.Equal(4, components.Count);
        Assert.All(components, c => Assert.Contains(c.ErrorCode, new[] { UserNameRules.InvalidCode, UserNameRules.ReservedCode }));
    }
}
