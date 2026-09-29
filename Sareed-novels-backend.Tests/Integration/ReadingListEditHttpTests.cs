using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Editing a reading list (#35), PATCH /api/readinglist/{id} with multipart form-data as the web and the app send it,
/// through the real model binding and validation: a field left out stays, an empty description clears it,
/// RemoveCover=true removes the picture, and the edit is validated as creating a list is.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ReadingListEditHttpTests(SardApiFactory api)
{
    private static MultipartFormDataContent WithImage(MultipartFormDataContent form, string contentType = "image/png")
    {
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(image, "CoverImage", "cover.png");
        return form;
    }

    private async Task<Guid> CreateList(ApiUser owner, string? description = null, bool withCover = false)
    {
        var form = ReaderApi.Form(("Name", "قائمتي " + Seed.Marker()), ("IsPublic", "true"));
        if (description != null)
        {
            form.Add(new StringContent(description), "Description");
        }
        var response = await api.Send(HttpMethod.Post, "/api/readinglist", owner, withCover ? WithImage(form) : form);
        return (await response.OkJson()).GetProperty("readingList").GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Edit(ApiUser user, Guid list, MultipartFormDataContent form) =>
        api.Send(HttpMethod.Patch, $"/api/readinglist/{list}", user, form);

    private async Task Edited(ApiUser user, Guid list, MultipartFormDataContent form)
    {
        var body = await (await Edit(user, list, form)).OkJson();
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    private async Task<JsonElement> Detail(ApiUser user, Guid list) =>
        await (await api.Get($"/api/readinglist/{list}", user)).OkJson();

    /// <summary>The list as GET my-lists shows it to its owner.</summary>
    private async Task<JsonElement> InMyLists(ApiUser owner, Guid list)
    {
        var page = await (await api.Get("/api/readinglist/my-lists?pageSize=100", owner)).OkJson();
        return page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == list);
    }

    private static string? Text(JsonElement json, string property) => json.GetProperty(property).GetString();

    [Fact]
    public async Task An_empty_description_clears_it_and_a_left_out_one_stays()
    {
        var owner = await api.SignUp();
        var list = await CreateList(owner, description: "روايات الصيف");
        var name = Text(await Detail(owner, list), "name");

        await Edited(owner, list, ReaderApi.Form(("IsPublic", "false")));
        Assert.Equal("روايات الصيف", Text(await Detail(owner, list), "description"));

        await Edited(owner, list, ReaderApi.Form(("Description", "")));
        var cleared = await Detail(owner, list);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await InMyLists(owner, list)).GetProperty("description").ValueKind);
        Assert.Equal(name, Text(cleared, "name"));
        Assert.False(cleared.GetProperty("isPublic").GetBoolean());

        await Edited(owner, list, ReaderApi.Form(("Description", "وصف جديد")));
        Assert.Equal("وصف جديد", Text(await Detail(owner, list), "description"));

        // The whole form as the web sends it (camelCase, every text field), with the description emptied and a blank
        // one as well.
        await Edited(owner, list, ReaderApi.Form(("name", name!), ("description", ""), ("isPublic", "true")));
        Assert.Equal(JsonValueKind.Null, (await Detail(owner, list)).GetProperty("description").ValueKind);
        await Edited(owner, list, ReaderApi.Form(("Description", "وصف")));
        await Edited(owner, list, ReaderApi.Form(("Description", "   ")));
        var blank = await Detail(owner, list);
        Assert.Equal(JsonValueKind.Null, blank.GetProperty("description").ValueKind);
        Assert.Equal(name, Text(blank, "name"));
        Assert.True(blank.GetProperty("isPublic").GetBoolean());
    }

    [Fact]
    public async Task RemoveCover_removes_the_picture()
    {
        var (owner, other) = (await api.SignUp(), await api.SignUp());
        var list = await CreateList(owner, description: "وصف", withCover: true);
        var cover = Text(await Detail(owner, list), "coverImageUrl");
        Assert.StartsWith($"https://files.test/reading-list-images/{list}/", cover);

        // Left out or false, the picture stays; someone else can't remove it.
        await Edited(owner, list, ReaderApi.Form(("RemoveCover", "false"), ("Description", "وصف آخر")));
        Assert.Equal(cover, Text(await Detail(owner, list), "coverImageUrl"));
        var notOwner = await (await Edit(other, list, ReaderApi.Form(("RemoveCover", "true")))).Error(HttpStatusCode.Forbidden);
        Assert.Equal("NotOwner", Text(notOwner, "code"));
        Assert.Equal(cover, Text(await Detail(owner, list), "coverImageUrl"));

        await Edited(owner, list, ReaderApi.Form(("RemoveCover", "true")));
        var removed = await Detail(owner, list);
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("coverImageUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await InMyLists(owner, list)).GetProperty("coverImageUrl").ValueKind);
        Assert.Equal("وصف آخر", Text(removed, "description"));

        // Removing it again is fine, and a new picture can be set afterwards.
        await Edited(owner, list, ReaderApi.Form(("removeCover", "true")));
        await Edited(owner, list, WithImage(ReaderApi.Form()));
        var replaced = Text(await Detail(owner, list), "coverImageUrl");
        Assert.StartsWith($"https://files.test/reading-list-images/{list}/", replaced);
        Assert.NotEqual(cover, replaced);
    }

    [Fact]
    public async Task A_new_picture_with_RemoveCover_is_refused_and_nothing_changes()
    {
        var owner = await api.SignUp();
        var list = await CreateList(owner, withCover: true);
        var before = await Detail(owner, list);

        var response = await Edit(owner, list, WithImage(ReaderApi.Form(("RemoveCover", "true"), ("Name", "اسم آخر " + Seed.Marker()))));

        var body = await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal("CoverConflict", Text(body, "code"));
        Assert.Matches(@"\p{IsArabic}", Text(body, "message")!);
        Assert.False(body.GetProperty("success").GetBoolean());
        var after = await Detail(owner, list);
        Assert.Equal(Text(before, "coverImageUrl"), Text(after, "coverImageUrl"));
        Assert.Equal(Text(before, "name"), Text(after, "name"));
    }

    [Fact]
    public async Task The_edit_is_validated_as_creating_a_list_is()
    {
        var owner = await api.SignUp();
        var list = await CreateList(owner, description: "وصف");
        var before = await Detail(owner, list);

        // The validator was bound to a type the endpoint doesn't bind: a long name or description was a 500 and any
        // file became the picture.
        MultipartFormDataContent[] refused =
        [
            ReaderApi.Form(("Name", new string('ن', 101))),
            ReaderApi.Form(("Description", new string('و', 1001))),
            WithImage(ReaderApi.Form(("Description", "وصف آخر")), "image/gif")
        ];
        foreach (var form in refused)
        {
            var body = await (await Edit(owner, list, form)).Error(HttpStatusCode.BadRequest);
            Assert.Equal(ValidationProblems.Code, Text(body, "code"));
            Assert.Matches(@"\p{IsArabic}", Text(body, "message")!);
        }

        var after = await Detail(owner, list);
        Assert.Equal(Text(before, "name"), Text(after, "name"));
        Assert.Equal("وصف", Text(after, "description"));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("coverImageUrl").ValueKind);

        // The limits themselves are accepted.
        var marker = Seed.Marker();
        var longName = new string('ن', 100 - marker.Length) + marker;
        await Edited(owner, list, ReaderApi.Form(("Name", longName), ("Description", new string('و', 1000))));
        var edited = await Detail(owner, list);
        Assert.Equal(longName, Text(edited, "name"));
        Assert.Equal(1000, Text(edited, "description")!.Length);
    }
}
