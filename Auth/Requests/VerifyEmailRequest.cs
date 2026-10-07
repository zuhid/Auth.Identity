using System.ComponentModel.DataAnnotations;

namespace Zuhid.Auth.Requests;

public record VerifyEmailRequest(
    [Required] string Email,
    [Required] string Token
);

