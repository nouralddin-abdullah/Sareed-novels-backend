using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Data fixes, no schema change:
    /// 1. Seeds the gift catalog. Production never had one, so every gift failed with "Gift not found"; the ids are the
    ///    ones the website already hard-codes, so its gift buttons start working as soon as this runs.
    /// 2. Points 19 novels at the cover their author actually uploaded last. Covers used to be stored under the title,
    ///    and changing a cover wrote a new file (after a rename, or when the title had stray spaces) without updating
    ///    the stored URL. Chosen from the public bucket on 2026-09-25 (newest file among the stored key and the
    ///    current-title key; every URL checked to load). Novels sharing a title were left alone.
    /// Both parts are idempotent and guarded, so they are no-ops on databases that don't have these rows.
    /// </summary>
    public partial class RepairNovelCoversAndSeedGifts : Migration
    {
        private static readonly (string Id, string Name, string ImageUrl, decimal Cost)[] Gifts =
        [
        ("ec16dfde-71b8-4e23-8ff5-d1846cdf2036", "Rose", "https://www.sardnovels.com/gifts/rose.png", 100m),
        ("88103b01-2e5b-4d06-9ff3-2724f4afba52", "Pizza", "https://www.sardnovels.com/gifts/pizza.png", 300m),
        ("9e17512a-269a-43e8-a571-1a1dc541cb5a", "Book", "https://www.sardnovels.com/gifts/book.png", 500m),
        ("48bdfb35-9f2c-4198-80c1-58f28eb648ef", "Crown", "https://www.sardnovels.com/gifts/crown.png", 1000m),
        ("e6bfb3e7-6273-4e6b-a577-6afa71055bce", "Scepter", "https://www.sardnovels.com/gifts/scepter.png", 1500m),
        ("a4005ee7-f2a5-488a-8757-574030513cd4", "Castle", "https://www.sardnovels.com/gifts/castle.png", 2000m),
        ("955f63a6-5f4e-4b10-8743-8ea11f544bae", "Dragon", "https://www.sardnovels.com/gifts/dragon.png", 5000m),
        ("50ca3576-e6ee-4708-8d7d-4e9ce82cf722", "Galaxy", "https://www.sardnovels.com/gifts/galaxy.png", 10000m),
        ];

        private static readonly (string NovelId, string OldUrl, string NewUrl)[] Covers =
        [
        // رواية مذكرات الكاتب الهارب
        ("f8e9bed2-7409-47b9-8a8a-bdd4e515e8f0", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/رواية مذكرات الكاتب الهارب ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%B1%D9%88%D8%A7%D9%8A%D8%A9%20%D9%85%D8%B0%D9%83%D8%B1%D8%A7%D8%AA%20%D8%A7%D9%84%D9%83%D8%A7%D8%AA%D8%A8%20%D8%A7%D9%84%D9%87%D8%A7%D8%B1%D8%A8"),
        // .. الياااا
        ("75f40261-2964-4e14-b0fa-d2c8d595528b", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/لعنة الأسماء : تحت المسمى", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/..%20%D8%A7%D9%84%D9%8A%D8%A7%D8%A7%D8%A7%D8%A7"),
        // أغنى رجل في دقيقة
        ("0c8978f6-0edf-43d5-b88f-c60b0d6e6028", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/أغنى رجل في دقيقة ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%A3%D8%BA%D9%86%D9%89%20%D8%B1%D8%AC%D9%84%20%D9%81%D9%8A%20%D8%AF%D9%82%D9%8A%D9%82%D8%A9"),
        // خريف ديكار
        ("d1e3f07b-68d1-4fb1-9b69-cd6f60e9b6bf", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/خريف ديكار ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%AE%D8%B1%D9%8A%D9%81%20%D8%AF%D9%8A%D9%83%D8%A7%D8%B1"),
        // الضائع من الموت
        ("e5e6c926-1593-4388-8067-5a8af0f983b0", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/الضائع من الموت ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%A7%D9%84%D8%B6%D8%A7%D8%A6%D8%B9%20%D9%85%D9%86%20%D8%A7%D9%84%D9%85%D9%88%D8%AA"),
        // ملحمة سفينة الفكر و حقيقة فلسطين 🇵🇸
        ("87106bfd-ebec-4d85-b0de-31a25a5f622d", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/سفينة الفكر ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D9%85%D9%84%D8%AD%D9%85%D8%A9%20%D8%B3%D9%81%D9%8A%D9%86%D8%A9%20%D8%A7%D9%84%D9%81%D9%83%D8%B1%20%D9%88%20%D8%AD%D9%82%D9%8A%D9%82%D8%A9%20%D9%81%D9%84%D8%B3%D8%B7%D9%8A%D9%86%20%F0%9F%87%B5%F0%9F%87%B8"),
        // ملحمة سفينة الفكر وحرية فلسطين 🇵🇸
        ("dd93bfd1-751e-4218-9120-55e88e6da384", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/ملحمة سفينة الفكر وحرية فلسطين 🇵🇸  ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D9%85%D9%84%D8%AD%D9%85%D8%A9%20%D8%B3%D9%81%D9%8A%D9%86%D8%A9%20%D8%A7%D9%84%D9%81%D9%83%D8%B1%20%D9%88%D8%AD%D8%B1%D9%8A%D8%A9%20%D9%81%D9%84%D8%B3%D8%B7%D9%8A%D9%86%20%F0%9F%87%B5%F0%9F%87%B8"),
        // ملحمة سفينة الفكر وحقيقة فلسطين
        ("c65a4a69-973f-40d9-8634-b4b61e0ab67c", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/ملحمة سفينة الفكر وحقيقة فلسطين ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D9%85%D9%84%D8%AD%D9%85%D8%A9%20%D8%B3%D9%81%D9%8A%D9%86%D8%A9%20%D8%A7%D9%84%D9%81%D9%83%D8%B1%20%D9%88%D8%AD%D9%82%D9%8A%D9%82%D8%A9%20%D9%81%D9%84%D8%B3%D8%B7%D9%8A%D9%86"),
        // رواية خارج ارادتى
        ("c634b160-9566-4b22-a05b-95f0e82b50d4", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/رواية خارج ارادتى ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%B1%D9%88%D8%A7%D9%8A%D8%A9%20%D8%AE%D8%A7%D8%B1%D8%AC%20%D8%A7%D8%B1%D8%A7%D8%AF%D8%AA%D9%89"),
        // رواية( سراع على  المبادئ )
        ("af754e31-33a9-44a9-ab74-63b3932fcec5", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/ثمن المبادئ ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%B1%D9%88%D8%A7%D9%8A%D8%A9%28%20%D8%B3%D8%B1%D8%A7%D8%B9%20%D8%B9%D9%84%D9%89%20%20%D8%A7%D9%84%D9%85%D8%A8%D8%A7%D8%AF%D8%A6%20%29"),
        // رواية ( ثمن المبادئ )
        ("c499be8c-a3cb-4c38-bc24-a98b17ec59e6", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/رواية (السقوط  فى الخفاء )", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%B1%D9%88%D8%A7%D9%8A%D8%A9%20%28%20%D8%AB%D9%85%D9%86%20%D8%A7%D9%84%D9%85%D8%A8%D8%A7%D8%AF%D8%A6%20%29"),
        // معلومه عن السماء
        ("a7ebd274-61ab-4454-9dbe-96cbfb835008", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/معلومه عن السماء ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D9%85%D8%B9%D9%84%D9%88%D9%85%D9%87%20%D8%B9%D9%86%20%D8%A7%D9%84%D8%B3%D9%85%D8%A7%D8%A1"),
        // نارين بين الحب والوجع
        ("678521bb-cefc-438b-9940-ef8a27726375", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/همس الاوتار (الوتر المفقود)", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D9%86%D8%A7%D8%B1%D9%8A%D9%86%20%D8%A8%D9%8A%D9%86%20%D8%A7%D9%84%D8%AD%D8%A8%20%D9%88%D8%A7%D9%84%D9%88%D8%AC%D8%B9"),
        // همس الأوتار
        ("a0e728a1-3f14-41b1-b35f-cbaf4a5793c9", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/همس الأوتار ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D9%87%D9%85%D8%B3%20%D8%A7%D9%84%D8%A3%D9%88%D8%AA%D8%A7%D8%B1"),
        // دارك:حكاية كيانين
        ("59eb6510-2297-4369-9f2a-1fedaf4defad", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/دارك:حكاية كيانين ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%AF%D8%A7%D8%B1%D9%83%3A%D8%AD%D9%83%D8%A7%D9%8A%D8%A9%20%D9%83%D9%8A%D8%A7%D9%86%D9%8A%D9%86"),
        // رسل التطهير
        ("934d56e9-b432-4d44-9e88-7d52704b7ccb", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/رسل التطهير ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%B1%D8%B3%D9%84%20%D8%A7%D9%84%D8%AA%D8%B7%D9%87%D9%8A%D8%B1"),
        // امراة فى الظلام
        ("845265f8-b34b-4dc4-9f9a-12b9deb4beab", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/امراة فى الظلام ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%A7%D9%85%D8%B1%D8%A7%D8%A9%20%D9%81%D9%89%20%D8%A7%D9%84%D8%B8%D9%84%D8%A7%D9%85"),
        // الجريمة
        ("8f4f8f8f-a6ba-45ce-92d1-8170b2136ba1", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/الجريمة ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%A7%D9%84%D8%AC%D8%B1%D9%8A%D9%85%D8%A9"),
        // الزائر الغريب
        ("33f570e8-cce3-4d69-8264-7e6dca75e5c6", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/الزائر الغريب ", "https://pub-2da766ccb48c485895ae36b58be35142.r2.dev/novel-images/%D8%A7%D9%84%D8%B2%D8%A7%D8%A6%D8%B1%20%D8%A7%D9%84%D8%BA%D8%B1%D9%8A%D8%A8"),
        ];

        private static string Sql(string value) => value.Replace("'", "''");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var gift in Gifts)
            {
                migrationBuilder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM Gifts WHERE Id = '{gift.Id}')
                        INSERT INTO Gifts (Id, Name, ImageUrl, Cost, IsActive, CreatedAt)
                        VALUES ('{gift.Id}', N'{Sql(gift.Name)}', N'{Sql(gift.ImageUrl)}', {gift.Cost.ToString(CultureInfo.InvariantCulture)}, 1, '2026-09-25T00:00:00');
                    """);
            }

            // Only rows still holding the old URL: a cover changed since then is left as the author set it.
            foreach (var cover in Covers)
            {
                migrationBuilder.Sql($"""
                    UPDATE Novels SET CoverImageUrl = N'{Sql(cover.NewUrl)}'
                    WHERE Id = '{cover.NovelId}' AND CoverImageUrl = N'{Sql(cover.OldUrl)}';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var cover in Covers)
            {
                migrationBuilder.Sql($"""
                    UPDATE Novels SET CoverImageUrl = N'{Sql(cover.OldUrl)}'
                    WHERE Id = '{cover.NovelId}' AND CoverImageUrl = N'{Sql(cover.NewUrl)}';
                    """);
            }

            // Gifts that were already sent stay: their transactions reference them.
            foreach (var gift in Gifts)
            {
                migrationBuilder.Sql($"""
                    DELETE FROM Gifts
                    WHERE Id = '{gift.Id}' AND NOT EXISTS (SELECT 1 FROM GiftTransactions WHERE GiftId = '{gift.Id}');
                    """);
            }
        }
    }
}
