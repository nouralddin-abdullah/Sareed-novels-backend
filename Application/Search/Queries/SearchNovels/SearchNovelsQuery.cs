using Application.Common;
using Application.Search.DTOs;
using MediatR;

namespace Application.Search.Queries.SearchNovels;

public record SearchNovelsQuery(SearchNovelsRequest Request) : IRequest<PagedResult<NovelSearchResult>>;
