using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Zuhid.Auth.Entities;

namespace Zuhid.Auth.Services;

public class JwtService(AppSetting appSetting, UserManager<User> userManager)
{
    public virtual async Task<string> GenerateToken(User user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var privateKey = File.ReadAllText(appSetting.Jwt.PrivateKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);

        var credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var token = new JwtSecurityToken(
            issuer: appSetting.Jwt.Issuer,
            audience: appSetting.Jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(appSetting.Jwt.ExpiryInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
