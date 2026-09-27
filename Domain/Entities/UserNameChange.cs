namespace Domain.Entities;

/// <summary>
/// A user name a member no longer uses, so links to it (/profile/{old name}) still find them. Written by
/// ApplicationDbContext whenever a user name changes; a live user with that name always wins over this history.
/// </summary>
public class UserNameChange
{
    public long Id { get; set; }
    public string UserId { get; set; } = default!;
    public string OldUserName { get; set; } = default!;
    /// <summary>As ASP.NET Identity normalizes user names (upper invariant); what lookups match on.</summary>
    public string OldNormalizedUserName { get; set; } = default!;
    public DateTime ChangedAt { get; set; }
}
