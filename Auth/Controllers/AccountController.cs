using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zuhid.Auth.Composers;
using Zuhid.Auth.Entities;
using Zuhid.Auth.Mappers;
using Zuhid.Auth.Repositories;
using Zuhid.Auth.Requests;
using Zuhid.Auth.Services;
using Zuhid.Auth.Validators;

namespace Zuhid.Auth.Controllers;

[ApiController]
[Route("[controller]")]
public class AccountController(UserRepository userRepository, EmailService emailService, AccountMapper accountMapper,
    AccountComposer accountComposer, AppSetting appSetting, LoginValidator loginValidator, JwtService jwtService,
    SsoService ssoService, IHttpClientFactory httpClientFactory
    ) : ControllerBase
{
    [HttpPost("Register")]
    [AllowAnonymous]
    public async Task Register([FromBody] RegisterRequest request)
    {
        var (user, errors) = await userRepository.Add(accountMapper.Map(request), request.Password);
        if (user != null)
        {
            var token = await userRepository.GenerateEmailConfirmationTokenAsync(user);
            var mailMessage = await accountComposer.VerifyEmailCompose(appSetting.AppUrl, user.Email!, user.FullName, token);
            await emailService.SendEmailAsync(mailMessage);
        }
        else
        {
            errors?.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
        }
    }

    [HttpPost("VerifyEmail")]
    [AllowAnonymous]
    public async Task VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var errors = await userRepository.ConfirmEmailAsync(request.Email, request.Token);
        errors?.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (result, user) = await userRepository.Login(request.Email, request.Password);
        if (result.RequiresTwoFactor)
        {
            var providers = new List<string> { "Authenticator", "Email" };
            if (!string.IsNullOrEmpty(user!.PhoneNumber))
                providers.Add("Phone");
            return Ok(new { RequiresTwoFactor = true, UserId = user.Id, Providers = providers });
        }

        loginValidator.Validate(result, ModelState);
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var token = await jwtService.GenerateToken(user!);
        return Ok(new { Token = token, Theme = user!.Theme });
    }

    [HttpGet()]
    public async Task<User?> Get()
    {
        return await userRepository.GetById(UserId);
    }

    [HttpPost("VerifyPhone")]
    public async Task VerifyPhone()
    {
        var (user, token) = await userRepository.GenerateChangePhoneNumberTokenAsync(UserId);
        if (user != null)
        {
            var body = await accountComposer.VerifyPhoneCompose(appSetting.AppUrl, user.PhoneNumber!, user.FullName, token);
            await emailService.SendSmsAsync(user.PhoneNumber!, body);
        }
    }

    [HttpPost("ConfirmPhone")]
    public async Task ConfirmPhone([FromBody] ConfirmPhoneRequest request)
    {
        var errors = await userRepository.ConfirmPhoneAsync(UserId, request.Token);
        errors?.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
    }

    [HttpGet("EnableTwoFactor")]
    public async Task<IActionResult> EnableTwoFactor()
    {
        var user = await userRepository.GetById(UserId);
        if (user == null) return NotFound();

        var key = await userRepository.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userRepository.ResetAuthenticatorKeyAsync(user);
            key = await userRepository.GetAuthenticatorKeyAsync(user);
        }

        var uri = $"otpauth://totp/Zuhid:{user.Email}?secret={key}&issuer=Zuhid&digits=6";
        return Ok(new { SharedKey = key, AuthenticatorUri = uri });
    }

    [HttpPost("EnableTwoFactor")]
    public async Task<IActionResult> EnableTwoFactor([FromBody] EnableTwoFactorRequest request)
    {
        var user = await userRepository.GetById(UserId);
        if (user == null) return NotFound();

        var isValid = await userRepository.VerifyTwoFactorTokenAsync(user, request.Code);
        if (!isValid)
        {
            ModelState.AddModelError("Code", "Verification code is invalid.");
            return BadRequest(ModelState);
        }

        await userRepository.SetTwoFactorEnabledAsync(user, true);
        return Ok();
    }

    [HttpPost("SendTwoFactorToken")]
    [AllowAnonymous]
    public async Task<IActionResult> SendTwoFactorToken([FromBody] SendTwoFactorTokenRequest request)
    {
        var user = await userRepository.GetById(request.UserId);
        if (user == null) return NotFound();

        var code = await userRepository.GenerateTwoFactorTokenAsync(user, request.Provider);

        if (request.Provider == "Email")
        {
            var mailMessage = accountComposer.TwoFactorEmailCompose(user.Email!, user.FullName, code);
            await emailService.SendEmailAsync(mailMessage);
        }
        else if (request.Provider == "Phone")
        {
            var body = accountComposer.TwoFactorSmsCompose(code);
            await emailService.SendSmsAsync(user.PhoneNumber!, body);
        }
        else
        {
            return BadRequest(new { error = "Provider must be Email or Phone" });
        }

        return Ok();
    }

    [HttpPost("LoginTwoFactor")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorLoginRequest request)
    {
        var user = await userRepository.GetById(request.UserId);
        if (user == null) return NotFound();

        var isValid = request.Provider switch
        {
            "Authenticator" => await userRepository.VerifyTwoFactorTokenAsync(user, request.Code),
            "Email" or "Phone" => await userRepository.VerifyTwoFactorTokenAsync(user, request.Provider, request.Code),
            _ => false
        };

        if (!isValid)
        {
            ModelState.AddModelError("Code", "Verification code is invalid.");
            return BadRequest(ModelState);
        }

        var token = await jwtService.GenerateToken(user);
        return Ok(new { Token = token, Theme = user.Theme });
    }

    [HttpGet("sso/microsoft")]
    [AllowAnonymous]
    public IActionResult MicrosoftSsoLogin()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append("sso_state", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });
        var authUrl = $"https://login.microsoftonline.com/{Uri.EscapeDataString(appSetting.Sso.Microsoft.TenantId)}/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(appSetting.Sso.Microsoft.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(appSetting.Sso.Microsoft.RedirectUri)}" +
            $"&scope=openid%20email%20profile" +
            $"&state={Uri.EscapeDataString(state)}";
        return Redirect(authUrl);
    }

    [HttpGet("sso/microsoft/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> MicrosoftSsoCallback(string? code, string? state, string? error)
    {
        var frontendUrl = appSetting.Sso.FrontendUrl;
        if (!string.IsNullOrEmpty(error))
            return Redirect($"{frontendUrl}/account/login?sso_error={Uri.EscapeDataString(error)}");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Redirect($"{frontendUrl}/account/login?sso_error=missing_parameters");

        var storedState = Request.Cookies["sso_state"];
        if (storedState != state)
            return Redirect($"{frontendUrl}/account/login?sso_error=invalid_state");
        Response.Cookies.Delete("sso_state");

        using var httpClient = httpClientFactory.CreateClient();
        var tokenResponse = await httpClient.PostAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(appSetting.Sso.Microsoft.TenantId)}/oauth2/v2.0/token",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", appSetting.Sso.Microsoft.ClientId),
                new KeyValuePair<string, string>("client_secret", appSetting.Sso.Microsoft.ClientSecret),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", appSetting.Sso.Microsoft.RedirectUri),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            ]));

        if (!tokenResponse.IsSuccessStatusCode)
            return Redirect($"{frontendUrl}/account/login?sso_error=token_exchange_failed");

        using var doc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenElement))
            return Redirect($"{frontendUrl}/account/login?sso_error=no_id_token");

        var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(idTokenElement.GetString()!);
        var oid = jwtToken.Claims.FirstOrDefault(c => c.Type == "oid")?.Value
               ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                 ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;

        if (string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(email))
            return Redirect($"{frontendUrl}/account/login?sso_error=missing_claims");

        var firstName = jwtToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        var lastName = jwtToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;

        var (appToken, ssoError) = await ssoService.ProcessLoginAsync("Microsoft", oid, email, firstName, lastName);
        if (appToken == null)
            return Redirect($"{frontendUrl}/account/login?sso_error={Uri.EscapeDataString(ssoError ?? "login_failed")}");

        return Redirect($"{frontendUrl}/sso/callback?token={Uri.EscapeDataString(appToken)}");
    }

    [HttpGet("sso/google")]
    [AllowAnonymous]
    public IActionResult GoogleSsoLogin()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Response.Cookies.Append("sso_state", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });
        var authUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(appSetting.Sso.Google.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(appSetting.Sso.Google.RedirectUri)}" +
            $"&scope=openid%20email%20profile" +
            $"&state={Uri.EscapeDataString(state)}";
        return Redirect(authUrl);
    }

    [HttpGet("sso/google/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleSsoCallback(string? code, string? state, string? error)
    {
        var frontendUrl = appSetting.Sso.FrontendUrl;
        if (!string.IsNullOrEmpty(error))
            return Redirect($"{frontendUrl}/account/login?sso_error={Uri.EscapeDataString(error)}");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Redirect($"{frontendUrl}/account/login?sso_error=missing_parameters");

        var storedState = Request.Cookies["sso_state"];
        if (storedState != state)
            return Redirect($"{frontendUrl}/account/login?sso_error=invalid_state");
        Response.Cookies.Delete("sso_state");

        using var httpClient = httpClientFactory.CreateClient();
        var tokenResponse = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", appSetting.Sso.Google.ClientId),
                new KeyValuePair<string, string>("client_secret", appSetting.Sso.Google.ClientSecret),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", appSetting.Sso.Google.RedirectUri),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            ]));

        if (!tokenResponse.IsSuccessStatusCode)
            return Redirect($"{frontendUrl}/account/login?sso_error=token_exchange_failed");

        using var doc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenElement))
            return Redirect($"{frontendUrl}/account/login?sso_error=no_id_token");

        var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(idTokenElement.GetString()!);
        var sub = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email))
            return Redirect($"{frontendUrl}/account/login?sso_error=missing_claims");

        var firstName = jwtToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        var lastName = jwtToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;

        var (appToken, ssoError) = await ssoService.ProcessLoginAsync("Google", sub, email, firstName, lastName);
        if (appToken == null)
            return Redirect($"{frontendUrl}/account/login?sso_error={Uri.EscapeDataString(ssoError ?? "login_failed")}");

        return Redirect($"{frontendUrl}/sso/callback?token={Uri.EscapeDataString(appToken)}");
    }

    private static readonly HashSet<string> ValidThemes = ["light", "dark", "material"];

    [HttpPost("UpdateTheme")]
    public async Task UpdateTheme([FromBody] UpdateThemeRequest request)
    {
        if (!ValidThemes.Contains(request.Theme))
        {
            ModelState.AddModelError("Theme", "Invalid theme. Must be one of: light, dark, material.");
            return;
        }
        var errors = await userRepository.UpdateTheme(UserId, request.Theme);
        errors?.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
    }

    private Guid UserId
    {
        get
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.Parse(userId!);
        }
    }

    // [HttpPost("LoginTwoFactor")]
    // public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorLoginRequest request)
    // {
    //     var user = await userRepository.GetById(request.UserId);
    //     if (user == null)
    //     {
    //         return NotFound();
    //     }

    //     var (result, signInUser) = await userRepository.TwoFactorAuthenticatorSignInAsync(request.Code, false, false);
    //     if (!result.Succeeded)
    //     {
    //         ModelState.AddModelError("Code", "Invalid authenticator code.");
    //         return BadRequest(ModelState);
    //     }

    //     var token = JwtService.GenerateToken(signInUser!);
    //     return Ok(new { Token = token });
    // }

    // [HttpGet("EnableTwoFactor/{userId}")]
    // public async Task<IActionResult> EnableTwoFactor(Guid userId)
    // {
    //     // var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    //     Console.WriteLine(userId);
    //     var user = await userRepository.GetById(userId);
    //     if (user == null)
    //     {
    //         return NotFound();
    //     }

    //     var unformattedKey = await userRepository.GetAuthenticatorKeyAsync(user);
    //     if (string.IsNullOrEmpty(unformattedKey))
    //     {
    //         await userRepository.ResetAuthenticatorKeyAsync(user);
    //         unformattedKey = await userRepository.GetAuthenticatorKeyAsync(user);
    //     }

    //     var authenticatorUri = string.Format(
    //         "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
    //         "Zuhid",
    //         user.Email,
    //         unformattedKey);

    //     return Ok(new { SharedKey = unformattedKey, AuthenticatorUri = authenticatorUri });
    // }

    // [HttpPost("EnableTwoFactor")]
    // public async Task<IActionResult> EnableTwoFactor([FromBody] EnableTwoFactorRequest request)
    // {
    //     var user = await userRepository.GetById(request.UserId);
    //     if (user == null)
    //     {
    //         return NotFound();
    //     }

    //     var isValid = await userRepository.VerifyTwoFactorTokenAsync(user, request.Code);
    //     if (!isValid)
    //     {
    //         ModelState.AddModelError("Code", "Verification code is invalid.");
    //         return BadRequest(ModelState);
    //     }

    //     await userRepository.SetTwoFactorEnabledAsync(user, true);
    //     return Ok();
    // }

    // [HttpPost("SendEmailTwoFactorToken")]
    // [AllowAnonymous]
    // public async Task<IActionResult> SendEmailTwoFactorToken([FromBody] SendTwoFactorTokenRequest request)
    // {
    //     var user = await userRepository.GetById(request.UserId);
    //     if (user == null)
    //     {
    //         return NotFound();
    //     }

    //     var token = await userRepository.GenerateTwoFactorTokenAsync(user, "Email");
    //     await emailClient.SendTwoFactorEmail(new NotificationRequests.SendTwoFactorEmailRequest(user.Email!, token));
    //     return Ok();
    // }

    // [HttpPost("SendSmsTwoFactorToken")]
    // [AllowAnonymous]
    // public async Task<IActionResult> SendSmsTwoFactorToken([FromBody] SendTwoFactorTokenRequest request)
    // {
    //     var user = await userRepository.GetById(request.UserId);
    //     if (user == null)
    //     {
    //         return NotFound();
    //     }

    //     var token = await userRepository.GenerateTwoFactorTokenAsync(user, "Phone");
    //     await notificationClient.SendTwoFactorSms(new NotificationRequests.SendTwoFactorSmsRequest(user.PhoneNumber!, token));
    //     return Ok();
    // }

    // [HttpPost("VerifyTwoFactorToken")]
    // [AllowAnonymous]
    // public async Task<IActionResult> VerifyTwoFactorToken([FromBody] TwoFactorLoginRequest request)
    // {
    //     var user = await userRepository.GetById(request.UserId);
    //     if (user == null)
    //     {
    //         return NotFound();
    //     }

    //     var isValid = await userRepository.VerifyTwoFactorTokenAsync(user, "Email", request.Code) ||
    //                   await userRepository.VerifyTwoFactorTokenAsync(user, "Phone", request.Code);

    //     if (!isValid)
    //     {
    //         ModelState.AddModelError("Code", "Verification code is invalid.");
    //         return BadRequest(ModelState);
    //     }

    //     var token = JwtService.GenerateToken(user);
    //     return Ok(new { Token = token });
    // }

    // [HttpPut]
    // public async Task Update([FromBody] UpdateAccountRequest request)
    // {
    //     var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    //     if (userId == null)
    //     {
    //         return;
    //     }

    //     var errors = await userRepository.Update(new User
    //     {
    //         Id = Guid.Parse(userId),
    //         FirstName = request.FirstName,
    //         LastName = request.LastName,
    //         PhoneNumber = request.Phone
    //     });

    //     if (errors == null)
    //     {
    //         var user = await userRepository.GetById(Guid.Parse(userId));
    //         if (user != null)
    //         {
    //             var token = await userRepository.GenerateChangePhoneNumberTokenAsync(user, request.Phone);
    //             await notificationClient.VerifyPhone(new NotificationRequests.VerifyPhoneRequest(request.Phone, token));
    //         }
    //     }

    //     errors?.ForEach(e => ModelState.AddModelError(e.Key, e.Value));
    // }
}

