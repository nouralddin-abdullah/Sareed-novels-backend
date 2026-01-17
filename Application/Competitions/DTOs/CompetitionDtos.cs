using Application.Novels.DTOS;

namespace Application.Competitions.DTOs;

// List view DTO
public class CompetitionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? ImageUrl { get; set; }
    public decimal TotalPrize { get; set; }
    public string Status { get; set; } = default!;
    public DateTime ParticipationStartDate { get; set; }
    public DateTime ParticipationEndDate { get; set; }
    public DateTime ResultsDate { get; set; }
    public int ParticipantCount { get; set; }
    public bool CanJoin { get; set; }
}

// Detail view DTO
public class CompetitionDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? ImageUrl { get; set; }
    
    // Prizes
    public decimal TotalPrize { get; set; }
    public decimal PrizeFirstPlace { get; set; }
    public decimal PrizeSecondPlace { get; set; }
    public decimal PrizeThirdPlace { get; set; }
    
    // Schedule
    public DateTime ParticipationStartDate { get; set; }
    public DateTime ParticipationEndDate { get; set; }
    public DateTime JudgmentStartDate { get; set; }
    public DateTime JudgmentEndDate { get; set; }
    public DateTime ResultsDate { get; set; }
    
    // Rules
    public int? MaxNovelAgeDays { get; set; }
    public int MinChapters { get; set; }
    
    // Status
    public string Status { get; set; } = default!;
    public bool IsActive { get; set; }
    public bool CanJoin { get; set; }
    
    public int ParticipantCount { get; set; }
    public List<CompetitionWinnerDto> Winners { get; set; } = new();
}

// Participant view (includes novel and author)
public class CompetitionParticipantDto
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public string NovelTitle { get; set; } = default!;
    public string NovelSlug { get; set; } = default!;
    public string NovelCoverImageUrl { get; set; } = default!;
    public List<GenreSmallDto> GenresList { get; set; } = new();
    public AuthorDTO Author { get; set; } = default!;
    
    public DateTime JoinedAt { get; set; }
    public int CompetitionViews { get; set; }
    public decimal TotalPoints { get; set; } // CurrentPoints + ExtraPoints (ExtraPoints not exposed separately)
    public int CurrentRank { get; set; }
}

// Winner DTO
public class CompetitionWinnerDto
{
    public Guid Id { get; set; }
    public int Rank { get; set; }
    public Guid NovelId { get; set; }
    public string NovelTitle { get; set; } = default!;
    public string NovelSlug { get; set; } = default!;
    public string NovelCoverImageUrl { get; set; } = default!;
    public AuthorDTO Author { get; set; } = default!;
    public decimal FinalPoints { get; set; }
    public int FinalViews { get; set; }
    public decimal PrizeWon { get; set; }
    public DateTime AwardedAt { get; set; }
}

// Leaderboard entry
public class CompetitionLeaderboardEntryDto
{
    public int Rank { get; set; }
    public Guid NovelId { get; set; }
    public string NovelTitle { get; set; } = default!;
    public string NovelSlug { get; set; } = default!;
    public string NovelCoverImageUrl { get; set; } = default!;
    public AuthorDTO Author { get; set; } = default!;
    public decimal TotalPoints { get; set; }
    public int CompetitionViews { get; set; }
}

// My participation DTO
public class MyCompetitionParticipationDto
{
    public Guid CompetitionId { get; set; }
    public string CompetitionName { get; set; } = default!;
    public string CompetitionSlug { get; set; } = default!;
    public string CompetitionStatus { get; set; } = default!;
    public Guid NovelId { get; set; }
    public string NovelTitle { get; set; } = default!;
    public string NovelSlug { get; set; } = default!;
    public string NovelCoverImageUrl { get; set; } = default!;
    public DateTime JoinedAt { get; set; }
    public int CurrentRank { get; set; }
    public decimal TotalPoints { get; set; }
    public int CompetitionViews { get; set; }
}
