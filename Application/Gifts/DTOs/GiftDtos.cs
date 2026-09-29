namespace Application.Gifts.DTOs;

public class GiftDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>The gift's Arabic name (وردة, بيتزا...).</summary>
    public string NameAr { get; set; } = default!;
    public string ImageUrl { get; set; } = default!;
    public decimal Cost { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GiftTransactionDto
{
    public Guid Id { get; set; }
    public GiftDto Gift { get; set; } = default!;
    /// <summary>The novel the gift was sent to.</summary>
    public Guid NovelId { get; set; }
    /// <summary>That novel's current slug.</summary>
    public string? NovelSlug { get; set; }
    public string SenderUserName { get; set; } = default!;
    public string SenderDisplayName { get; set; } = default!;
    public string SenderProfilePhoto { get; set; } = default!;
    public int Count { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// What the sender wrote to the author with the gift (#31), public, for anonymous callers too; null when there is
    /// none, once a moderator removed it, and for a signed-in viewer who blocked the sender or whom the sender blocked.
    /// Report it as target type GiftMessage with this item's <see cref="Id"/>.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// A gift the signed-in user sent (GET /api/gift/my-history): what, how many, and the novel it went to. The sender is
/// the user, so there are no sender fields (they were always null here).
/// </summary>
public class GiftHistoryItemDto
{
    public Guid Id { get; set; }
    public GiftDto Gift { get; set; } = default!;
    public Guid NovelId { get; set; }
    /// <summary>The novel's current slug, title and cover.</summary>
    public string NovelSlug { get; set; } = default!;
    public string NovelTitle { get; set; } = default!;
    public string? NovelCoverImageUrl { get; set; }
    public int Count { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The message the user wrote with the gift (#31); null when none, or once a moderator removed it.</summary>
    public string? Message { get; set; }
}

public class NovelGiftsSummaryDto
{
    public List<GiftTransactionDto> RecentGifts { get; set; } = new();
    public decimal TotalPointsReceived { get; set; }
    public int TotalGiftsCount { get; set; }
}

public class TopSupporterDto
{
    public string UserId { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string ProfilePhoto { get; set; } = default!;
    public decimal TotalPointsGifted { get; set; }
    public int TotalGiftsCount { get; set; }
    public int Rank { get; set; }
}

public class GlobalLeaderboardDto
{
    public List<TopSupporterDto> Supporters { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTime LastUpdated { get; set; }
}
