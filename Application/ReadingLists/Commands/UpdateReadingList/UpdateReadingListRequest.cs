using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Application.ReadingLists.Commands.UpdateReadingList;

/// <summary>
/// The multipart form of PATCH /api/readinglist/{id}, checked by <see cref="UpdateReadingListRequestValidator"/>. A field
/// left out stays as it is.
/// </summary>
public class UpdateReadingListRequest
{
    public string? Name { get; set; }

    /// <summary>
    /// Bound as sent, "" included (model binding would otherwise turn it into null, which means "unchanged"): empty or
    /// blank clears the description.
    /// </summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Description { get; set; }

    public bool? IsPublic { get; set; }
    public IFormFile? CoverImage { get; set; }

    /// <summary>True removes the list's picture; it can't come with a new <see cref="CoverImage"/>.</summary>
    public bool RemoveCover { get; set; }
}
