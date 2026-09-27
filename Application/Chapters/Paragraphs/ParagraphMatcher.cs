namespace Application.Chapters.Paragraphs;

/// <summary>
/// Decides which saved paragraphs an edited chapter still contains. A saved paragraph continues, keeping its id and
/// with it its comments, only when an edited paragraph has exactly its visible text (<see cref="ParagraphText.VisibleText"/>);
/// any change to the words, even one letter or one diacritic, makes a new paragraph.
/// Equal paragraphs pair up as a multiset, so each copy of a repeated paragraph keeps its own id: first in order
/// (a longest common subsequence, so deleting one copy removes that copy and not its twin), then whatever is left by
/// text alone (paragraphs the author moved).
/// </summary>
public static class ParagraphMatcher
{
    /// <summary>
    /// Size limit of the in-order pass, in cells (saved × edited paragraphs, after the unchanged start and end are
    /// set aside). Beyond it every unchanged paragraph is still matched, by the moved pass.
    /// </summary>
    public const long InOrderCellLimit = 4_000_000;

    public static ParagraphMatch Match(IReadOnlyList<string> saved, IReadOnlyList<string> edited)
    {
        var keyIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var savedKeys = Keys(saved, keyIds);
        var editedKeys = Keys(edited, keyIds);

        var savedIndexByEdited = new int[editedKeys.Length];
        Array.Fill(savedIndexByEdited, -1);
        var matched = new bool[savedKeys.Length];

        var inOrder = MatchInOrder(savedKeys, editedKeys, savedIndexByEdited, matched);

        // Unchanged paragraphs the author moved: what is left pairs up by text, each copy once, in order.
        var leftByKey = new Dictionary<int, Queue<int>>();
        for (var i = 0; i < savedKeys.Length; i++)
        {
            if (matched[i])
            {
                continue;
            }

            if (!leftByKey.TryGetValue(savedKeys[i], out var queue))
            {
                leftByKey[savedKeys[i]] = queue = new Queue<int>();
            }

            queue.Enqueue(i);
        }

        var moved = 0;
        for (var j = 0; j < editedKeys.Length; j++)
        {
            if (savedIndexByEdited[j] >= 0 || !leftByKey.TryGetValue(editedKeys[j], out var queue) || queue.Count == 0)
            {
                continue;
            }

            var i = queue.Dequeue();
            savedIndexByEdited[j] = i;
            matched[i] = true;
            moved++;
        }

        var removed = Enumerable.Range(0, savedKeys.Length).Where(i => !matched[i]).ToArray();
        return new ParagraphMatch(savedIndexByEdited, removed, inOrder, moved);
    }

    private static int[] Keys(IReadOnlyList<string> paragraphs, Dictionary<string, int> keyIds)
    {
        var keys = new int[paragraphs.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var text = ParagraphText.VisibleText(paragraphs[i]);
            if (!keyIds.TryGetValue(text, out var id))
            {
                keyIds[text] = id = keyIds.Count;
            }

            keys[i] = id;
        }

        return keys;
    }

    /// <summary>Pairs a longest common subsequence of equal paragraphs, preferring the earliest pairs.</summary>
    private static int MatchInOrder(int[] saved, int[] edited, int[] savedIndexByEdited, bool[] matched)
    {
        var count = 0;

        // The unchanged start and end of the chapter, usually nearly all of it, pair up without a table.
        var start = 0;
        while (start < saved.Length && start < edited.Length && saved[start] == edited[start])
        {
            Pair(start, start);
            start++;
        }

        var savedEnd = saved.Length;
        var editedEnd = edited.Length;
        while (savedEnd > start && editedEnd > start && saved[savedEnd - 1] == edited[editedEnd - 1])
        {
            savedEnd--;
            editedEnd--;
            Pair(savedEnd, editedEnd);
        }

        var rows = savedEnd - start;
        var columns = editedEnd - start;
        if (rows == 0 || columns == 0 || (long)rows * columns > InOrderCellLimit)
        {
            return count;
        }

        // lengths[i * width + j]: longest common subsequence of saved[start + i..savedEnd) and edited[start + j..editedEnd).
        // It never exceeds min(rows, columns), which the cell limit keeps below ushort.MaxValue.
        var width = columns + 1;
        var lengths = new ushort[(rows + 1) * width];
        for (var i = rows - 1; i >= 0; i--)
        {
            for (var j = columns - 1; j >= 0; j--)
            {
                lengths[i * width + j] = saved[start + i] == edited[start + j]
                    ? (ushort)(lengths[(i + 1) * width + j + 1] + 1)
                    : Math.Max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);
            }
        }

        for (int i = 0, j = 0; i < rows && j < columns;)
        {
            if (saved[start + i] == edited[start + j])
            {
                Pair(start + i, start + j);
                i++;
                j++;
            }
            else if (lengths[(i + 1) * width + j] >= lengths[i * width + j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return count;

        void Pair(int savedIndex, int editedIndex)
        {
            savedIndexByEdited[editedIndex] = savedIndex;
            matched[savedIndex] = true;
            count++;
        }
    }
}

/// <summary>The outcome of <see cref="ParagraphMatcher.Match"/>.</summary>
public sealed class ParagraphMatch(int[] savedIndexByEdited, int[] removedSaved, int kept, int moved)
{
    /// <summary>For each edited paragraph, the index of the saved paragraph it continues, or -1 for a new paragraph.</summary>
    public IReadOnlyList<int> SavedIndexByEdited => savedIndexByEdited;

    /// <summary>Indexes of the saved paragraphs the edited chapter no longer has, in their saved order.</summary>
    public IReadOnlyList<int> RemovedSaved => removedSaved;

    /// <summary>Unchanged paragraphs still in their order (paragraphs may have come or gone around them).</summary>
    public int Kept => kept;

    /// <summary>Unchanged paragraphs the author moved elsewhere in the chapter.</summary>
    public int Moved => moved;

    /// <summary>Edited paragraphs that match no saved paragraph: new, or changed.</summary>
    public int Created => savedIndexByEdited.Length - kept - moved;

    /// <summary>Saved paragraphs the edit removed or changed.</summary>
    public int Removed => removedSaved.Length;
}
