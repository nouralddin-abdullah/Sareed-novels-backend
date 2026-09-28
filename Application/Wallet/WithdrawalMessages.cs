using Application.Common;
using Application.Services;
using Domain.Constants;

namespace Application.Wallet;

/// <summary>Arabic messages about what can be withdrawn (#22), in the tone of the other wallet messages.</summary>
public static class WithdrawalMessages
{
    /// <summary>The code of a withdrawal asking for more than is withdrawable (HTTP 400), when requested and when approved.</summary>
    public const string NotWithdrawableCode = "InsufficientWithdrawableBalance";

    /// <summary>To the member: what they can withdraw now, when more of their earnings is released, and the rule.</summary>
    public static string NotWithdrawable(WithdrawableBalance balance)
    {
        var now = balance.Withdrawable > 0
            ? $"يمكنك سحب {Points.Format(balance.Withdrawable)} نقطة فقط الآن"
            : "لا توجد نقاط قابلة للسحب الآن";
        var next = balance.PendingEarnings > 0 && balance.NextReleaseAt is { } releaseAt
            ? $"، وتصبح أرباحك التالية قابلة للسحب خلال {Days(DaysUntil(releaseAt, balance.AsOf))}"
            : "";
        return $"{now}{next}. {Rule(balance.HoldDays)}";
    }

    /// <summary>What can be withdrawn, in one sentence.</summary>
    public static string Rule(int holdDays) => holdDays > 0
        ? $"تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، بعد {Days(holdDays)} من استلامها، أما النقاط المشحونة أو المشتراة فلا تُسحب."
        : "تُسحب أرباح الهدايا واشتراكات الوصول المبكر وحدها، أما النقاط المشحونة أو المشتراة فلا تُسحب.";

    /// <summary>
    /// The reason a request its owner cancelled carries (#27). Its status is Rejected, as for the requests an account
    /// deletion cancels, which every app already shows with its reason.
    /// </summary>
    public const string CancelledByOwnerReason = "ألغاه صاحب الطلب";

    /// <summary>Why a request can't be cancelled: it isn't pending any more.</summary>
    public static string NotCancellable(string status, string? rejectionReason) => status switch
    {
        RequestStatus.Approved => "قُبل طلب السحب هذا من قبل، فلا يمكن إلغاؤه.",
        RequestStatus.Rejected when rejectionReason == CancelledByOwnerReason => "ألغيت طلب السحب هذا من قبل.",
        RequestStatus.Rejected => "رُفض طلب السحب هذا من قبل، فلا يمكن إلغاؤه.",
        _ => "عولج طلب السحب هذا من قبل، فلا يمكن إلغاؤه."
    };

    /// <summary>To the admin approving a request the member can't be paid in full any more.</summary>
    public static string NotPayable(WithdrawableBalance balance) =>
        $"رصيد المستخدم القابل للسحب لا يكفي لهذا الطلب: القابل للسحب الآن {Points.Format(balance.Payable)} نقطة.";

    /// <summary>Whole days from <paramref name="now"/> until <paramref name="at"/>, rounded up, at least 1.</summary>
    public static int DaysUntil(DateTime at, DateTime now) => Math.Max(1, (int)Math.Ceiling((at - now).TotalDays));

    /// <summary>
    /// A number of days as Arabic counts them after «خلال» or «بعد»: يوم واحد، يومين، 3 إلى 10 أيام، 11 إلى 99 يومًا،
    /// 100 يوم.
    /// </summary>
    public static string Days(int days) => days switch
    {
        1 => "يوم واحد",
        2 => "يومين",
        _ => (days % 100) switch
        {
            >= 3 and <= 10 => $"{days} أيام",
            >= 11 => $"{days} يومًا",
            _ => $"{days} يوم"
        }
    };
}
