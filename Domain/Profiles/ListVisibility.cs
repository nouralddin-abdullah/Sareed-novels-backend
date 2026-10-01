namespace Domain.Profiles;

/// <summary>
/// Who may browse one of a member's lists on their profile (#61): their reviews (GET /api/User/{userName}/reviews) or
/// their comments (/comments). There is no "followers only" on purpose: anyone can follow anyone without approval, so
/// it would protect nothing. The names are the API's values and are stored as text: never rename one.
/// </summary>
public enum ListVisibility
{
    /// <summary>Anyone, signed in or not: the default, and how the lists were before there was a choice.</summary>
    Everyone,

    /// <summary>The member alone; anyone else is refused the list (403 ListHidden), but still sees the count.</summary>
    OnlyMe
}

/// <summary>A member's choice for each of their two lists (GET and PATCH /api/User/me/privacy).</summary>
public sealed record ProfileListPrivacy(ListVisibility Reviews, ListVisibility Comments);
