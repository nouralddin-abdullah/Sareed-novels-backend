using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
    public string? ProfileBanner { get; set; }
    public string? UserBio { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Follow> Following { get; set; } = new List<Follow>();
    public ICollection<Follow> Followers { get; set; } = new List<Follow>();
    public ICollection<Novel> Novels { get; set; } = new List<Novel>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<ReviewLike> ReviewLikes { get; set; } = new List<ReviewLike>();
    public ICollection<CommentLikes> CommentLikes { get; set; } = new List<CommentLikes>();
    public ICollection<Comments> Comments { get; set; } = new List<Comments>();
}
