using System.Text.RegularExpressions;

namespace ERPApiHub.Application.Audit;

public sealed class PiiMaskingService
{
    private static readonly Regex EmailRegex = new(@"\b([a-zA-Z0-9])[^@]*@([a-zA-Z0-9].*?\.[a-zA-Z]{2,})\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"\b(0\d)(\d+)(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"\b([A-ZÀ-Ỹa-zà-Ỹ])[\w]+(\s[\w]+)*\b", RegexOptions.Compiled);

    public string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email;
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;
        return $"{email[0]}***{email[atIndex..]}";
    }

    public string MaskPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 6) return phone;
        return $"{phone[..2]}***{phone[^4..]}";
    }

    public string MaskText(string text, bool maskEmails = true, bool maskPhones = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        if (maskEmails)
        {
            text = EmailRegex.Replace(text, m =>
            {
                var localPart = m.Groups[1].Value;
                var domain = m.Groups[2].Value;
                return $"{localPart}***@{domain}";
            });
        }

        if (maskPhones)
        {
            text = PhoneRegex.Replace(text, m =>
            {
                var prefix = m.Groups[1].Value;
                var suffix = m.Groups[3].Value;
                return $"{prefix}***{suffix}";
            });
        }

        return text;
    }
}