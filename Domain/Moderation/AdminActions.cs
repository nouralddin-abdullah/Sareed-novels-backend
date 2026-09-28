namespace Domain.Moderation;

// The names below are the API's contract and are stored as text in the admin audit log: never rename one.

/// <summary>What an admin did, as the admin audit log (<see cref="Entities.AdminAuditLog"/>) records it.</summary>
public enum AdminAuditAction
{
    /// <summary>Deleted a member's account (DELETE /api/admin/users/{userId}) for an <see cref="AccountDeletionReason"/>.</summary>
    DeleteAccount
}

/// <summary>Why an admin deleted a member's account.</summary>
public enum AccountDeletionReason
{
    /// <summary>
    /// The member is under 13: Sard is for 13 and older, and the terms say such an account is deleted once we learn of it.
    /// </summary>
    Underage,

    /// <summary>Enforcing Sard's rules, when suspending the account isn't enough.</summary>
    PolicyViolation,

    /// <summary>The member asked for it by email and can't do it in the app; the admin has checked it's them.</summary>
    OwnerRequest
}
