using Microsoft.AspNetCore.Identity;

namespace Zuhid.Auth.Entities;

public class User : IdentityUser<Guid>
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Theme { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}
