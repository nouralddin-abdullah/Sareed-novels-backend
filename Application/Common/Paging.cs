namespace Application.Common;

public static class Paging
{
    public const int MaxPageSize = 100;

    /// <summary>Page numbers start at 1 and pages hold 1..100 items, whatever the query string says.</summary>
    public static (int PageNumber, int PageSize) Clamp(int pageNumber, int pageSize) =>
        (Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, MaxPageSize));
}
