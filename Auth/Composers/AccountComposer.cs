using System.Net;
using System.Net.Mail;

namespace Zuhid.Auth.Composers;

public class AccountComposer : BaseComposer
{
    internal async Task<MailMessage> VerifyEmailCompose(string appUrl, string toEmail, string fullName, string token)
    {
        var subject = "Verify Email";
        var style = await ReadTemplate("VerifyEmailComposer.css");
        var body = (await ReadTemplate("VerifyEmailComposer.html"))
            .Replace("{{appUrl}}", appUrl ?? string.Empty)
            .Replace("{{fullName}}", fullName ?? string.Empty)
            .Replace("{{emailEncoded}}", WebUtility.UrlEncode(toEmail))
            .Replace("{{token}}", WebUtility.UrlEncode(token));
        var mailMessage = new MailMessage
        {
            Subject = subject,
            Body = await CreateHtmlAsync(body, style),
            IsBodyHtml = true
        };
        mailMessage.To.Add(toEmail);
        return mailMessage;
    }

    internal async Task<string> VerifyPhoneCompose(string appUrl, string phoneNumber, string fullName, string token)
    {
        var template = await ReadTemplate("VerifyPhoneComposer.txt");
        return template
            .Replace("{{token}}", token);
    }

    internal MailMessage TwoFactorEmailCompose(string toEmail, string fullName, string code)
    {
        var mailMessage = new MailMessage
        {
            Subject = "Your two-factor authentication code",
            Body = $"Hi {fullName},\n\nYour login verification code is: {code}\n\nThis code expires in a few minutes.",
            IsBodyHtml = false
        };
        mailMessage.To.Add(toEmail);
        return mailMessage;
    }

    internal string TwoFactorSmsCompose(string code)
    {
        return $"Your login verification code is: {code}";
    }
}
