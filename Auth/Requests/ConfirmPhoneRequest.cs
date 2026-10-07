using System.ComponentModel.DataAnnotations;

namespace Zuhid.Auth.Requests;

public record ConfirmPhoneRequest(
    [Required] string Token
);
