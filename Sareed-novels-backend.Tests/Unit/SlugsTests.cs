using Application.Novels.Commands.UpdateNovel;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using Domain.Seo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class SlugsTests
{
    private static readonly Guid Id = Guid.Parse("6949f0aa-0000-4000-8000-000000000001");

    [Theory]
    [InlineData("Bob Novel", "6949f-bob-novel")]
    [InlineData("  مذكرات كائن فضائي ", "6949f-مذكرات-كائن-فضائي")]
    [InlineData("ديجور الهوى/ كاملة", "6949f-ديجور-الهوى-كاملة")]
    [InlineData("غدًا في الساعة 6:17", "6949f-غدا-في-الساعة-6-17")]
    [InlineData("لعنة الفراعنة  (قيد التعديل )", "6949f-لعنة-الفراعنة-قيد-التعديل")]
    [InlineData("What? #1 & 100% done\\now", "6949f-what-1-100-done-now")]
    [InlineData("سَرْد ــ الرواية", "6949f-سرد-الرواية")]
    [InlineData("في عالم ٱخر", "6949f-في-عالم-ٱخر")]
    public void Slugs_are_the_id_prefix_and_the_title_in_dashes(string title, string expected)
    {
        Assert.Equal(expected, Slugs.For(Id, title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("?!...")]
    [InlineData(null)]
    public void A_title_without_letters_leaves_only_the_prefix(string? title)
    {
        Assert.Equal("6949f", Slugs.For(Id, title));
    }

    [Fact]
    public void Slugs_never_contain_url_breaking_characters()
    {
        var slug = Slugs.For(Id, "a/b\\c?d#e%f&g:h;i\"j'k<l>m n\tо‏p");

        Assert.Matches("^[0-9a-f]{5}(-[\\p{L}\\p{Nd}]+)*$", slug);
    }

    [Fact]
    public void Long_titles_are_cut_without_a_trailing_dash()
    {
        var slug = Slugs.TitlePart(string.Join(' ', Enumerable.Repeat("كلمة", 40)));

        Assert.True(slug.Length <= Slugs.MaxTitleLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public async Task Saving_details_with_the_unchanged_title_keeps_the_existing_url()
    {
        // 9 of 75 public novels in production have a slug the old update formula would rewrite (trailing dash,
        // ':' or '/'); the web editor resends the title on every save.
        var (handler, novel) = UpdateHandlerFor(title: "مذكرات كائن فضائي ", slug: "76c3c-مذكرات-كائن-فضائي-");

        await handler.Handle(new UpdateNovelCommand(novel.Id, "مذكرات كائن فضائي", "summary", null, null), CancellationToken.None);

        Assert.Equal("76c3c-مذكرات-كائن-فضائي-", novel.Slug);
    }

    [Fact]
    public async Task Renaming_a_novel_gives_it_a_clean_slug()
    {
        var (handler, novel) = UpdateHandlerFor(title: "Old title", slug: "76c3c-old-title");

        await handler.Handle(new UpdateNovelCommand(novel.Id, "New: title/2", null, null, null), CancellationToken.None);

        Assert.Equal(Slugs.For(novel.Id, "New: title/2"), novel.Slug);
        Assert.EndsWith("-new-title-2", novel.Slug);
    }

    private static (UpdateNovelCommandHandler, Novel) UpdateHandlerFor(string title, string slug)
    {
        var novel = new Novel { Id = Guid.NewGuid(), AuthorId = "author", Title = title, Slug = slug, Summary = "s", CoverImageUrl = "" };
        var novels = Substitute.For<INovelsRepository>();
        novels.GetOne(novel.Id).Returns(novel);
        novels.UpdateOne(novel).Returns(true);
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser("author", "e", "u", "d"));
        var mapper = Substitute.For<IMapper>();
        mapper.When(m => m.Map(Arg.Any<UpdateNovelCommand>(), novel)).Do(call => novel.Title = ((UpdateNovelCommand)call[0]).Title ?? novel.Title);

        var handler = new UpdateNovelCommandHandler(
            NullLogger<UpdateNovelCommandHandler>.Instance, userContext, Substitute.For<INovelGenresRepository>(),
            Substitute.For<IGenresRepository>(), novels,
            mapper, Substitute.For<INovelRecommendationService>());
        return (handler, novel);
    }
}
