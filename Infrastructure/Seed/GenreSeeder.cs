using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Seed;

public class GenreSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GenreSeeder> _logger;

    public GenreSeeder(ApplicationDbContext context, ILogger<GenreSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        try
        {
            // Check if genres already exist
            if (await _context.Genres.AnyAsync())
            {
                _logger.LogInformation("Genres already exist, skipping seeding");
                return;
            }

            var genres = new List<Genre>
            {
                new Genre
                {
                    Name = "Romance",
                    Slug = "romance",
                    Description = "تجتاحنا نفحات العشق حين تلتقي الأرواح في لحظة صمتٍ تتراقص فيها النجوم، حيث تنسج الأقدار خيوط الحنين وتعزف قصائد حب لا تُنسى.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Action",
                    Slug = "action",
                    Description = "هنا تُشعل الانفجارات سماء الأمل، ويتحول العرق إلى وقودٍ لصراعٍ محتدم، كل خطوةٍ تحمل المصير في كفة والموت في الأخرى.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Fantasy",
                    Slug = "fantasy",
                    Description = "تزهو القلاع العتيقة بألوانٍ من وحي الأحلام، حيث تتراقص المخلوقات الأسطورية على أنغام القوى الخفية وتكتب أساطير الأبطال.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Science Fiction",
                    Slug = "science-fiction",
                    Description = "تنفتح أمامنا أبواب المستقبل، فتلتقي التقنية بأحلام البشر، ونسافر بين كواكبٍ شاسعة بحثًا عن حقيقةٍ تفوق حدود الخيال.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Mystery",
                    Slug = "mystery",
                    Description = "تُسدل القصص ثوبها على ألغازٍ باردة، فتنبض الأدلة تحت ضوء القمر الصناعي، ويبدأ الأبطال مطاردة السر المختبئ في زوايا الليل.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Thriller",
                    Slug = "thriller",
                    Description = "تنبض القلوب في صمتٍ قاتل، ثم يتفجر التوتر في لحظةٍ واحدة، لتسقط كل الأقنعة وتنكشف أعمق الأسرار أمام عيونٍ مرهقة.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Drama",
                    Slug = "drama",
                    Description = "تختلط الدموع بالضحكات في مسرحٍ واحد، حيث يولد الألم من رحم الحب، ويتراقص الألم على أوتار القلب حتى آخر ممشى.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Comedy",
                    Slug = "comedy",
                    Description = "تتحول المواقف البسيطة إلى فصولٍ فكاهية، وتنساب القهقهات كالشلال وسط رتابة الأيام، لتنسى الهموم ولو للحظات.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Horror",
                    Slug = "horror",
                    Description = "يزحف الخوف بخطواتٍ باردة نحو أعماقنا، تنبض الظلال بأشكالٍ لا ترحم، ويصبح الصمت أدرأ من الصرخات.",
                    CreatedAt = DateTime.UtcNow
                },
                new Genre
                {
                    Name = "Adventure",
                    Slug = "adventure",
                    Description = "يتحدى الأبطال المجهول في صحراءٍ لا تعرف الرحمة، وكل وادٍ يخبئ حكايةً جديدة، وكل قمةٍ تُسجل شهادةً على جرأة القلوب.",
                    CreatedAt = DateTime.UtcNow
                }
            };

            await _context.Genres.AddRangeAsync(genres);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully seeded {Count} genres", genres.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding genres");
            throw;
        }
    }
}

