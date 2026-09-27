using Application.ReadingLists.DTOs;
using Application.Users.Commands.FollowUser;

namespace Application.ReadingLists.Commands.CreateReadingList;

/// <summary>The usual success and message, and the reading list that was created.</summary>
public class CreateReadingListResult : OperationResult
{
    /// <summary>The new list exactly as GET /api/readinglist/my-lists returns it; null when nothing was created.</summary>
    public ReadingListPreviewDTO? ReadingList { get; set; }
}
