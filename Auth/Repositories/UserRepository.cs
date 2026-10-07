using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zuhid.Auth.Entities;

namespace Zuhid.Auth.Repositories;

public class UserRepository(UserManager<User> userManager, SignInManager<User> signInManager)
{
    public async Task<List<User>> GetAll()
    {
        return await userManager.Users.ToListAsync();
    }

    public async Task<(User?, List<KeyValuePair<string, string>>?)> AdminCreate(User user, string password)
    {
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return (null, result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description)).ToList());

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await userManager.ConfirmEmailAsync(user, token);
        return (user, null);
    }

    public async Task<List<KeyValuePair<string, string>>?> AdminUpdate(Guid userId, string? firstName, string? lastName, string? phone)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return [new KeyValuePair<string, string>("UserNotFound", "User not found")];

        user.FirstName = firstName;
        user.LastName = lastName;
        user.PhoneNumber = phone;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? null
            : [.. result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description))];
    }

    public async Task<List<KeyValuePair<string, string>>?> SetActive(Guid userId, bool isActive)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return [new KeyValuePair<string, string>("UserNotFound", "User not found")];

        var lockoutEnd = isActive ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddYears(100);
        var result = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        return result.Succeeded
            ? null
            : [.. result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description))];
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string role)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user != null && await userManager.IsInRoleAsync(user, role);
    }


    public virtual async Task<(SignInResult Result, User? User)> Login(string email, string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return (SignInResult.Failed, null);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, false, false);
        return (result, result.Succeeded || result.RequiresTwoFactor ? user : null);
    }

    public async Task<string?> GetAuthenticatorKeyAsync(User user)
    {
        return await userManager.GetAuthenticatorKeyAsync(user);
    }

    public async Task<IdentityResult> ResetAuthenticatorKeyAsync(User user)
    {
        return await userManager.ResetAuthenticatorKeyAsync(user);
    }

    public async Task<bool> VerifyTwoFactorTokenAsync(User user, string code)
    {
        return await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);
    }

    public async Task<string> GenerateTwoFactorTokenAsync(User user, string provider)
    {
        return await userManager.GenerateTwoFactorTokenAsync(user, provider);
    }

    public async Task<bool> VerifyTwoFactorTokenAsync(User user, string provider, string token)
    {
        return await userManager.VerifyTwoFactorTokenAsync(user, provider, token);
    }

    public async Task<IdentityResult> SetTwoFactorEnabledAsync(User user, bool enabled)
    {
        return await userManager.SetTwoFactorEnabledAsync(user, enabled);
    }

    public virtual async Task<(SignInResult Result, User? User)> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient)
    {
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient);
        if (result.Succeeded)
        {
            return (result, await signInManager.GetTwoFactorAuthenticationUserAsync());
        }
        return (result, null);
    }

    public async Task<(User? User, List<KeyValuePair<string, string>>? errors)> Add(User user, string password)
    {
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description)).ToList();
            return (null, errors);
        }
        var foundUser = await userManager.FindByEmailAsync(user.Email!);
        return (foundUser, null);
    }

    internal async Task<string> GenerateEmailConfirmationTokenAsync(User user)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return token;
    }

    internal async Task<string> GenerateChangePhoneNumberTokenAsync(User user, string phoneNumber)
    {
        var token = await userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);
        return token;
    }

    public async Task<List<KeyValuePair<string, string>>?> ConfirmEmailAsync(string email, string token)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return [new KeyValuePair<string, string>("UserNotFound", "User not found")];
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? null
            : [.. result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description))];
    }

    public async Task<(User?, string)> GenerateChangePhoneNumberTokenAsync(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return (null, string.Empty);
        }
        var token = await userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber!);
        return (user, token);
    }

    public async Task<List<KeyValuePair<string, string>>?> Update(User user)
    {
        var foundUser = await userManager.FindByIdAsync(user.Id.ToString());
        if (foundUser == null)
        {
            return [new KeyValuePair<string, string>("UserNotFound", "User not found")];
        }

        foundUser.FirstName = user.FirstName;
        foundUser.LastName = user.LastName;
        foundUser.PhoneNumber = user.PhoneNumber;

        var result = await userManager.UpdateAsync(foundUser);
        return result.Succeeded
            ? null
            : [.. result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description))];
    }

    public async Task<List<KeyValuePair<string, string>>?> UpdateTheme(Guid userId, string theme)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return [new KeyValuePair<string, string>("UserNotFound", "User not found")];

        user.Theme = theme;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? null
            : [.. result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description))];
    }

    public virtual async Task<User?> GetById(Guid id)
    {
        return await userManager.FindByIdAsync(id.ToString());
    }

    public async Task<(User? User, List<KeyValuePair<string, string>>? Errors)> GetOrCreateSsoUserAsync(string provider, string providerKey, string email, string? firstName, string? lastName)
    {
        var user = await userManager.FindByLoginAsync(provider, providerKey);
        if (user == null)
        {
            user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new User { UserName = email, Email = email, FirstName = firstName, LastName = lastName, EmailConfirmed = true };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return (null, createResult.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description)).ToList());
            }
            await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
            return (null, [new KeyValuePair<string, string>("LockedOut", "Account is locked.")]);

        return (user, null);
    }

    public async Task<List<KeyValuePair<string, string>>?> ConfirmPhoneAsync(Guid userId, string token)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return [new KeyValuePair<string, string>("UserNotFound", "User not found")];
        }

        var result = await userManager.ChangePhoneNumberAsync(user, user.PhoneNumber!, token);
        return result.Succeeded
            ? null
            : [.. result.Errors.Select(e => new KeyValuePair<string, string>(e.Code, e.Description))];
    }
}

