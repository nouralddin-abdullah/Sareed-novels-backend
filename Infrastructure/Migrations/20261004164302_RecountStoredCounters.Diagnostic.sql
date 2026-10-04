-- Read-only diagnostic for the migration RecountStoredCounters (#66): what it will change, before it is deployed.
-- Only SELECT statements, each standing alone; nothing is written. Generated from the migration's own counter
-- definitions (Infrastructure/Migrations/20261004164302_RecountStoredCounters.cs, RecountStoredCounters.Counters)
-- and kept equal to them by a test: change the migration, not this file.
--
-- Each counter is compared with the recount the migration stores, row by row; a row "changes" exactly when the
-- migration's UPDATE touches it. The five Novels score columns are in points (two decimals); the rest are counts.
-- After the migration, statements 1 and 2 report nothing to change.


-- 1. Summary, one row per counter, in the order the migration recounts them.
--    RowsToChange: rows the migration updates (StoredTooHigh + StoredTooLow).
--    TotalAbsoluteDrift: the sum, over all rows, of |stored - recounted|.
SELECT 1 AS Step, N'Posts.CommentsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.CommentsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.CommentsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.CommentsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.CommentsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Posts t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.PostId = t.Id AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) r
UNION ALL
SELECT 2 AS Step, N'ChapterParagraphs.CommentsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.CommentsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.CommentsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.CommentsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.CommentsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM ChapterParagraphs t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.ParagraphId = t.Id AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) r
UNION ALL
SELECT 3 AS Step, N'Chapters.CommentsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.CommentsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.CommentsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.CommentsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.CommentsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Chapters t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.ChapterId = t.Id AND c.ParagraphId IS NULL AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) r
UNION ALL
SELECT 4 AS Step, N'Chapters.TotalCommentsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.TotalCommentsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.TotalCommentsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.TotalCommentsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.TotalCommentsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Chapters t
CROSS APPLY (SELECT (SELECT COUNT(*) FROM Comments c
                     WHERE c.ChapterId = t.Id AND c.ParagraphId IS NULL AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0)
                  + (SELECT COUNT(*) FROM ChapterParagraphs p JOIN Comments c ON c.ParagraphId = p.Id
                     WHERE p.ChapterId = t.Id AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) AS Value) r
UNION ALL
SELECT 5 AS Step, N'AspNetUsers.CommentsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.CommentsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.CommentsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.CommentsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.CommentsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM AspNetUsers t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.UserId = t.Id AND c.IsDeleted = 0) r
UNION ALL
SELECT 6 AS Step, N'Posts.LikesCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.LikesCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.LikesCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.LikesCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.LikesCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Posts t
CROSS APPLY (SELECT COUNT(*) AS Value FROM PostLikes l WHERE l.PostId = t.Id) r
UNION ALL
SELECT 7 AS Step, N'Comments.LikesCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.LikesCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.LikesCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.LikesCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.LikesCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Comments t
CROSS APPLY (SELECT COUNT(*) AS Value FROM CommentLikes l WHERE l.CommentId = t.Id) r
UNION ALL
SELECT 8 AS Step, N'Reviews.LikeCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.LikeCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.LikeCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.LikeCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.LikeCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Reviews t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ReviewLikes l WHERE l.ReviewId = t.Id) r
UNION ALL
SELECT 9 AS Step, N'AspNetUsers.ReviewsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.ReviewsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.ReviewsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.ReviewsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.ReviewsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM AspNetUsers t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Reviews v WHERE v.ReviewerId = t.Id) r
UNION ALL
SELECT 10 AS Step, N'Novels.ReviewCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.ReviewCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.ReviewCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.ReviewCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.ReviewCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
UNION ALL
SELECT 11 AS Step, N'Novels.AverageWritingQualityScore' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.AverageWritingQualityScore <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.AverageWritingQualityScore > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.AverageWritingQualityScore < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.AverageWritingQualityScore - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.WritingQualityScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
UNION ALL
SELECT 12 AS Step, N'Novels.AverageUpdatingStabilityScore' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.AverageUpdatingStabilityScore <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.AverageUpdatingStabilityScore > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.AverageUpdatingStabilityScore < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.AverageUpdatingStabilityScore - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.UpdatingStabilityScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
UNION ALL
SELECT 13 AS Step, N'Novels.AverageCharacterDevelopmentScore' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.AverageCharacterDevelopmentScore <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.AverageCharacterDevelopmentScore > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.AverageCharacterDevelopmentScore < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.AverageCharacterDevelopmentScore - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.CharacterDevelopmentScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
UNION ALL
SELECT 14 AS Step, N'Novels.AverageWorldBuildingScore' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.AverageWorldBuildingScore <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.AverageWorldBuildingScore > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.AverageWorldBuildingScore < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.AverageWorldBuildingScore - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.WorldBuildingScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
UNION ALL
SELECT 15 AS Step, N'Novels.TotalAverageScore' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.TotalAverageScore <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.TotalAverageScore > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.TotalAverageScore < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.TotalAverageScore - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT CAST((COALESCE(AVG(v.WritingQualityScore), 0.0) + COALESCE(AVG(v.UpdatingStabilityScore), 0.0)
                          + COALESCE(AVG(v.CharacterDevelopmentScore), 0.0) + COALESCE(AVG(v.WorldBuildingScore), 0.0))
                         / 4.0 AS decimal(3, 2)) AS Value
             FROM Reviews v WHERE v.NovelId = t.Id) r
UNION ALL
SELECT 16 AS Step, N'AspNetUsers.LibraryNovelsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.LibraryNovelsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.LibraryNovelsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.LibraryNovelsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.LibraryNovelsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM AspNetUsers t
CROSS APPLY (SELECT COUNT(*) AS Value FROM UserNovelProgress u WHERE u.UserId = t.Id) r
UNION ALL
SELECT 17 AS Step, N'ReadingLists.NovelsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.NovelsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.NovelsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.NovelsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.NovelsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM ReadingLists t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ReadingListNovels n WHERE n.ReadingListId = t.Id) r
UNION ALL
SELECT 18 AS Step, N'ReadingLists.FollowersCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.FollowersCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.FollowersCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.FollowersCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.FollowersCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM ReadingLists t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ReadingListFollowers f WHERE f.ReadingListId = t.Id) r
UNION ALL
SELECT 19 AS Step, N'Chapters.ParagraphsCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.ParagraphsCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.ParagraphsCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.ParagraphsCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.ParagraphsCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Chapters t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ChapterParagraphs p WHERE p.ChapterId = t.Id) r
UNION ALL
SELECT 20 AS Step, N'Novels.ChapterCount' AS Counter, COUNT(*) AS RowsChecked,
       COUNT(CASE WHEN t.ChapterCount <> r.Value THEN 1 END) AS RowsToChange,
       COUNT(CASE WHEN t.ChapterCount > r.Value THEN 1 END) AS StoredTooHigh,
       COUNT(CASE WHEN t.ChapterCount < r.Value THEN 1 END) AS StoredTooLow,
       CAST(ISNULL(SUM(ABS(t.ChapterCount - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
FROM Novels t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Chapters ch WHERE ch.NovelId = t.Id AND ch.Status = N'Published') r
ORDER BY Step;


-- 2. Every row the migration changes: the counter, the row's Id, its stored value and the recounted one it gets.
SELECT 1 AS Step, N'Posts.CommentsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.CommentsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Posts t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.PostId = t.Id AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) r
WHERE t.CommentsCount <> r.Value
UNION ALL
SELECT 2 AS Step, N'ChapterParagraphs.CommentsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.CommentsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM ChapterParagraphs t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.ParagraphId = t.Id AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) r
WHERE t.CommentsCount <> r.Value
UNION ALL
SELECT 3 AS Step, N'Chapters.CommentsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.CommentsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Chapters t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.ChapterId = t.Id AND c.ParagraphId IS NULL AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) r
WHERE t.CommentsCount <> r.Value
UNION ALL
SELECT 4 AS Step, N'Chapters.TotalCommentsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.TotalCommentsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Chapters t
CROSS APPLY (SELECT (SELECT COUNT(*) FROM Comments c
                     WHERE c.ChapterId = t.Id AND c.ParagraphId IS NULL AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0)
                  + (SELECT COUNT(*) FROM ChapterParagraphs p JOIN Comments c ON c.ParagraphId = p.Id
                     WHERE p.ChapterId = t.Id AND c.PostId IS NULL AND c.ParentCommentId IS NULL AND c.IsDeleted = 0) AS Value) r
WHERE t.TotalCommentsCount <> r.Value
UNION ALL
SELECT 5 AS Step, N'AspNetUsers.CommentsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.CommentsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM AspNetUsers t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Comments c
             WHERE c.UserId = t.Id AND c.IsDeleted = 0) r
WHERE t.CommentsCount <> r.Value
UNION ALL
SELECT 6 AS Step, N'Posts.LikesCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.LikesCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Posts t
CROSS APPLY (SELECT COUNT(*) AS Value FROM PostLikes l WHERE l.PostId = t.Id) r
WHERE t.LikesCount <> r.Value
UNION ALL
SELECT 7 AS Step, N'Comments.LikesCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.LikesCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Comments t
CROSS APPLY (SELECT COUNT(*) AS Value FROM CommentLikes l WHERE l.CommentId = t.Id) r
WHERE t.LikesCount <> r.Value
UNION ALL
SELECT 8 AS Step, N'Reviews.LikeCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.LikeCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Reviews t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ReviewLikes l WHERE l.ReviewId = t.Id) r
WHERE t.LikeCount <> r.Value
UNION ALL
SELECT 9 AS Step, N'AspNetUsers.ReviewsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.ReviewsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM AspNetUsers t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Reviews v WHERE v.ReviewerId = t.Id) r
WHERE t.ReviewsCount <> r.Value
UNION ALL
SELECT 10 AS Step, N'Novels.ReviewCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.ReviewCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
WHERE t.ReviewCount <> r.Value
UNION ALL
SELECT 11 AS Step, N'Novels.AverageWritingQualityScore' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.AverageWritingQualityScore AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.WritingQualityScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
WHERE t.AverageWritingQualityScore <> r.Value
UNION ALL
SELECT 12 AS Step, N'Novels.AverageUpdatingStabilityScore' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.AverageUpdatingStabilityScore AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.UpdatingStabilityScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
WHERE t.AverageUpdatingStabilityScore <> r.Value
UNION ALL
SELECT 13 AS Step, N'Novels.AverageCharacterDevelopmentScore' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.AverageCharacterDevelopmentScore AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.CharacterDevelopmentScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
WHERE t.AverageCharacterDevelopmentScore <> r.Value
UNION ALL
SELECT 14 AS Step, N'Novels.AverageWorldBuildingScore' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.AverageWorldBuildingScore AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT CAST(COALESCE(AVG(v.WorldBuildingScore), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v WHERE v.NovelId = t.Id) r
WHERE t.AverageWorldBuildingScore <> r.Value
UNION ALL
SELECT 15 AS Step, N'Novels.TotalAverageScore' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.TotalAverageScore AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT CAST((COALESCE(AVG(v.WritingQualityScore), 0.0) + COALESCE(AVG(v.UpdatingStabilityScore), 0.0)
                          + COALESCE(AVG(v.CharacterDevelopmentScore), 0.0) + COALESCE(AVG(v.WorldBuildingScore), 0.0))
                         / 4.0 AS decimal(3, 2)) AS Value
             FROM Reviews v WHERE v.NovelId = t.Id) r
WHERE t.TotalAverageScore <> r.Value
UNION ALL
SELECT 16 AS Step, N'AspNetUsers.LibraryNovelsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.LibraryNovelsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM AspNetUsers t
CROSS APPLY (SELECT COUNT(*) AS Value FROM UserNovelProgress u WHERE u.UserId = t.Id) r
WHERE t.LibraryNovelsCount <> r.Value
UNION ALL
SELECT 17 AS Step, N'ReadingLists.NovelsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.NovelsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM ReadingLists t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ReadingListNovels n WHERE n.ReadingListId = t.Id) r
WHERE t.NovelsCount <> r.Value
UNION ALL
SELECT 18 AS Step, N'ReadingLists.FollowersCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.FollowersCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM ReadingLists t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ReadingListFollowers f WHERE f.ReadingListId = t.Id) r
WHERE t.FollowersCount <> r.Value
UNION ALL
SELECT 19 AS Step, N'Chapters.ParagraphsCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.ParagraphsCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Chapters t
CROSS APPLY (SELECT COUNT(*) AS Value FROM ChapterParagraphs p WHERE p.ChapterId = t.Id) r
WHERE t.ParagraphsCount <> r.Value
UNION ALL
SELECT 20 AS Step, N'Novels.ChapterCount' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
       CAST(t.ChapterCount AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
FROM Novels t
CROSS APPLY (SELECT COUNT(*) AS Value FROM Chapters ch WHERE ch.NovelId = t.Id AND ch.Status = N'Published') r
WHERE t.ChapterCount <> r.Value
ORDER BY Step, RowId;


-- 3. What the comment counters assume: a comment is in one place, a post, a paragraph or a chapter, as the API
--    stores them. Expect no rows for "several places" or "no place". A comment in several places is counted
--    where SocialCounters counts it (its post, else its paragraph, else its chapter); one in no place only
--    for its author.
SELECT Place, COUNT(*) AS Comments, COUNT(CASE WHEN ParentCommentId IS NULL THEN 1 END) AS TopLevel,
       COUNT(CASE WHEN ParentCommentId IS NULL AND IsDeleted = 0 THEN 1 END) AS TopLevelNotDeleted
FROM (SELECT c.ParentCommentId, c.IsDeleted,
             CASE (CASE WHEN c.PostId IS NULL THEN 0 ELSE 1 END + CASE WHEN c.ParagraphId IS NULL THEN 0 ELSE 1 END
                   + CASE WHEN c.ChapterId IS NULL THEN 0 ELSE 1 END)
                 WHEN 0 THEN N'no place'
                 WHEN 1 THEN CASE WHEN c.PostId IS NOT NULL THEN N'post'
                                  WHEN c.ParagraphId IS NOT NULL THEN N'paragraph'
                                  ELSE N'chapter' END
                 ELSE N'several places' END AS Place
      FROM Comments c) x
GROUP BY Place
ORDER BY Place;
