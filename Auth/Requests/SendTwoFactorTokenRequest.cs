namespace Zuhid.Auth.Requests;

public class SendTwoFactorTokenRequest
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
}
