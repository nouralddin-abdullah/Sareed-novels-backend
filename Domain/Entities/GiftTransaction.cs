namespace Domain.Entities;

public class GiftTransaction
{
    /// <summary>
    /// The width of <see cref="Message"/>'s column, in UTF-16 code units. The limit senders see
    /// (AppConfig:Gifts:MessageMaxLength, 200 by default) counts user-perceived characters, and one of those can take
    /// many units (a family emoji joins four people into 11, a flag of Scotland is 14): this holds 200 of any emoji. A
    /// message within the limit but longer than this (a few letters under hundreds of combining marks) is refused as
    /// too long.
    /// </summary>
    public const int MessageMaxStoredLength = 4000;

    public Guid Id { get; set; }
    public Guid GiftId { get; set; }
    public Gift Gift { get; set; } = default!;
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    public string SenderId { get; set; } = default!;
    public User Sender { get; set; } = default!;
    public int Count { get; set; } = 1; // x1, x2, x3, etc.
    public decimal TotalCost { get; set; } // Cost * Count
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// What the sender wrote to the author with the gift (#31), trimmed; null when they wrote nothing, and once a
    /// moderator removed it (the gift itself stays). Public: it shows under the novel's recent gifts.
    /// </summary>
    public string? Message { get; set; }
}
