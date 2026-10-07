namespace Zuhid.Auth.Requests;

public class TwoFactorLoginRequest
{
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}
