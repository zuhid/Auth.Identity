using System.Net.Mail;

namespace Zuhid.Auth.Services;


public class EmailService(SmtpService smtpService, ILogger<EmailService> logger)
// public class EmailService(ISmtpClient smtpClient, ILogger<EmailService> logger)
{
    public virtual async Task SendEmailAsync(MailMessage mailMessage)
    {
        var retryCount = 3;//appSetting.Smtp.RetryCount;
        var delay = 3;// appSetting.Smtp.RetryInterval;
        for (var i = 0; i < retryCount; i++)
        {
            try
            {
                if (mailMessage.From == null || string.IsNullOrWhiteSpace(mailMessage.From.Address))
                {
                    mailMessage.From = new MailAddress("no-reply@company.com"); //new MailAddress(appSetting.Smtp.From);
                }
                await smtpService.SendMailAsync(mailMessage);
                // var smtpClient = new SmtpClient("localhost", 1025);
                // await smtpClient.SendMailAsync(mailMessage);
                return;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, $"{mailMessage.Subject}: {i}");
                if (i == retryCount - 1)
                {
                    throw;
                }
                await Task.Delay(delay);
                delay *= 2;
            }
        }
    }

    public virtual async Task SendSmsAsync(string phone, string body)
    {
        var mailMessage = new MailMessage
        {
            Subject = $"{phone}",
            Body = body,
            IsBodyHtml = false
        };
        mailMessage.To.Add("phone@test.com");
        await SendEmailAsync(mailMessage);
    }
}

public interface ISmtpClient
{
    Task SendMailAsync(MailMessage mailMessage);
}
