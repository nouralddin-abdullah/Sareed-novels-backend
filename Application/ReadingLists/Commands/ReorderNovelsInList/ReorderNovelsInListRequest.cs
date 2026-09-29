namespace Application.ReadingLists.Commands.ReorderNovelsInList;

/// <summary>The JSON body of PATCH /api/readinglist/{id}/novels/order.</summary>
public class ReorderNovelsInListRequest
{
    /// <summary>Every novel GET /api/readinglist/{id} lists, each once, in the new order.</summary>
    public List<Guid>? OrderedNovelIds { get; set; }
}
