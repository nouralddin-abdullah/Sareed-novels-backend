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
    internal DbSet<Character> Characters { get; set; }
    internal DbSet<Comments> Comments { get; set; }
    internal DbSet<CommentLikes> CommentLikes { get; set; }
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

            // String properties with constraints
            entity.Property(c => c.Title)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(c => c.Content)
                  .IsRequired();

            entity.Property(c => c.Status)
                  .IsRequired()
                  .HasMaxLength(20)
                  .HasDefaultValue("Draft");

            // Required fields
            entity.Property(c => c.ChapterIndex)
                  .IsRequired();

            entity.Property(c => c.CreatedAt)
                  .IsRequired();

            //Comments count with default value
            entity.Property(c => c.CommentsCount)
                  .HasDefaultValue(0);

            // Foreign key relationship
            entity.HasOne(c => c.Novel)
                  .WithMany(n => n.Chapters)
                  .HasForeignKey(c => c.NovelId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Performance indexes
            entity.HasIndex(c => new { c.NovelId, c.ChapterIndex })
                  .HasDatabaseName("IX_Chapters_Novel_Index");

            entity.HasIndex(c => new { c.NovelId, c.Status, c.ChapterIndex })
                  .HasDatabaseName("IX_Chapters_Novel_Status_Index");

            entity.HasIndex(c => new { c.NovelId, c.Status })
                  .HasDatabaseName("IX_Chapters_Novel_Status");

            entity.HasIndex(c => c.CreatedAt)
                  .HasDatabaseName("IX_Chapters_CreatedAt");
        });

        modelBuilder.Entity<Character>(entity =>
        {
            entity.HasKey(c => c.Id);

            // String properties with constraints
            entity.Property(c => c.CharacterName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(c => c.CharacterDescription)
                  .IsRequired()
                  .HasMaxLength(3000);

            entity.Property(c => c.CharacterImageUrl)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(c => c.CharacterAge)
                  .IsRequired();

            // Foreign key relationship
            entity.HasOne(c => c.Novel)
                  .WithMany(n => n.Characters)
                  .HasForeignKey(c => c.NovelId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Performance indexes
            entity.HasIndex(c => c.NovelId)
                  .HasDatabaseName("IX_Characters_NovelId");

            entity.HasIndex(c => new { c.NovelId, c.CharacterName })
                  .HasDatabaseName("IX_Characters_Novel_Name");

            // Ensure character names are unique within a novel
            entity.HasIndex(c => new { c.NovelId, c.CharacterName })
                  .IsUnique()
                  .HasDatabaseName("IX_Characters_Novel_Name_Unique");
        });

        modelBuilder.Entity<Comments>(entity =>
        {
            entity.HasKey(c => c.Id);

            // String properties with constraints
            entity.Property(c => c.Content)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(c => c.AttachedImageUrl)
                .HasMaxLength(500);

            // Self-referential relationship for replies
            entity.HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete issues

            // User relationship
            entity.HasOne(c => c.User)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Chapter relationship
            entity.HasOne(c => c.Chapter)
                .WithMany() // No navigation property on Chapter
                .HasForeignKey(c => c.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Default values
            entity.Property(c => c.LikesCount)
                .HasDefaultValue(0);

            // Performance indexes
            entity.HasIndex(c => c.ChapterId)
                .HasDatabaseName("IX_Comments_ChapterId");

            entity.HasIndex(c => c.ParentCommentId)
                .HasDatabaseName("IX_Comments_ParentCommentId");

            entity.HasIndex(c => c.UserId)
                .HasDatabaseName("IX_Comments_UserId");

            entity.HasIndex(c => c.CreatedAt)
                .HasDatabaseName("IX_Comments_CreatedAt");

            // Composite index for chapter top-level comments
            entity.HasIndex(c => new { c.ChapterId, c.ParentCommentId })
                .HasDatabaseName("IX_Comments_Chapter_Parent");

            // Soft delete query filter
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
    }
}
