using Application.Common;
using Application.Gifts.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Gifts.Queries.GetAllGifts;

public class GetAllGiftsQueryHandler(
    IGiftRepository giftRepository,
    IMapper mapper) : IRequestHandler<GetAllGiftsQuery, PagedResult<GiftDto>>
{
    public async Task<PagedResult<GiftDto>> Handle(GetAllGiftsQuery request, CancellationToken cancellationToken)
    {
        var (gifts, totalCount) = await giftRepository.GetAllGifts(
            request.PageNumber,
            request.PageSize,
            includeInactive: false
        );

        var giftDtos = mapper.Map<List<GiftDto>>(gifts);

        return new PagedResult<GiftDto>(
            giftDtos,
            totalCount,
            request.PageNumber,
            request.PageSize
        );
    }
}
