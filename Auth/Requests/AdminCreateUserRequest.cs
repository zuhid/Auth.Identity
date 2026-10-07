using System.ComponentModel.DataAnnotations;

namespace Zuhid.Auth.Requests;

public class AdminCreateUserRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
    [Required]
    public string Password { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
}
