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
        // Out-of-range paging used to throw (page 0) or divide by zero (size 0).
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (gifts, totalCount) = await giftRepository.GetAllGifts(
            pageNumber,
            pageSize,
            includeInactive: false
        );

        var giftDtos = mapper.Map<List<GiftDto>>(gifts);

        return new PagedResult<GiftDto>(
            giftDtos,
            totalCount,
            pageSize: pageSize,
            pageNumber: pageNumber
        );
    }
}
