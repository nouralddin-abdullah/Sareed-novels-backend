using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Application.Chapters.DTOS;
using Application.Chapters.Scheduling;
using MediatR;

namespace Application.Chapters.Queries.GetChapterAuthor;

public class GetChapterAuthorQuery(Guid novelId, Guid chapterId) : IRequest<ChapterSingleAuthorDTO>, IReadsNovel
{
    public Guid NovelId { get; set; } = novelId;
    public Guid ChapterId { get; set; } = chapterId;

    Guid? IReadsNovel.ReadNovelId => NovelId;
}
