using Zuhid.Auth.Repositories;

namespace Zuhid.Auth.Services;

public class SsoService(UserRepository userRepository, JwtService jwtService)
{
    public async Task<(string? Token, string? Error)> ProcessLoginAsync(string provider, string providerKey, string email, string? firstName, string? lastName)
    {
        var (user, errors) = await userRepository.GetOrCreateSsoUserAsync(provider, providerKey, email, firstName, lastName);
        if (user == null)
            return (null, errors?.FirstOrDefault().Value ?? "Failed to create user");

        var token = await jwtService.GenerateToken(user);
        return (token, null);
    }
}
