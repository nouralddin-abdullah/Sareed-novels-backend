namespace Application.Gifts.DTOs;

public class GiftDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string ImageUrl { get; set; } = default!;
    public decimal Cost { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GiftTransactionDto
{
    public Guid Id { get; set; }
    public GiftDto Gift { get; set; } = default!;
    public string SenderUserName { get; set; } = default!;
    public string SenderDisplayName { get; set; } = default!;
    public string SenderProfilePhoto { get; set; } = default!;
    public int Count { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime CreatedAt { get; set; }
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
