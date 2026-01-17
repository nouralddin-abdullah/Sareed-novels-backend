using System.Text.RegularExpressions;
using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.CreateCompetition;

public class CreateCompetitionCommandHandler(
    ICompetitionRepository competitionRepository,
    IMapper mapper) : IRequestHandler<CreateCompetitionCommand, CompetitionDetailDto>
{
    public async Task<CompetitionDetailDto> Handle(CreateCompetitionCommand request, CancellationToken cancellationToken)
    {
        var slug = GenerateSlug(request.Name);
        
        // Ensure unique slug
        var baseSlug = slug;
        var counter = 1;
        while (await competitionRepository.SlugExistsAsync(slug))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = slug,
            ImageUrl = request.ImageUrl,
            TotalPrize = request.TotalPrize,
            PrizeFirstPlace = request.PrizeFirstPlace,
            PrizeSecondPlace = request.PrizeSecondPlace,
            PrizeThirdPlace = request.PrizeThirdPlace,
            ParticipationStartDate = request.ParticipationStartDate,
            ParticipationEndDate = request.ParticipationEndDate,
            JudgmentStartDate = request.JudgmentStartDate,
            JudgmentEndDate = request.JudgmentEndDate,
            ResultsDate = request.ResultsDate,
            MaxNovelAgeDays = request.MaxNovelAgeDays,
            MinChapters = request.MinChapters,
            Status = CompetitionStatus.Upcoming,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await competitionRepository.CreateAsync(competition);

        return mapper.Map<CompetitionDetailDto>(competition);
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');
        return slug;
    }
}
