namespace Domain.Constants;

/// <summary>
/// What a deleted account (<see cref="Entities.User.DeletedAt"/>) looks like: its comments, reviews and posts stay,
/// under this name, and nothing else about the person is kept.
/// </summary>
public static class DeletedAccounts
{
    /// <summary>The display name every deleted account shows.</summary>
    public const string DisplayName = "مستخدم محذوف";

    /// <summary>A deleted account's user name is this prefix and a short id ("deleted-3f2a9c1b7d4e"), unique like any user name.</summary>
    public const string UserNamePrefix = "deleted-";

    /// <summary>The reason a pending withdrawal request gets when its owner deletes their account (users and admins read it).</summary>
    public const string WithdrawalCancelledReason = "أُلغي الطلب لأن صاحبه حذف حسابه";
}
