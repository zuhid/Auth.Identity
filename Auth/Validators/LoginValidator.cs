using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Zuhid.Auth.Validators;

public class LoginValidator
{
    public void Validate(SignInResult result, ModelStateDictionary modelState)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.IsLockedOut)
        {
            modelState.AddModelError("Login", "User is locked out");
        }
        else if (result.IsNotAllowed)
        {
            modelState.AddModelError("Login", "User is not allowed to login");
        }
        else if (result.RequiresTwoFactor)
        {
            modelState.AddModelError("Login", "Two factor authentication is required");
        }
        else
        {
            modelState.AddModelError("Login", "Invalid login attempt");
        }
    }
}
