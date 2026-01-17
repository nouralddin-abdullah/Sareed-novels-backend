using Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<User>(options)
{
    internal DbSet<Novel> Novels { get; set; }
    internal DbSet<Follow> Follows { get; set; }
    internal DbSet<Review> Reviews { get; set; }
    internal DbSet<ReviewLike> ReviewLikes { get; set; }
    internal DbSet<Genre> Genres { get; set; }
    internal DbSet<NovelGenre> NovelGenres { get; set; }
    internal DbSet<NovelViews> NovelViews { get; set; }
    internal DbSet<RankingList> RankingLists { get; set; }
    internal DbSet<RankingEntry> RankingEntries { get; set; }
    internal DbSet<Chapter> Chapters { get; set; }
    internal DbSet<Comments> Comments { get; set; }
    internal DbSet<CommentLikes> CommentLikes { get; set; }
    internal DbSet<ChapterParagraph> ChapterParagraphs { get; set; }
    internal DbSet<ReadingList> ReadingLists { get; set; }
    internal DbSet<ReadingListNovel> ReadingListNovels { get; set; }
    internal DbSet<ReadingListFollower> ReadingListFollowers { get; set; }
    internal DbSet<UserNovelProgress> UserNovelProgress { get; set; }
    internal DbSet<SearchIndexOutbox> SearchIndexOutbox { get; set; }
    internal DbSet<Post> Posts { get; set; }
    internal DbSet<PostLike> PostLikes { get; set; }
    internal DbSet<NovelEntity> NovelEntities { get; set; }
    internal DbSet<EntityArticle> EntityArticles { get; set; }
    internal DbSet<EntityRelationship> EntityRelationships { get; set; }
    internal DbSet<EntityGalleryImage> EntityGalleryImages { get; set; }
    internal DbSet<Notification> Notifications { get; set; }
    
    // Wallet System
    internal DbSet<UserWallet> UserWallets { get; set; }
    internal DbSet<RechargeRequest> RechargeRequests { get; set; }
    internal DbSet<WithdrawalRequest> WithdrawalRequests { get; set; }
    internal DbSet<PointTransaction> PointTransactions { get; set; }
    
    // Gift System
    internal DbSet<Gift> Gifts { get; set; }
    internal DbSet<GiftTransaction> GiftTransactions { get; set; }
    internal DbSet<GlobalSupporterLeaderboard> GlobalSupporterLeaderboards { get; set; }
    
    // Privilege System
    internal DbSet<NovelPrivilege> NovelPrivileges { get; set; }
    internal DbSet<NovelPrivilegeSubscription> NovelPrivilegeSubscriptions { get; set; }
    
    // Competition System
    internal DbSet<Competition> Competitions { get; set; }
    internal DbSet<CompetitionParticipant> CompetitionParticipants { get; set; }
    internal DbSet<CompetitionWinner> CompetitionWinners { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Follow>(entity =>
        {
            entity.HasKey(f => new { f.FollowerId, f.FollowedId });

            entity.HasOne(f => f.Follower)
                  .WithMany(u => u.Following)
                  .HasForeignKey(f => f.FollowerId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(f => f.Followed)
                  .WithMany(u => u.Followers)
                  .HasForeignKey(f => f.FollowedId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(f => f.FollowerId);
            entity.HasIndex(f => f.FollowedId);
        });

        modelBuilder.Entity<Novel>().HasQueryFilter(n => !n.IsDeleted);
        modelBuilder.Entity<Novel>(entity =>
        {
            entity.HasKey(n => n.Id);

            entity.HasOne(n => n.Owner)
                  .WithMany(u => u.Novels)
                  .HasForeignKey(n => n.AuthorId);

            entity.HasIndex(n => n.AuthorId);

            entity.Property(n => n.AverageWritingQualityScore)
                  .HasPrecision(3, 2)
                  .HasDefaultValue(0);

            entity.Property(n => n.AverageUpdatingStabilityScore)
                  .HasPrecision(3, 2)
                  .HasDefaultValue(0);

            entity.Property(n => n.AverageCharacterDevelopmentScore)
                  .HasPrecision(3, 2)
                  .HasDefaultValue(0);

            entity.Property(n => n.AverageWorldBuildingScore)
                  .HasPrecision(3, 2)
                  .HasDefaultValue(0);

            entity.Property(n => n.TotalAverageScore)
                  .HasPrecision(3, 2)
                  .HasDefaultValue(0);

            entity.Property(n => n.ReviewCount)
                  .HasDefaultValue(0);

            // Add index for sorting by rating
            entity.HasIndex(n => n.TotalAverageScore);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.HasOne(r => r.ReviewOwner)
                  .WithMany(u => u.Reviews) 
                  .HasForeignKey(r => r.ReviewerId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.ReviewedNovel)
                  .WithMany(n => n.Reviews) 
                  .HasForeignKey(r => r.NovelId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(r => new { r.ReviewerId, r.NovelId })
                  .IsUnique()
                  .HasDatabaseName("IX_Reviews_ReviewerId_NovelId_Unique");

            entity.Property(r => r.WritingQualityScore)
                  .HasPrecision(3, 2);

            entity.Property(r => r.UpdatingStabilityScore)
                  .HasPrecision(3, 2);

            entity.Property(r => r.CharacterDevelopmentScore)
                  .HasPrecision(3, 2);

            entity.Property(r => r.WorldBuildingScore)
                  .HasPrecision(3, 2);

            entity.Property(r => r.TotalAverageScore)
                  .HasPrecision(3, 2);

            // Configure Content max length
            entity.Property(r => r.Content)
                  .HasMaxLength(2000);

            entity.Property(r => r.LikeCount)
                  .HasDefaultValue(0);

            // Add indexes for common queries
            entity.HasIndex(r => r.ReviewerId);
            entity.HasIndex(r => r.NovelId);
            entity.HasIndex(r => r.CreatedAt);
        });

        modelBuilder.Entity<ReviewLike>(entity =>
        {
            entity.HasKey(rl => rl.Id);

            entity.HasOne(rl => rl.User)
                  .WithMany(u => u.ReviewLikes)
                  .HasForeignKey(rl => rl.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rl => rl.Review)
                  .WithMany(r => r.Likes)
                  .HasForeignKey(rl => rl.ReviewId)
                  .OnDelete(DeleteBehavior.NoAction); 

            entity.HasIndex(rl => new { rl.UserId, rl.ReviewId })
                  .IsUnique()
                  .HasDatabaseName("IX_ReviewLikes_UserId_ReviewId_Unique");

            entity.HasIndex(rl => rl.UserId);
            entity.HasIndex(rl => rl.ReviewId);
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.HasKey(g => g.Id);

            entity.Property(g => g.Name)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(g => g.Slug)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(g => g.Description)
                  .HasMaxLength(500);

            entity.HasIndex(g => g.Slug)
                  .IsUnique();

            entity.HasIndex(g => g.Name)
                  .IsUnique();
        });

        modelBuilder.Entity<NovelGenre>(entity =>
        {
            // Composite primary key
            entity.HasKey(ng => new { ng.NovelId, ng.GenreId });

            // Configure relationships
            entity.HasOne(ng => ng.Novel)
                  .WithMany(n => n.NovelGenres)
                  .HasForeignKey(ng => ng.NovelId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ng => ng.Genre)
                  .WithMany(g => g.NovelGenres)
                  .HasForeignKey(ng => ng.GenreId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Configure ranking score fields
            entity.Property(ng => ng.GenreScore)
                  .HasPrecision(5, 2);

            entity.Property(ng => ng.QualityScore)
                  .HasPrecision(5, 2)
                  .HasDefaultValue(0);

            entity.Property(ng => ng.PopularityScore)
                  .HasPrecision(10, 2)
                  .HasDefaultValue(0);

            entity.Property(ng => ng.TrendingScore)
                  .HasPrecision(10, 2)
                  .HasDefaultValue(0);

            // Indexes for fast ranking queries
            entity.HasIndex(ng => new { ng.GenreId, ng.GenreRank });
            entity.HasIndex(ng => new { ng.GenreId, ng.QualityScore });
            entity.HasIndex(ng => new { ng.GenreId, ng.TrendingScore });
        });

        // ADD THIS CONFIGURATION for NovelViews
        modelBuilder.Entity<NovelViews>(entity =>
        {
            entity.HasKey(nv => nv.Id);

            entity.HasOne(nv => nv.Novel)
                  .WithMany()
                  .HasForeignKey(nv => nv.NovelId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Unique constraint: one record per novel per day
            entity.HasIndex(nv => new { nv.NovelId, nv.ViewDate })
                  .IsUnique();

            // Index for date-based queries
            entity.HasIndex(nv => nv.ViewDate);
        });

        modelBuilder.Entity<RankingList>(entity =>
        {
            entity.HasKey(rl => rl.Id);

            entity.Property(rl => rl.Name)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(rl => rl.RankingType)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.HasOne(rl => rl.Genre)
                  .WithMany()
                  .HasForeignKey(rl => rl.GenreId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Unique constraint: one ranking list per genre per type
            entity.HasIndex(rl => new { rl.GenreId, rl.RankingType })
                  .IsUnique();
        });

        modelBuilder.Entity<RankingEntry>(entity =>
        {
            entity.HasKey(re => re.Id);

            entity.HasOne(re => re.RankingList)
                  .WithMany(rl => rl.Entries)
                  .HasForeignKey(re => re.RankingListId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(re => re.Novel)
                  .WithMany()
                  .HasForeignKey(re => re.NovelId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(re => re.Score)
                  .HasPrecision(10, 2);

            entity.Property(re => re.QualityScore)
                  .HasPrecision(5, 2);

            entity.Property(re => re.PopularityScore)
                  .HasPrecision(10, 2);

            entity.Property(re => re.TrendingScore)
                  .HasPrecision(10, 2);

            // Indexes for fast ranking queries
            entity.HasIndex(re => new { re.RankingListId, re.Rank });
            entity.HasIndex(re => re.NovelId);
        });

        modelBuilder.Entity<Chapter>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Title)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(c => c.Content); // Made nullable in entity

            entity.Property(c => c.Status)
                  .IsRequired()
                  .HasMaxLength(20)
                  .HasDefaultValue("Draft");

            entity.Property(c => c.ChapterIndex)
                  .IsRequired();

            entity.Property(c => c.CreatedAt)
                  .IsRequired();

            entity.Property(c => c.CommentsCount)
                  .HasDefaultValue(0);
            
            entity.Property(c => c.TotalCommentsCount)
                  .HasDefaultValue(0);
            
            entity.Property(c => c.ParagraphsCount)
                  .HasDefaultValue(0);

            entity.HasOne(c => c.Novel)
                  .WithMany(n => n.Chapters)
                  .HasForeignKey(c => c.NovelId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => new { c.NovelId, c.ChapterIndex })
                  .HasDatabaseName("IX_Chapters_Novel_Index");

            entity.HasIndex(c => new { c.NovelId, c.Status, c.ChapterIndex })
                  .HasDatabaseName("IX_Chapters_Novel_Status_Index");

            entity.HasIndex(c => new { c.NovelId, c.Status })
                  .HasDatabaseName("IX_Chapters_Novel_Status");

            entity.HasIndex(c => c.CreatedAt)
                  .HasDatabaseName("IX_Chapters_CreatedAt");
        });

        // NEW: ChapterParagraph configuration
        modelBuilder.Entity<ChapterParagraph>(entity =>
        {
            entity.HasKey(cp => cp.Id);
            
            entity.Property(cp => cp.Content)
                  .IsRequired();
            
            entity.Property(cp => cp.ContentHash)
                  .IsRequired()
                  .HasMaxLength(64); // SHA256 hash in base64 is 44 chars, 64 for safety
            
            entity.Property(cp => cp.OrderIndex)
                  .IsRequired();
            
            entity.Property(cp => cp.ContentType)
                  .HasMaxLength(20)
                  .HasDefaultValue("text");
            
            entity.Property(cp => cp.CommentsCount)
                  .HasDefaultValue(0);
            
            entity.HasOne(cp => cp.Chapter)
                  .WithMany(c => c.Paragraphs)
                  .HasForeignKey(cp => cp.ChapterId)
                  .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasIndex(cp => cp.ChapterId)
                  .HasDatabaseName("IX_ChapterParagraphs_ChapterId");
            
            entity.HasIndex(cp => new { cp.ChapterId, cp.OrderIndex })
                  .HasDatabaseName("IX_ChapterParagraphs_Chapter_Order");
            
            // NEW: Index on ContentHash for fast lookups
            entity.HasIndex(cp => cp.ContentHash)
                  .HasDatabaseName("IX_ChapterParagraphs_ContentHash");
        });

        modelBuilder.Entity<Comments>(entity =>
        {
            entity.HasKey(c => c.Id);
            
            entity.Property(c => c.Content)
                .IsRequired()
                .HasMaxLength(2000);
            
            entity.Property(c => c.AttachedImageUrl)
                .HasMaxLength(500);
            
            entity.HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(c => c.User)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(c => c.Chapter)
                .WithMany()
                .HasForeignKey(c => c.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(c => c.Paragraph)
                .WithMany()
                .HasForeignKey(c => c.ParagraphId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(c => c.Post)
                .WithMany(p => p.Comments)
                .HasForeignKey(c => c.PostId)
                .OnDelete(DeleteBehavior.NoAction);
            
            entity.Property(c => c.LikesCount)
                .HasDefaultValue(0);
            
            entity.HasIndex(c => c.ChapterId)
                .HasDatabaseName("IX_Comments_ChapterId");
            
            entity.HasIndex(c => c.ParentCommentId)
                .HasDatabaseName("IX_Comments_ParentCommentId");
            
            entity.HasIndex(c => c.UserId)
                .HasDatabaseName("IX_Comments_UserId");
            
            entity.HasIndex(c => c.CreatedAt)
                .HasDatabaseName("IX_Comments_CreatedAt");
            
            entity.HasIndex(c => new { c.ChapterId, c.ParentCommentId })
                .HasDatabaseName("IX_Comments_Chapter_Parent");
            
            entity.HasIndex(c => c.ParagraphId)
                .HasDatabaseName("IX_Comments_ParagraphId");
            
            entity.HasIndex(c => new { c.ParagraphId, c.ParentCommentId })
                .HasDatabaseName("IX_Comments_Paragraph_Parent");
            
            entity.HasIndex(c => c.PostId)
                .HasDatabaseName("IX_Comments_PostId");
            
            entity.HasIndex(c => new { c.PostId, c.ParentCommentId })
                .HasDatabaseName("IX_Comments_Post_Parent");
            
            entity.HasQueryFilter(c => !c.IsDeleted);
        });

        // CommentLikes configuration
        modelBuilder.Entity<CommentLikes>(entity =>
        {
            entity.HasKey(cl => cl.Id);

            // User relationship
            entity.HasOne(cl => cl.User)
                .WithMany(u => u.CommentLikes)
                .HasForeignKey(cl => cl.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Comment relationship
            entity.HasOne(cl => cl.Comment)
                .WithMany(c => c.Likes)
                .HasForeignKey(cl => cl.CommentId)
                .OnDelete(DeleteBehavior.NoAction); // Prevent cascade conflicts

            // Unique constraint: one like per user per comment
            entity.HasIndex(cl => new { cl.UserId, cl.CommentId })
                .IsUnique()
                .HasDatabaseName("IX_CommentLikes_UserId_CommentId_Unique");

            // Performance indexes
            entity.HasIndex(cl => cl.UserId)
                .HasDatabaseName("IX_CommentLikes_UserId");

            entity.HasIndex(cl => cl.CommentId)
                .HasDatabaseName("IX_CommentLikes_CommentId");
        });
        // ReadingList configuration
        modelBuilder.Entity<ReadingList>(entity =>
        {
            entity.HasKey(rl => rl.Id);

            entity.Property(rl => rl.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(rl => rl.Description)
                .HasMaxLength(1000);

            entity.Property(rl => rl.CoverImageUrl)
                .HasMaxLength(500);

            entity.Property(rl => rl.IsPublic)
                .HasDefaultValue(false);

            entity.Property(rl => rl.NovelsCount)
                .HasDefaultValue(0);

            entity.Property(rl => rl.FollowersCount)
                .HasDefaultValue(0);

            entity.HasOne(rl => rl.Owner)
                .WithMany(u => u.ReadingLists)
                .HasForeignKey(rl => rl.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(rl => rl.UserId)
                .HasDatabaseName("IX_ReadingLists_UserId");

            entity.HasIndex(rl => new { rl.UserId, rl.Name })
                .HasDatabaseName("IX_ReadingLists_UserId_Name");

            entity.HasIndex(rl => rl.IsPublic)
                .HasDatabaseName("IX_ReadingLists_IsPublic");

            entity.HasIndex(rl => new { rl.IsPublic, rl.FollowersCount })
                .HasDatabaseName("IX_ReadingLists_Public_Followers");
        });

        // ReadingListNovel configuration (many-to-many)
        modelBuilder.Entity<ReadingListNovel>(entity =>
        {
            entity.HasKey(rln => new { rln.ReadingListId, rln.NovelId });

            entity.HasOne(rln => rln.ReadingList)
                .WithMany(rl => rl.Novels)
                .HasForeignKey(rln => rln.ReadingListId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rln => rln.Novel)
                .WithMany(n => n.ReadingListNovels)
                .HasForeignKey(rln => rln.NovelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Property(rln => rln.OrderIndex)
                .HasDefaultValue(0);

            entity.HasIndex(rln => new { rln.ReadingListId, rln.OrderIndex })
                .HasDatabaseName("IX_ReadingListNovels_List_Order");
        });

        // ReadingListFollower configuration (many-to-many)
        modelBuilder.Entity<ReadingListFollower>(entity =>
        {
            entity.HasKey(rlf => new { rlf.ReadingListId, rlf.UserId });

            entity.HasOne(rlf => rlf.ReadingList)
                .WithMany(rl => rl.Followers)
                .HasForeignKey(rlf => rlf.ReadingListId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rlf => rlf.User)
                .WithMany(u => u.FollowedReadingLists)
                .HasForeignKey(rlf => rlf.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(rlf => rlf.UserId)
                .HasDatabaseName("IX_ReadingListFollowers_UserId");

            entity.HasIndex(rlf => rlf.ReadingListId)
                .HasDatabaseName("IX_ReadingListFollowers_ReadingListId");
        });

        modelBuilder.Entity<UserNovelProgress>(entity =>
        {
            entity.HasKey(unp => new { unp.UserId, unp.NovelId });

            entity.HasOne(unp => unp.User)
                .WithMany()
                .HasForeignKey(unp => unp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(unp => unp.Novel)
                .WithMany()
                .HasForeignKey(unp => unp.NovelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(unp => unp.LastReadChapter)
                .WithMany()
                .HasForeignKey(unp => unp.LastReadChapterId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(unp => unp.UserId)
                .HasDatabaseName("IX_UserNovelProgress_UserId");

            entity.HasIndex(unp => unp.LastReadAt)
                .HasDatabaseName("IX_UserNovelProgress_LastReadAt");

            entity.HasIndex(unp => new { unp.UserId, unp.LastReadAt })
                .HasDatabaseName("IX_UserNovelProgress_User_LastRead");
        });

        modelBuilder.Entity<SearchIndexOutbox>(entity =>
        {
            entity.HasKey(sio => sio.Id);

            entity.Property(sio => sio.EntityType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(sio => sio.Action)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(sio => sio.Processed)
                .HasDefaultValue(false);

            entity.Property(sio => sio.RetryCount)
                .HasDefaultValue(0);

            entity.Property(sio => sio.ErrorMessage)
                .HasMaxLength(2000);

            entity.HasIndex(sio => new { sio.Processed, sio.CreatedAt })
                .HasDatabaseName("IX_SearchIndexOutbox_Processed_CreatedAt");

            entity.HasIndex(sio => new { sio.Processed, sio.RetryCount })
                .HasDatabaseName("IX_SearchIndexOutbox_Processed_RetryCount");

            entity.HasIndex(sio => new { sio.EntityType, sio.EntityId })
                .HasDatabaseName("IX_SearchIndexOutbox_Entity");
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Content)
                .IsRequired()
                .HasMaxLength(5000);

            entity.Property(p => p.ImageUrl)
                .HasMaxLength(500);

            entity.Property(p => p.LikesCount)
                .HasDefaultValue(0);

            entity.Property(p => p.CommentsCount)
                .HasDefaultValue(0);

            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Novel)
                .WithMany()
                .HasForeignKey(p => p.NovelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(p => p.UserId)
                .HasDatabaseName("IX_Posts_UserId");

            entity.HasIndex(p => p.NovelId)
                .HasDatabaseName("IX_Posts_NovelId");

            entity.HasIndex(p => p.CreatedAt)
                .HasDatabaseName("IX_Posts_CreatedAt");

            entity.HasIndex(p => new { p.UserId, p.CreatedAt })
                .HasDatabaseName("IX_Posts_User_Created");

            entity.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<PostLike>(entity =>
        {
            entity.HasKey(pl => pl.Id);

            entity.HasOne(pl => pl.User)
                .WithMany()
                .HasForeignKey(pl => pl.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pl => pl.Post)
                .WithMany(p => p.Likes)
                .HasForeignKey(pl => pl.PostId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(pl => new { pl.UserId, pl.PostId })
                .IsUnique()
                .HasDatabaseName("IX_PostLikes_UserId_PostId_Unique");

            entity.HasIndex(pl => pl.UserId)
                .HasDatabaseName("IX_PostLikes_UserId");

            entity.HasIndex(pl => pl.PostId)
                .HasDatabaseName("IX_PostLikes_PostId");
        });

        // NovelEntity configuration
        modelBuilder.Entity<NovelEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Section)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Icon)
                .HasMaxLength(20);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.ShortDescription)
                .HasMaxLength(500);

            entity.Property(e => e.Description)
                .HasMaxLength(5000);

            entity.Property(e => e.Role)
                .HasMaxLength(100);

            entity.Property(e => e.ImageUrl)
                .HasMaxLength(500);

            entity.Property(e => e.AttributesJson)
                .IsRequired()
                .HasDefaultValue("{}");

            entity.HasOne(e => e.Novel)
                .WithMany()
                .HasForeignKey(e => e.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.NovelId)
                .HasDatabaseName("IX_NovelEntities_NovelId");

            entity.HasIndex(e => e.Section)
                .HasDatabaseName("IX_NovelEntities_Section");

            entity.HasIndex(e => new { e.NovelId, e.Section })
                .HasDatabaseName("IX_NovelEntities_Novel_Section");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_NovelEntities_CreatedAt");

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // EntityGalleryImage configuration
        modelBuilder.Entity<EntityGalleryImage>(entity =>
        {
            entity.HasKey(egi => egi.Id);

            entity.Property(egi => egi.ImageUrl)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(egi => egi.Caption)
                .HasMaxLength(500);

            entity.Property(egi => egi.OrderIndex)
                .HasDefaultValue(0);

            entity.HasOne(egi => egi.Entity)
                .WithMany(e => e.GalleryImages)
                .HasForeignKey(egi => egi.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(egi => egi.EntityId)
                .HasDatabaseName("IX_EntityGalleryImages_EntityId");

            entity.HasIndex(egi => new { egi.EntityId, egi.OrderIndex })
                .HasDatabaseName("IX_EntityGalleryImages_Entity_Order");
        });

        // EntityArticle configuration
        modelBuilder.Entity<EntityArticle>(entity =>
        {
            entity.HasKey(ea => ea.Id);

            entity.Property(ea => ea.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(ea => ea.Content)
                .IsRequired();

            entity.Property(ea => ea.OrderIndex)
                .HasDefaultValue(0);

            entity.HasOne(ea => ea.Entity)
                .WithMany(e => e.Articles)
                .HasForeignKey(ea => ea.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ea => ea.EntityId)
                .HasDatabaseName("IX_EntityArticles_EntityId");

            entity.HasIndex(ea => new { ea.EntityId, ea.OrderIndex })
                .HasDatabaseName("IX_EntityArticles_Entity_Order");

            entity.HasQueryFilter(ea => !ea.IsDeleted);
        });

        // EntityRelationship configuration
        modelBuilder.Entity<EntityRelationship>(entity =>
        {
            entity.HasKey(er => er.Id);

            entity.Property(er => er.RelationType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(er => er.Label)
                .HasMaxLength(100);

            entity.Property(er => er.Description)
                .HasMaxLength(1000);

            entity.HasOne(er => er.SourceEntity)
                .WithMany(e => e.SourceRelationships)
                .HasForeignKey(er => er.SourceEntityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(er => er.TargetEntity)
                .WithMany(e => e.TargetRelationships)
                .HasForeignKey(er => er.TargetEntityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(er => er.SourceEntityId)
                .HasDatabaseName("IX_EntityRelationships_SourceId");

            entity.HasIndex(er => er.TargetEntityId)
                .HasDatabaseName("IX_EntityRelationships_TargetId");

            entity.HasIndex(er => new { er.SourceEntityId, er.TargetEntityId })
                .HasDatabaseName("IX_EntityRelationships_Source_Target");

            entity.HasQueryFilter(er => !er.IsDeleted);
        });

        // Notification configuration
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);

            entity.Property(n => n.Type)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(n => n.ActorDisplayName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(n => n.ActorProfilePhoto)
                .HasMaxLength(500);

            entity.Property(n => n.Message)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(n => n.ActionUrl)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(n => n.IsRead)
                .HasDefaultValue(false);

            entity.Property(n => n.RelatedEntityType)
                .HasMaxLength(50);

            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(n => n.UserId)
                .HasDatabaseName("IX_Notifications_UserId");

            entity.HasIndex(n => new { n.UserId, n.IsRead })
                .HasDatabaseName("IX_Notifications_UserId_IsRead");

            entity.HasIndex(n => new { n.UserId, n.CreatedAt })
                .HasDatabaseName("IX_Notifications_UserId_CreatedAt");

            entity.HasIndex(n => n.Type)
                .HasDatabaseName("IX_Notifications_Type");

            entity.HasIndex(n => n.ActorId)
                .HasDatabaseName("IX_Notifications_ActorId");
        });

        // UserWallet configuration
        modelBuilder.Entity<UserWallet>(entity =>
        {
            entity.HasKey(uw => uw.Id);

            entity.HasOne(uw => uw.User)
                .WithMany()
                .HasForeignKey(uw => uw.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(uw => uw.UserId)
                .IsUnique()
                .HasDatabaseName("IX_UserWallets_UserId_Unique");

            entity.Property(uw => uw.CurrentBalance)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(uw => uw.TotalRecharged)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(uw => uw.TotalWithdrawn)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(uw => uw.TotalSpent)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(uw => uw.TotalEarned)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);
        });

        // RechargeRequest configuration
        modelBuilder.Entity<RechargeRequest>(entity =>
        {
            entity.HasKey(rr => rr.Id);

            entity.HasOne(rr => rr.User)
                .WithMany()
                .HasForeignKey(rr => rr.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rr => rr.ProcessedByUser)
                .WithMany()
                .HasForeignKey(rr => rr.ProcessedBy)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Property(rr => rr.PaymentMethod)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(rr => rr.PaymentProofUrl)
                .HasMaxLength(500);

            entity.Property(rr => rr.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("Pending");

            entity.Property(rr => rr.RejectionReason)
                .HasMaxLength(500);

            entity.Property(rr => rr.BaseAmountEGP)
                .HasPrecision(18, 2);

            entity.Property(rr => rr.TransactionFee)
                .HasPrecision(18, 2);

            entity.Property(rr => rr.TotalAmountEGP)
                .HasPrecision(18, 2);

            entity.HasIndex(rr => new { rr.UserId, rr.Status })
                .HasDatabaseName("IX_RechargeRequests_User_Status");

            entity.HasIndex(rr => new { rr.Status, rr.RequestedAt })
                .HasDatabaseName("IX_RechargeRequests_Status_Requested");
        });

        // WithdrawalRequest configuration
        modelBuilder.Entity<WithdrawalRequest>(entity =>
        {
            entity.HasKey(wr => wr.Id);

            entity.HasOne(wr => wr.User)
                .WithMany()
                .HasForeignKey(wr => wr.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(wr => wr.ProcessedByUser)
                .WithMany()
                .HasForeignKey(wr => wr.ProcessedBy)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Property(wr => wr.WithdrawalMethod)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(wr => wr.PaymentDetails)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(wr => wr.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("Pending");

            entity.Property(wr => wr.RejectionReason)
                .HasMaxLength(500);

            entity.Property(wr => wr.BaseAmountEGP)
                .HasPrecision(18, 2);

            entity.Property(wr => wr.TaxDeducted)
                .HasPrecision(18, 2);

            entity.Property(wr => wr.NetAmountEGP)
                .HasPrecision(18, 2);

            entity.HasIndex(wr => new { wr.UserId, wr.Status })
                .HasDatabaseName("IX_WithdrawalRequests_User_Status");

            entity.HasIndex(wr => new { wr.Status, wr.RequestedAt })
                .HasDatabaseName("IX_WithdrawalRequests_Status_Requested");
        });

        // PointTransaction configuration
        modelBuilder.Entity<PointTransaction>(entity =>
        {
            entity.HasKey(pt => pt.Id);

            entity.HasOne(pt => pt.User)
                .WithMany()
                .HasForeignKey(pt => pt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(pt => pt.Type)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(pt => pt.Description)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(pt => pt.Amount)
                .HasPrecision(18, 2);

            entity.Property(pt => pt.BalanceBefore)
                .HasPrecision(18, 2);

            entity.Property(pt => pt.BalanceAfter)
                .HasPrecision(18, 2);

            entity.HasIndex(pt => new { pt.UserId, pt.CreatedAt })
                .HasDatabaseName("IX_PointTransactions_User_Created");

            entity.HasIndex(pt => new { pt.Type, pt.CreatedAt })
                .HasDatabaseName("IX_PointTransactions_Type_Created");
        });

        // Gift configuration
        modelBuilder.Entity<Gift>(entity =>
        {
            entity.HasKey(g => g.Id);

            entity.Property(g => g.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(g => g.ImageUrl)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(g => g.Cost)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(g => g.IsActive)
                .HasDefaultValue(true);

            entity.HasIndex(g => g.IsActive)
                .HasDatabaseName("IX_Gifts_IsActive");

            entity.HasIndex(g => g.Cost)
                .HasDatabaseName("IX_Gifts_Cost");
        });

        // GiftTransaction configuration
        modelBuilder.Entity<GiftTransaction>(entity =>
        {
            entity.HasKey(gt => gt.Id);

            entity.HasOne(gt => gt.Gift)
                .WithMany(g => g.Transactions)
                .HasForeignKey(gt => gt.GiftId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(gt => gt.Novel)
                .WithMany()
                .HasForeignKey(gt => gt.NovelId)
                .OnDelete(DeleteBehavior.NoAction);  // Changed from Cascade to NoAction to avoid cascade path conflict

            entity.HasOne(gt => gt.Sender)
                .WithMany()
                .HasForeignKey(gt => gt.SenderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(gt => gt.TotalCost)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(gt => gt.Count)
                .HasDefaultValue(1);

            entity.HasIndex(gt => new { gt.NovelId, gt.CreatedAt })
                .HasDatabaseName("IX_GiftTransactions_Novel_Created");

            entity.HasIndex(gt => new { gt.SenderId, gt.CreatedAt })
                .HasDatabaseName("IX_GiftTransactions_Sender_Created");

            entity.HasIndex(gt => gt.CreatedAt)
                .HasDatabaseName("IX_GiftTransactions_CreatedAt");
        });

        // GlobalSupporterLeaderboard configuration
        modelBuilder.Entity<GlobalSupporterLeaderboard>(entity =>
        {
            entity.HasKey(gsl => gsl.Id);

            entity.HasOne(gsl => gsl.User)
                .WithMany()
                .HasForeignKey(gsl => gsl.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(gsl => gsl.TotalPointsGifted)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(gsl => gsl.TotalGiftsCount)
                .HasDefaultValue(0);

            entity.Property(gsl => gsl.Rank)
                .IsRequired();

            entity.Property(gsl => gsl.Period)
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(gsl => new { gsl.Period, gsl.Rank })
                .HasDatabaseName("IX_GlobalSupporterLeaderboard_Period_Rank");

            entity.HasIndex(gsl => new { gsl.UserId, gsl.Period })
                .HasDatabaseName("IX_GlobalSupporterLeaderboard_User_Period");
        });
        
        // NovelPrivilege configuration
        modelBuilder.Entity<NovelPrivilege>(entity =>
        {
            entity.HasKey(np => np.Id);

            entity.HasOne(np => np.Novel)
                .WithMany()
                .HasForeignKey(np => np.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(np => np.SubscriptionCost)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(np => np.IsEnabled)
                .HasDefaultValue(false);

            entity.Property(np => np.MaxLockedChapters)
                .HasDefaultValue(20);

            entity.Property(np => np.CurrentLockedCount)
                .HasDefaultValue(0);

            entity.Property(np => np.MinPublishedRequired)
                .HasDefaultValue(11);

            entity.Property(np => np.TotalDailyUnlocksPerformed)
                .HasDefaultValue(0);

            // Unique constraint: one privilege config per novel
            entity.HasIndex(np => np.NovelId)
                .IsUnique()
                .HasDatabaseName("IX_NovelPrivileges_NovelId_Unique");

            entity.HasIndex(np => np.IsEnabled)
                .HasDatabaseName("IX_NovelPrivileges_IsEnabled");

            entity.HasIndex(np => new { np.IsEnabled, np.CurrentLockedCount })
                .HasDatabaseName("IX_NovelPrivileges_Enabled_LockedCount");
        });

        // NovelPrivilegeSubscription configuration
        modelBuilder.Entity<NovelPrivilegeSubscription>(entity =>
        {
            entity.HasKey(nps => nps.Id);

            entity.HasOne(nps => nps.Novel)
                .WithMany()
                .HasForeignKey(nps => nps.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(nps => nps.User)
                .WithMany()
                .HasForeignKey(nps => nps.UserId)
                .OnDelete(DeleteBehavior.NoAction); // Avoid cascade path conflict

            entity.Property(nps => nps.AmountPaid)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(nps => nps.IsActive)
                .HasDefaultValue(true);

            entity.HasIndex(nps => new { nps.NovelId, nps.UserId })
                .HasDatabaseName("IX_NovelPrivilegeSubscriptions_Novel_User");

            entity.HasIndex(nps => new { nps.UserId, nps.IsActive })
                .HasDatabaseName("IX_NovelPrivilegeSubscriptions_User_Active");

            entity.HasIndex(nps => nps.IsActive)
                .HasDatabaseName("IX_NovelPrivilegeSubscriptions_Active");

            entity.HasIndex(nps => nps.SubscribedAt)
                .HasDatabaseName("IX_NovelPrivilegeSubscriptions_SubscribedAt");
        });

        // Competition configuration
        modelBuilder.Entity<Competition>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(c => c.Slug)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(c => c.ImageUrl)
                .HasMaxLength(500);

            entity.Property(c => c.TotalPrize)
                .HasPrecision(18, 2);

            entity.Property(c => c.PrizeFirstPlace)
                .HasPrecision(18, 2);

            entity.Property(c => c.PrizeSecondPlace)
                .HasPrecision(18, 2);

            entity.Property(c => c.PrizeThirdPlace)
                .HasPrecision(18, 2);

            entity.Property(c => c.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(CompetitionStatus.Upcoming);

            entity.Property(c => c.MinChapters)
                .HasDefaultValue(5);

            entity.Property(c => c.IsActive)
                .HasDefaultValue(true);

            entity.HasIndex(c => c.Slug)
                .IsUnique()
                .HasDatabaseName("IX_Competitions_Slug_Unique");

            entity.HasIndex(c => c.Status)
                .HasDatabaseName("IX_Competitions_Status");

            entity.HasIndex(c => new { c.IsActive, c.Status })
                .HasDatabaseName("IX_Competitions_Active_Status");

            entity.HasIndex(c => c.ParticipationStartDate)
                .HasDatabaseName("IX_Competitions_ParticipationStart");
        });

        // CompetitionParticipant configuration
        modelBuilder.Entity<CompetitionParticipant>(entity =>
        {
            entity.HasKey(cp => cp.Id);

            entity.HasOne(cp => cp.Competition)
                .WithMany(c => c.Participants)
                .HasForeignKey(cp => cp.CompetitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(cp => cp.Novel)
                .WithMany()
                .HasForeignKey(cp => cp.NovelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Property(cp => cp.CurrentPoints)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(cp => cp.ExtraPoints)
                .HasPrecision(18, 2)
                .HasDefaultValue(0);

            entity.Property(cp => cp.ViewsAtJoin)
                .HasDefaultValue(0);

            entity.Property(cp => cp.CurrentRank)
                .HasDefaultValue(0);

            entity.HasIndex(cp => new { cp.CompetitionId, cp.NovelId })
                .IsUnique()
                .HasDatabaseName("IX_CompetitionParticipants_Competition_Novel_Unique");

            entity.HasIndex(cp => cp.CompetitionId)
                .HasDatabaseName("IX_CompetitionParticipants_CompetitionId");

            entity.HasIndex(cp => cp.NovelId)
                .HasDatabaseName("IX_CompetitionParticipants_NovelId");

            entity.HasIndex(cp => new { cp.CompetitionId, cp.CurrentPoints })
                .HasDatabaseName("IX_CompetitionParticipants_Competition_Points");

            entity.HasIndex(cp => cp.JoinedAt)
                .HasDatabaseName("IX_CompetitionParticipants_JoinedAt");
        });

        // CompetitionWinner configuration
        modelBuilder.Entity<CompetitionWinner>(entity =>
        {
            entity.HasKey(cw => cw.Id);

            entity.HasOne(cw => cw.Competition)
                .WithMany(c => c.Winners)
                .HasForeignKey(cw => cw.CompetitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(cw => cw.Novel)
                .WithMany()
                .HasForeignKey(cw => cw.NovelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(cw => cw.Author)
                .WithMany()
                .HasForeignKey(cw => cw.AuthorId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Property(cw => cw.FinalPoints)
                .HasPrecision(18, 2);

            entity.Property(cw => cw.PrizeWon)
                .HasPrecision(18, 2);

            entity.HasIndex(cw => cw.CompetitionId)
                .HasDatabaseName("IX_CompetitionWinners_CompetitionId");

            entity.HasIndex(cw => new { cw.CompetitionId, cw.Rank })
                .HasDatabaseName("IX_CompetitionWinners_Competition_Rank");

            entity.HasIndex(cw => cw.AuthorId)
                .HasDatabaseName("IX_CompetitionWinners_AuthorId");

            entity.HasIndex(cw => cw.NovelId)
                .HasDatabaseName("IX_CompetitionWinners_NovelId");
        });
    }
}
