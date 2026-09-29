namespace Domain.ReadingLists;

/// <summary>What putting a reading list's novels in a new order did.</summary>
public enum ReadingListReorderResult
{
    /// <summary>The novels are in the new order.</summary>
    Reordered,

    /// <summary>They were in that order already.</summary>
    AlreadyInOrder,

    /// <summary>The ids weren't exactly the list's novels (one missing, extra or repeated): nothing changed.</summary>
    Mismatch,

    /// <summary>The list doesn't exist (any more).</summary>
    ListNotFound
}

/// <summary>A new order for a reading list's novels, given as the novels readers see on it.</summary>
public static class ReadingListOrder
{
    /// <summary>
    /// Every novel of the list in its new order, or null when <paramref name="requested"/> isn't exactly the novels
    /// readers can open, each once. <paramref name="current"/> is every novel of the list in its current order, with
    /// whether readers can open it: the visible ones take the requested order in the places visible novels hold, and the
    /// others (drafts, deleted novels), which clients never see, keep theirs.
    /// </summary>
    public static List<Guid>? Apply(IReadOnlyList<(Guid NovelId, bool Visible)> current, IReadOnlyList<Guid> requested)
    {
        var visible = current.Where(n => n.Visible).Select(n => n.NovelId).ToHashSet();
        if (requested.Count != visible.Count || requested.Distinct().Count() != requested.Count || !visible.SetEquals(requested))
        {
            return null;
        }

        var next = 0;
        return current.Select(n => n.Visible ? requested[next++] : n.NovelId).ToList();
    }
}
