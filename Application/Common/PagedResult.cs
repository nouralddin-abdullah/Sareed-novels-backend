namespace Application.Common;

public class PagedResult<T>
{
    /// <summary>
    /// A page of <paramref name="totalCount"/> items: <see cref="ItemsFrom"/> is the 1-based position of its first item,
    /// <see cref="ItemsTo"/> of its last, never past the last item (a short last page used to claim a full page).
    /// </summary>
    public PagedResult(IEnumerable<T> items, int totalCount, int pageSize, int pageNumber)
    {
        pageSize = Math.Max(1, pageSize);
        pageNumber = Math.Max(1, pageNumber);
        Items = items;
        TotalItemsCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        ItemsFrom = (int)Math.Min((long)pageSize * (pageNumber - 1) + 1, int.MaxValue);
        ItemsTo = (int)Math.Min((long)ItemsFrom + pageSize - 1, totalCount);
    }
    public IEnumerable<T> Items { get; set; }
    public int TotalPages { get; set; }
    public int TotalItemsCount { get; set; }
    public int ItemsFrom { get; set; }
    public int ItemsTo { get; set; }
}
