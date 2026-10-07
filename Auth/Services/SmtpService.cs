using System.Net.Mail;

namespace Zuhid.Auth.Services;

public class SmtpService(SmtpClient smtpClient)
{
    public async Task SendMailAsync(MailMessage mailMessage)
    {
        await smtpClient.SendMailAsync(mailMessage);
    }
}
