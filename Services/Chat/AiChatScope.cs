using System.Text.RegularExpressions;
using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Services.Chat;

internal enum AiChatScope
{
    Outside,
    Workspace,
    CurrentRecord,
    RoleName
}

internal static partial class AiChatScopeRules
{
    public static bool IsRoleNameQuestion(string question) => RoleQuestion().IsMatch(question.TrimEnd('?', '؟', '.', '!', ' '));

    public static bool RequestsTopCommitteeProcess(string workspace, string question) =>
        workspace is not (RoleNames.Admin or RoleNames.TopCommittee) && TopProcess().IsMatch(question);

    public static bool IsManagerReferralQuestion(string question) =>
        ManagerReferral().IsMatch(question.TrimEnd('?', '.', '!', ' '));

    public static string OutsideReply(string role, string question) => (role == RoleNames.Admin, ArabicText().IsMatch(question)) switch
    {
        (true, true) => "يمكنني مساعدتك في الأسئلة المتعلقة بإجراءات العمل في ترياق لجميع الأدوار.",
        (true, false) => "I can help with Tiryaq workflows across all roles.",
        (false, true) => $"يمكنني مساعدتك في الأسئلة المتعلقة بدورك في ترياق فقط. دورك هو {role}.",
        _ => $"I can help with Tiryaq questions about your {role} workspace."
    };

    [GeneratedRegex(@"^(?:what(?:['’]?s| is) my (?:current )?role(?: in tiryaq)?|what role (?:am i|do i have)|(?:please )?tell me my (?:current )?role|my role|ما (?:هو )?دوري|شو دوري|ما هي صلاحية دوري)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleQuestion();

    [GeneratedRegex(@"\b(?:top\s+committee|ceo\s*(?:and|&)\s*chiefs|final\s+committee)(?:['’]s)?\s+(?:(?:review|approval|decision)\s+)?(?:cycle|workflow|process|votes?|voting|rules)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopProcess();

    [GeneratedRegex(@"^how (?:do|can) (?:i|we) (?:send|forward|refer) (?:an?|the|my|our|this) (?:approved )?(?:recommendation|tender|submission|nomination) to (?:the )?(?:ceo\s*(?:and|&)\s*chiefs|top committee)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManagerReferral();

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex ArabicText();
}
