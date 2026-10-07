using System.ComponentModel.DataAnnotations;

namespace Zuhid.Auth.Requests;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);
