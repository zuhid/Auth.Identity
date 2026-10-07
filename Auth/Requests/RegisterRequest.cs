using System.ComponentModel.DataAnnotations;

namespace Zuhid.Auth.Requests;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string FirstName,
    string LastName,
    string Phone
);
