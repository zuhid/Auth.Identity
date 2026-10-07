using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zuhid.Auth.Entities;
using Zuhid.Auth.Services;

namespace Zuhid.Auth.Tests.Integration;

public class AccountTest(WebApplicationFactory<Program> factory) : BaseIntegrationTest(factory)
{
    private static readonly HttpClient MailpitClient = new() { BaseAddress = new Uri("http://localhost:8025") };

    private async Task DeleteMailpitMessagesForEmail(string email)
    {
        var searchResponse = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(email)}");
        if (!searchResponse.IsSuccessStatusCode)
        {
            return;
        }

        using var doc = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        var messages = doc.RootElement.GetProperty("messages");
        if (messages.ValueKind == JsonValueKind.Null || messages.GetArrayLength() == 0)
        {
            return;
        }

        var ids = messages.EnumerateArray()
            .Select(m => m.GetProperty("ID").GetString())
            .ToArray();

        await MailpitClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/messages")
        {
            Content = JsonContent.Create(new { IDs = ids })
        });
    }

    [Fact]
    public async Task Create_Account_And_Login()
    {
        // Arrange
        var email = $"test_{Guid.NewGuid()}@example.com";
        await DeleteMailpitMessagesForEmail(email);

        var request = new
        {
            Email = email,
            Password = "Password123!",
            FirstName = "Test",
            LastName = "User",
            Phone = "1234567890"
        };

        // Act
        var response = await Client.PostAsJsonAsync("/Account/Register", request);

        // Assert registration succeeded
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert email was delivered to Mailpit
        var mailResponse = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(email)}");
        Assert.True(mailResponse.IsSuccessStatusCode, "Mailpit API request failed");

        using var doc = JsonDocument.Parse(await mailResponse.Content.ReadAsStringAsync());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.True(messages.ValueKind != JsonValueKind.Null && messages.GetArrayLength() > 0,
            $"Expected at least one email sent to {email} in Mailpit, but found none.");

        var firstMessage = messages[0];
        var toAddresses = firstMessage.GetProperty("To");
        var matchedRecipient = toAddresses.EnumerateArray()
            .Any(recipient => recipient.GetProperty("Address").GetString() == email);
        Assert.True(matchedRecipient, $"Expected email recipient to be {email}, but none of the To addresses matched.");

        var messageId = firstMessage.GetProperty("ID").GetString();
        var messageResponse = await MailpitClient.GetAsync($"/api/v1/message/{messageId}");
        Assert.True(messageResponse.IsSuccessStatusCode, "Mailpit message detail request failed");

        using var messageDoc = JsonDocument.Parse(await messageResponse.Content.ReadAsStringAsync());
        var htmlBody = messageDoc.RootElement.GetProperty("HTML").GetString();
        Assert.False(string.IsNullOrWhiteSpace(htmlBody), "Expected HTML body in the email, but it was empty.");

        var tokenMatch = Regex.Match(htmlBody!, @"/account/verify-email/[^/]+/([^""]+)""",
            RegexOptions.IgnoreCase);
        Assert.True(tokenMatch.Success, "Expected a verify email link in the email body.");
        var token = Uri.UnescapeDataString(tokenMatch.Groups[1].Value);

        // Act - verify email
        var verifyRequest = new { Email = email, Token = token };
        var verifyResponse = await Client.PostAsJsonAsync("/Account/VerifyEmail", verifyRequest);

        // Assert email verification succeeded
        Assert.True(verifyResponse.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        // Act - login
        var loginRequest = new { Email = email, request.Password };
        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", loginRequest);

        // Assert login succeeded
        Assert.True(loginResponse.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var jwtToken = loginDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jwtToken), "Expected a JWT token in the login response.");

        // Assert phone number is set
        var userRequest = new HttpRequestMessage(HttpMethod.Get, "/Account");
        userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
        var userResponse = await Client.SendAsync(userRequest);
        Assert.True(userResponse.IsSuccessStatusCode);

        // Act - Verify Phone
        var phoneEmail = "phone@test.com";
        await DeleteMailpitMessagesForEmail(phoneEmail);

        var verifyPhoneRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/VerifyPhone");
        verifyPhoneRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
        var verifyPhoneResponse = await Client.SendAsync(verifyPhoneRequest);

        // Assert Verify Phone request succeeded
        Assert.True(verifyPhoneResponse.IsSuccessStatusCode);

        // Assert phone verification email was delivered to Mailpit
        var phoneMailResponse = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(phoneEmail)}");
        Assert.True(phoneMailResponse.IsSuccessStatusCode, "Mailpit API request for phone verification failed");

        using var phoneDoc = JsonDocument.Parse(await phoneMailResponse.Content.ReadAsStringAsync());
        var phoneMessages = phoneDoc.RootElement.GetProperty("messages");
        Assert.True(phoneMessages.ValueKind != JsonValueKind.Null && phoneMessages.GetArrayLength() > 0,
            $"Expected at least one email sent to {phoneEmail} for phone verification.");

        var phoneMessageId = phoneMessages[0].GetProperty("ID").GetString();
        var phoneMessageDetailResponse = await MailpitClient.GetAsync($"/api/v1/message/{phoneMessageId}");
        using var phoneMessageDetailDoc = JsonDocument.Parse(await phoneMessageDetailResponse.Content.ReadAsStringAsync());
        var phoneBody = phoneMessageDetailDoc.RootElement.GetProperty("Text").GetString();
        Assert.Contains("Your verification code is:", phoneBody);
    }

    private async Task<string> RegisterVerifyEmailAndLogin(string email, string password = "Password123!")
    {
        await DeleteMailpitMessagesForEmail(email);
        await Client.PostAsJsonAsync("/Account/Register", new { Email = email, Password = password, FirstName = "Test", LastName = "User", Phone = "1234567890" });

        var mailResponse = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(email)}");
        using var mailDoc = JsonDocument.Parse(await mailResponse.Content.ReadAsStringAsync());
        var messageId = mailDoc.RootElement.GetProperty("messages")[0].GetProperty("ID").GetString();

        var messageResponse = await MailpitClient.GetAsync($"/api/v1/message/{messageId}");
        using var messageDoc = JsonDocument.Parse(await messageResponse.Content.ReadAsStringAsync());
        var htmlBody = messageDoc.RootElement.GetProperty("HTML").GetString();
        var tokenMatch = Regex.Match(htmlBody!, @"/account/verify-email/[^/]+/([^""]+)""", RegexOptions.IgnoreCase);
        var verifyToken = Uri.UnescapeDataString(tokenMatch.Groups[1].Value);

        await Client.PostAsJsonAsync("/Account/VerifyEmail", new { Email = email, Token = verifyToken });

        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = password });
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        return loginDoc.RootElement.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Enable_Microsoft_Authenticator()
    {
        // Arrange — register a fresh account and log in
        var email = $"test_{Guid.NewGuid()}@example.com";
        var jwtToken = await RegisterVerifyEmailAndLogin(email);
        var authHeader = new AuthenticationHeaderValue("Bearer", jwtToken);

        // Act — fetch the TOTP setup info
        var setupRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/EnableTwoFactor");
        setupRequest.Headers.Authorization = authHeader;
        var setupResponse = await Client.SendAsync(setupRequest);

        // Assert — response contains a shared key and a valid otpauth URI
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        using var setupDoc = JsonDocument.Parse(await setupResponse.Content.ReadAsStringAsync());
        var sharedKey = setupDoc.RootElement.GetProperty("sharedKey").GetString();
        var authenticatorUri = setupDoc.RootElement.GetProperty("authenticatorUri").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sharedKey));
        Assert.StartsWith("otpauth://totp/Zuhid:", authenticatorUri);
        Assert.Contains(email, authenticatorUri);

        // Act — submit an invalid code
        var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/EnableTwoFactor");
        invalidRequest.Headers.Authorization = authHeader;
        invalidRequest.Content = JsonContent.Create(new { Code = "000000" });
        var invalidResponse = await Client.SendAsync(invalidRequest);

        // Assert — invalid code is rejected
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        // Compute a valid TOTP code from the shared key (RFC 6238, same algorithm the API validates against)
        var validCode = ComputeTotp(sharedKey!);

        // Act — submit the valid code
        var enableRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/EnableTwoFactor");
        enableRequest.Headers.Authorization = authHeader;
        enableRequest.Content = JsonContent.Create(new { Code = validCode });
        var enableResponse = await Client.SendAsync(enableRequest);

        // Assert — 2FA is now enabled
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var accountRequest = new HttpRequestMessage(HttpMethod.Get, "/Account");
        accountRequest.Headers.Authorization = authHeader;
        var accountResponse = await Client.SendAsync(accountRequest);
        using var accountDoc = JsonDocument.Parse(await accountResponse.Content.ReadAsStringAsync());
        Assert.True(accountDoc.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
    }

    private async Task<(string jwtToken, string sharedKey)> RegisterVerifyLoginAndEnableTwoFactor(string email)
    {
        var jwtToken = await RegisterVerifyEmailAndLogin(email);
        var authHeader = new AuthenticationHeaderValue("Bearer", jwtToken);

        var setupRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/EnableTwoFactor");
        setupRequest.Headers.Authorization = authHeader;
        var setupResponse = await Client.SendAsync(setupRequest);
        using var setupDoc = JsonDocument.Parse(await setupResponse.Content.ReadAsStringAsync());
        var sharedKey = setupDoc.RootElement.GetProperty("sharedKey").GetString()!;

        var validCode = ComputeTotp(sharedKey);
        var enableRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/EnableTwoFactor");
        enableRequest.Headers.Authorization = authHeader;
        enableRequest.Content = JsonContent.Create(new { Code = validCode });
        await Client.SendAsync(enableRequest);

        return (jwtToken, sharedKey);
    }

    [Fact]
    public async Task Login_With_Two_Factor_Authenticator()
    {
        var email = $"test_{Guid.NewGuid()}@example.com";
        var (_, sharedKey) = await RegisterVerifyLoginAndEnableTwoFactor(email);

        // Login — expect requiresTwoFactor
        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.True(loginDoc.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        var userId = loginDoc.RootElement.GetProperty("userId").GetString()!;
        var providers = loginDoc.RootElement.GetProperty("providers").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("Authenticator", providers);
        Assert.Contains("Email", providers);

        // Reject invalid code
        var invalidResponse = await Client.PostAsJsonAsync("/Account/LoginTwoFactor", new { UserId = Guid.Parse(userId), Code = "000000", Provider = "Authenticator" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        // Accept valid TOTP code
        var validCode = ComputeTotp(sharedKey);
        var validResponse = await Client.PostAsJsonAsync("/Account/LoginTwoFactor", new { UserId = Guid.Parse(userId), Code = validCode, Provider = "Authenticator" });
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        using var validDoc = JsonDocument.Parse(await validResponse.Content.ReadAsStringAsync());
        var newJwt = validDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newJwt));
    }

    [Fact]
    public async Task Login_With_Two_Factor_Email()
    {
        var email = $"test_{Guid.NewGuid()}@example.com";
        await RegisterVerifyLoginAndEnableTwoFactor(email);

        // Login — expect requiresTwoFactor
        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = "Password123!" });
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.True(loginDoc.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        var userId = loginDoc.RootElement.GetProperty("userId").GetString()!;

        // Send 2FA token via email
        await DeleteMailpitMessagesForEmail(email);
        var sendResponse = await Client.PostAsJsonAsync("/Account/SendTwoFactorToken", new { UserId = Guid.Parse(userId), Provider = "Email" });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        // Wait for the 2FA email in Mailpit and extract the code
        string? code = null;
        for (var i = 0; i < 20 && code == null; i++)
        {
            await Task.Delay(500);
            var mailRes = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(email)}");
            if (!mailRes.IsSuccessStatusCode) continue;
            using var mailDoc = JsonDocument.Parse(await mailRes.Content.ReadAsStringAsync());
            var messages = mailDoc.RootElement.GetProperty("messages");
            if (messages.ValueKind == JsonValueKind.Null || messages.GetArrayLength() == 0) continue;
            foreach (var msg in messages.EnumerateArray())
            {
                var subject = msg.GetProperty("Snippet").GetString() ?? "";
                var msgId = msg.GetProperty("ID").GetString()!;
                var msgRes = await MailpitClient.GetAsync($"/api/v1/message/{msgId}");
                if (!msgRes.IsSuccessStatusCode) continue;
                using var msgDoc = JsonDocument.Parse(await msgRes.Content.ReadAsStringAsync());
                var body = msgDoc.RootElement.GetProperty("Text").GetString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(body, @"code is: (\d+)");
                if (match.Success) { code = match.Groups[1].Value; break; }
            }
        }
        Assert.False(string.IsNullOrEmpty(code), "Did not receive 2FA email code in Mailpit");

        // Reject invalid code
        var invalidResponse = await Client.PostAsJsonAsync("/Account/LoginTwoFactor", new { UserId = Guid.Parse(userId), Code = "000000", Provider = "Email" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        // Accept valid code
        var validResponse = await Client.PostAsJsonAsync("/Account/LoginTwoFactor", new { UserId = Guid.Parse(userId), Code = code, Provider = "Email" });
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        using var validDoc = JsonDocument.Parse(await validResponse.Content.ReadAsStringAsync());
        var newJwt = validDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newJwt));
    }

    [Fact]
    public async Task Login_With_Two_Factor_Phone()
    {
        var email = $"test_{Guid.NewGuid()}@example.com";
        await RegisterVerifyLoginAndEnableTwoFactor(email);

        // Login — expect requiresTwoFactor
        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = "Password123!" });
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.True(loginDoc.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        var userId = loginDoc.RootElement.GetProperty("userId").GetString()!;
        var providers = loginDoc.RootElement.GetProperty("providers").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("Phone", providers);

        // Send 2FA token via phone (routed to phone@test.com via SMS)
        var phoneEmail = "phone@test.com";
        await DeleteMailpitMessagesForEmail(phoneEmail);
        var sendResponse = await Client.PostAsJsonAsync("/Account/SendTwoFactorToken", new { UserId = Guid.Parse(userId), Provider = "Phone" });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        // Wait for the SMS in Mailpit and extract the code
        string? code = null;
        for (var i = 0; i < 20 && code == null; i++)
        {
            await Task.Delay(500);
            var mailRes = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(phoneEmail)}");
            if (!mailRes.IsSuccessStatusCode) continue;
            using var mailDoc = JsonDocument.Parse(await mailRes.Content.ReadAsStringAsync());
            var messages = mailDoc.RootElement.GetProperty("messages");
            if (messages.ValueKind == JsonValueKind.Null || messages.GetArrayLength() == 0) continue;
            var msgId = messages[0].GetProperty("ID").GetString()!;
            var msgRes = await MailpitClient.GetAsync($"/api/v1/message/{msgId}");
            if (!msgRes.IsSuccessStatusCode) continue;
            using var msgDoc = JsonDocument.Parse(await msgRes.Content.ReadAsStringAsync());
            var body = msgDoc.RootElement.GetProperty("Text").GetString() ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(body, @"code is: (\d+)");
            if (match.Success) { code = match.Groups[1].Value; break; }
        }
        Assert.False(string.IsNullOrEmpty(code), "Did not receive 2FA SMS code in Mailpit");

        // Accept valid code
        var validResponse = await Client.PostAsJsonAsync("/Account/LoginTwoFactor", new { UserId = Guid.Parse(userId), Code = code, Provider = "Phone" });
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        using var validDoc = JsonDocument.Parse(await validResponse.Content.ReadAsStringAsync());
        var newJwt = validDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newJwt));
    }

    [Fact]
    public async Task Save_And_Restore_Theme()
    {
        // Arrange — register and log in
        var email = $"test_{Guid.NewGuid()}@example.com";
        var jwt = await RegisterVerifyEmailAndLogin(email);
        var authHeader = new AuthenticationHeaderValue("Bearer", jwt);

        // Act — save theme to "dark"
        var saveRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/UpdateTheme");
        saveRequest.Headers.Authorization = authHeader;
        saveRequest.Content = JsonContent.Create(new { Theme = "dark" });
        var saveResponse = await Client.SendAsync(saveRequest);

        // Assert save succeeded
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        // Act — log in again
        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Assert login response includes the saved theme
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var theme = loginDoc.RootElement.GetProperty("theme").GetString();
        Assert.Equal("dark", theme);

        // Act — save an invalid theme
        var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/UpdateTheme");
        invalidRequest.Headers.Authorization = authHeader;
        invalidRequest.Content = JsonContent.Create(new { Theme = "neon" });
        var invalidResponse = await Client.SendAsync(invalidRequest);

        // Assert invalid theme is rejected
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    private async Task<string> LoginAsAdmin()
    {
        using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
        var adminUser = await userManager.FindByEmailAsync("admin@example.com");
        return await jwtService.GenerateToken(adminUser!);
    }

    [Fact]
    public async Task Admin_User_Management()
    {
        // Arrange — log in as seeded admin
        var adminJwt = await LoginAsAdmin();
        var adminAuth = new AuthenticationHeaderValue("Bearer", adminJwt);

        // Assert — non-admin is rejected
        var nonAdminJwt = await RegisterVerifyEmailAndLogin($"test_{Guid.NewGuid()}@example.com");
        var nonAdminRequest = new HttpRequestMessage(HttpMethod.Get, "/User");
        nonAdminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nonAdminJwt);
        var nonAdminResponse = await Client.SendAsync(nonAdminRequest);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        // Act — admin lists users
        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/User");
        listRequest.Headers.Authorization = adminAuth;
        var listResponse = await Client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.True(listDoc.RootElement.GetArrayLength() > 0);

        // Act — admin creates a user
        var newEmail = $"admin_created_{Guid.NewGuid()}@example.com";
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/User");
        createRequest.Headers.Authorization = adminAuth;
        createRequest.Content = JsonContent.Create(new
        {
            Email = newEmail,
            Password = "NewUser123!",
            FirstName = "Created",
            LastName = "ByAdmin"
        });
        var createResponse = await Client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var createdId = createDoc.RootElement.GetProperty("id").GetString()!;
        Assert.True(createDoc.RootElement.GetProperty("isActive").GetBoolean());

        // Act — new user can log in immediately (email auto-confirmed by admin create)
        var newUserLoginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = newEmail, Password = "NewUser123!" });
        Assert.Equal(HttpStatusCode.OK, newUserLoginResponse.StatusCode);

        // Act — admin edits the user
        var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/User/{createdId}");
        updateRequest.Headers.Authorization = adminAuth;
        updateRequest.Content = JsonContent.Create(new { FirstName = "Updated", LastName = "Name", Phone = "9876543210" });
        var updateResponse = await Client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Act — admin deactivates the user
        var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/User/{createdId}/Deactivate");
        deactivateRequest.Headers.Authorization = adminAuth;
        var deactivateResponse = await Client.SendAsync(deactivateRequest);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        // Assert — deactivated user cannot log in
        var lockedLoginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = newEmail, Password = "NewUser123!" });
        Assert.Equal(HttpStatusCode.BadRequest, lockedLoginResponse.StatusCode);

        // Act — admin reactivates the user
        var activateRequest = new HttpRequestMessage(HttpMethod.Post, $"/User/{createdId}/Activate");
        activateRequest.Headers.Authorization = adminAuth;
        var activateResponse = await Client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        // Assert — reactivated user can log in again
        var reactivatedLoginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = newEmail, Password = "NewUser123!" });
        Assert.Equal(HttpStatusCode.OK, reactivatedLoginResponse.StatusCode);
    }

    [Fact]
    public async Task Get_User_Stats_Returns_Active_And_Inactive_Counts()
    {
        // Arrange — non-admin should be rejected
        var nonAdminJwt = await RegisterVerifyEmailAndLogin($"test_{Guid.NewGuid()}@example.com");
        var nonAdminRequest = new HttpRequestMessage(HttpMethod.Get, "/User/stats");
        nonAdminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nonAdminJwt);
        var nonAdminResponse = await Client.SendAsync(nonAdminRequest);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminResponse.StatusCode);

        // Arrange — admin
        var adminJwt = await LoginAsAdmin();
        var adminAuth = new AuthenticationHeaderValue("Bearer", adminJwt);

        // Act — fetch baseline stats
        var statsRequest = new HttpRequestMessage(HttpMethod.Get, "/User/stats");
        statsRequest.Headers.Authorization = adminAuth;
        var statsResponse = await Client.SendAsync(statsRequest);
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);

        using var statsDoc = JsonDocument.Parse(await statsResponse.Content.ReadAsStringAsync());
        var baselineActive = statsDoc.RootElement.GetProperty("active").GetInt32();
        var baselineInactive = statsDoc.RootElement.GetProperty("inactive").GetInt32();
        Assert.True(baselineActive >= 0);
        Assert.True(baselineInactive >= 0);

        // Arrange — admin creates a new active user
        var newEmail = $"stats_test_{Guid.NewGuid()}@example.com";
        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/User");
        createRequest.Headers.Authorization = adminAuth;
        createRequest.Content = JsonContent.Create(new { Email = newEmail, Password = "Stats123!", FirstName = "Stats", LastName = "Test" });
        var createResponse = await Client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var createdId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Assert — active count increased by 1
        var afterCreateRequest = new HttpRequestMessage(HttpMethod.Get, "/User/stats");
        afterCreateRequest.Headers.Authorization = adminAuth;
        var afterCreateResponse = await Client.SendAsync(afterCreateRequest);
        using var afterCreateDoc = JsonDocument.Parse(await afterCreateResponse.Content.ReadAsStringAsync());
        Assert.Equal(baselineActive + 1, afterCreateDoc.RootElement.GetProperty("active").GetInt32());
        Assert.Equal(baselineInactive, afterCreateDoc.RootElement.GetProperty("inactive").GetInt32());

        // Act — deactivate the user
        var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/User/{createdId}/Deactivate");
        deactivateRequest.Headers.Authorization = adminAuth;
        await Client.SendAsync(deactivateRequest);

        // Assert — active count returns to baseline, inactive increases by 1
        var afterDeactivateRequest = new HttpRequestMessage(HttpMethod.Get, "/User/stats");
        afterDeactivateRequest.Headers.Authorization = adminAuth;
        var afterDeactivateResponse = await Client.SendAsync(afterDeactivateRequest);
        using var afterDeactivateDoc = JsonDocument.Parse(await afterDeactivateResponse.Content.ReadAsStringAsync());
        Assert.Equal(baselineActive, afterDeactivateDoc.RootElement.GetProperty("active").GetInt32());
        Assert.Equal(baselineInactive + 1, afterDeactivateDoc.RootElement.GetProperty("inactive").GetInt32());
    }

    [Fact]
    public async Task Register_Duplicate_Email_Returns_BadRequest()
    {
        // Arrange — register once successfully
        var email = $"test_{Guid.NewGuid()}@example.com";
        await DeleteMailpitMessagesForEmail(email);
        var request = new { Email = email, Password = "Password123!", FirstName = "Test", LastName = "User", Phone = "1234567890" };
        var first = await Client.PostAsJsonAsync("/Account/Register", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act — register again with the same email
        var second = await Client.PostAsJsonAsync("/Account/Register", request);

        // Assert — duplicate is rejected with validation errors
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("Duplicate", body);
    }

    [Fact]
    public async Task VerifyEmail_Invalid_Token_Returns_BadRequest()
    {
        // Arrange — register a fresh account
        var email = $"test_{Guid.NewGuid()}@example.com";
        await DeleteMailpitMessagesForEmail(email);
        await Client.PostAsJsonAsync("/Account/Register", new { Email = email, Password = "Password123!", FirstName = "Test", LastName = "User", Phone = "1234567890" });

        // Act — verify with a bogus token
        var response = await Client.PostAsJsonAsync("/Account/VerifyEmail", new { Email = email, Token = "not-a-valid-token" });

        // Assert — rejected
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_Unknown_User_Returns_BadRequest()
    {
        // Act — verify email for a user that does not exist
        var response = await Client.PostAsJsonAsync("/Account/VerifyEmail",
            new { Email = $"nobody_{Guid.NewGuid()}@example.com", Token = "whatever" });

        // Assert — UserNotFound surfaces as a validation error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Wrong_Password_Returns_BadRequest()
    {
        // Arrange — a verified account
        var email = $"test_{Guid.NewGuid()}@example.com";
        await RegisterVerifyEmailAndLogin(email);

        // Act — log in with the wrong password
        var response = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = "WrongPassword123!" });

        // Assert — invalid login attempt
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Nonexistent_User_Returns_BadRequest()
    {
        // Act — log in as a user that was never registered
        var response = await Client.PostAsJsonAsync("/Account/Login",
            new { Email = $"nobody_{Guid.NewGuid()}@example.com", Password = "Password123!" });

        // Assert — invalid login attempt (no user enumeration difference)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_Phone_With_Valid_And_Invalid_Token()
    {
        // Arrange — register, verify email, log in
        var email = $"test_{Guid.NewGuid()}@example.com";
        var jwt = await RegisterVerifyEmailAndLogin(email);
        var authHeader = new AuthenticationHeaderValue("Bearer", jwt);

        // Act — request a phone verification code (delivered as SMS to phone@test.com)
        const string phoneEmail = "phone@test.com";
        await DeleteMailpitMessagesForEmail(phoneEmail);
        var verifyPhoneRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/VerifyPhone") { Headers = { Authorization = authHeader } };
        var verifyPhoneResponse = await Client.SendAsync(verifyPhoneRequest);
        Assert.True(verifyPhoneResponse.IsSuccessStatusCode);

        // Extract the code from Mailpit
        var code = await WaitForVerificationCode(phoneEmail);
        Assert.False(string.IsNullOrEmpty(code), "Did not receive phone verification code in Mailpit");

        // Act — confirm with an invalid token first
        var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/ConfirmPhone")
        {
            Headers = { Authorization = authHeader },
            Content = JsonContent.Create(new { Token = "000000" })
        };
        var invalidResponse = await Client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        // Act — confirm with the valid token
        var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/ConfirmPhone")
        {
            Headers = { Authorization = authHeader },
            Content = JsonContent.Create(new { Token = code })
        };
        var confirmResponse = await Client.SendAsync(confirmRequest);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
    }

    [Fact]
    public async Task SendTwoFactorToken_Nonexistent_User_Returns_NotFound()
    {
        // Act — request a token for a user id that does not exist
        var response = await Client.PostAsJsonAsync("/Account/SendTwoFactorToken",
            new { UserId = Guid.NewGuid(), Provider = "Email" });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LoginTwoFactor_Nonexistent_User_Returns_NotFound()
    {
        // Act — complete 2FA for a user id that does not exist
        var response = await Client.PostAsJsonAsync("/Account/LoginTwoFactor",
            new { UserId = Guid.NewGuid(), Code = "123456", Provider = "Authenticator" });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LoginTwoFactor_Invalid_Provider_Returns_BadRequest()
    {
        // Arrange — a real user with 2FA enabled
        var email = $"test_{Guid.NewGuid()}@example.com";
        await RegisterVerifyLoginAndEnableTwoFactor(email);
        var loginResponse = await Client.PostAsJsonAsync("/Account/Login", new { Email = email, Password = "Password123!" });
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var userId = loginDoc.RootElement.GetProperty("userId").GetString()!;

        // Act — submit an unrecognised provider (hits the switch `_ => false` branch)
        var response = await Client.PostAsJsonAsync("/Account/LoginTwoFactor",
            new { UserId = Guid.Parse(userId), Code = "123456", Provider = "CarrierPigeon" });

        // Assert — treated as an invalid code
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Microsoft_Sso_Login_Redirects_To_Authorize_Endpoint()
    {
        using var client = CreateNonRedirectingClient();

        // Act
        var response = await client.GetAsync("/Account/sso/microsoft");

        // Assert — 302 to the Microsoft authorize endpoint with an OAuth code flow + state
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://login.microsoftonline.com/", location);
        Assert.Contains("/oauth2/v2.0/authorize", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("state=", location);

        // Assert — an sso_state cookie was set to match against on callback
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("sso_state="));
    }

    [Fact]
    public async Task Google_Sso_Login_Redirects_To_Authorize_Endpoint()
    {
        using var client = CreateNonRedirectingClient();

        // Act
        var response = await client.GetAsync("/Account/sso/google");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("state=", location);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("sso_state="));
    }

    [Fact]
    public async Task Sso_Callback_With_Provider_Error_Redirects_To_Login_With_Error()
    {
        using var client = CreateNonRedirectingClient();

        // Act — provider redirected back with an error
        var response = await client.GetAsync("/Account/sso/microsoft/callback?error=access_denied");

        // Assert — bounced to the frontend login with the error surfaced
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/account/login", location);
        Assert.Contains("sso_error=access_denied", location);
    }

    [Fact]
    public async Task Sso_Callback_Missing_Parameters_Redirects_To_Login_With_Error()
    {
        using var client = CreateNonRedirectingClient();

        // Act — no code/state supplied
        var response = await client.GetAsync("/Account/sso/google/callback");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("sso_error=missing_parameters", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Sso_Callback_Invalid_State_Redirects_To_Login_With_Error()
    {
        using var client = CreateNonRedirectingClient();

        // Act — code + state present but no matching sso_state cookie
        var response = await client.GetAsync("/Account/sso/microsoft/callback?code=dummy-code&state=does-not-match");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("sso_error=invalid_state", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_Create_Duplicate_Email_Returns_BadRequest()
    {
        var adminAuth = new AuthenticationHeaderValue("Bearer", await LoginAsAdmin());
        var email = $"dup_admin_{Guid.NewGuid()}@example.com";

        // Arrange — create once
        var createContent = new { Email = email, Password = "NewUser123!", FirstName = "Dup", LastName = "User" };
        var first = new HttpRequestMessage(HttpMethod.Post, "/User") { Headers = { Authorization = adminAuth }, Content = JsonContent.Create(createContent) };
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(first)).StatusCode);

        // Act — create again with the same email
        var second = new HttpRequestMessage(HttpMethod.Post, "/User") { Headers = { Authorization = adminAuth }, Content = JsonContent.Create(createContent) };
        var response = await Client.SendAsync(second);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Duplicate", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_Update_Nonexistent_User_Returns_BadRequest()
    {
        var adminAuth = new AuthenticationHeaderValue("Bearer", await LoginAsAdmin());

        // Act — update a user id that does not exist
        var request = new HttpRequestMessage(HttpMethod.Put, $"/User/{Guid.NewGuid()}")
        {
            Headers = { Authorization = adminAuth },
            Content = JsonContent.Create(new { FirstName = "Ghost", LastName = "User", Phone = "5555555555" })
        };
        var response = await Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UserNotFound", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_Deactivate_Nonexistent_User_Returns_BadRequest()
    {
        var adminAuth = new AuthenticationHeaderValue("Bearer", await LoginAsAdmin());

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, $"/User/{Guid.NewGuid()}/Deactivate") { Headers = { Authorization = adminAuth } };
        var response = await Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UserNotFound", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task User_Endpoints_Require_Authentication()
    {
        // Act — no bearer token
        var response = await Client.GetAsync("/User");

        // Assert — challenged
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Polls Mailpit for a "Your verification code is: NNNNNN" / "code is: NNNNNN" message and returns the digits.
    private async Task<string?> WaitForVerificationCode(string recipient)
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            var mailRes = await MailpitClient.GetAsync($"/api/v1/search?query=to:{Uri.EscapeDataString(recipient)}");
            if (!mailRes.IsSuccessStatusCode) continue;
            using var mailDoc = JsonDocument.Parse(await mailRes.Content.ReadAsStringAsync());
            var messages = mailDoc.RootElement.GetProperty("messages");
            if (messages.ValueKind == JsonValueKind.Null || messages.GetArrayLength() == 0) continue;
            var msgId = messages[0].GetProperty("ID").GetString()!;
            var msgRes = await MailpitClient.GetAsync($"/api/v1/message/{msgId}");
            if (!msgRes.IsSuccessStatusCode) continue;
            using var msgDoc = JsonDocument.Parse(await msgRes.Content.ReadAsStringAsync());
            var body = msgDoc.RootElement.GetProperty("Text").GetString() ?? "";
            var match = Regex.Match(body, @"code is: (\d+)");
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    // RFC 6238 TOTP — mirrors what ASP.NET Core Auth uses in AuthenticatorTokenProvider
    private static string ComputeTotp(string base32Key)
    {
        var key = Base32Decode(base32Key.ToUpperInvariant().Replace(" ", ""));
        var timestep = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var timestepBytes = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian) Array.Reverse(timestepBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(timestepBytes);
        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24)
                 | ((hash[offset + 1] & 0xFF) << 16)
                 | ((hash[offset + 2] & 0xFF) << 8)
                 | (hash[offset + 3] & 0xFF);
        return (code % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var result = new List<byte>();
        foreach (var c in base32)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) continue;
            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. result];
    }
}

