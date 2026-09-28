namespace Application.Common;

public static class Paging
{
    /// <summary>The most items a page holds, unless an endpoint documents more (<see cref="Clamp"/>'s maxPageSize).</summary>
    public const int MaxPageSize = 50;

    /// <summary>
    /// Page numbers start at 1 and pages hold 1..<paramref name="maxPageSize"/> items, whatever the query string says
    /// (0 or a negative number used to reach EF as a negative Skip, a 500; a size of 0 returned every row on some lists).
    /// The page number also stays low enough that (pageNumber - 1) * pageSize can't overflow: past the end is just empty.
    /// </summary>
    public static (int PageNumber, int PageSize) Clamp(int pageNumber, int pageSize, int maxPageSize = MaxPageSize)
    {
        var size = Math.Clamp(pageSize, 1, maxPageSize);
        return (Math.Clamp(pageNumber, 1, int.MaxValue / size), size);
    }
}
