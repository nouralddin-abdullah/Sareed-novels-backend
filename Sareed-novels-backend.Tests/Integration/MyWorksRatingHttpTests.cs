using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>The works lists rate a novel with the fraction, like every other novel list (#25: they gave a whole number).</summary>
[Collection(ReaderApiCollection.Name)]
public class MyWorksRatingHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task My_works_a_members_works_and_one_work_rate_with_the_fraction()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        await using (var db = api.Db())
        {
            await db.Novels.Where(n => n.Id == novel.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.TotalAverageScore, 3.75m));
        }

        var mine = await (await api.Get("/api/myworks?pageSize=50", author)).OkJson();
        var theirs = await (await api.Get($"/api/myworks/user/{author.Id}")).OkJson();
        var one = await (await api.Get($"/api/myworks/{novel.Id}", author)).OkJson();

        foreach (var work in new[] { Assert.Single(mine.GetProperty("items").EnumerateArray()), Assert.Single(theirs.GetProperty("items").EnumerateArray()), one })
        {
            Assert.Equal(novel.Id, work.GetProperty("id").GetGuid());
            Assert.Equal(3.75m, work.GetProperty("totalAverageScore").GetDecimal());
        }
    }
}
