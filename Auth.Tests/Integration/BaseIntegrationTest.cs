using Microsoft.AspNetCore.Mvc.Testing;

namespace Zuhid.Auth.Tests.Integration;

public abstract class BaseIntegrationTest(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    protected readonly WebApplicationFactory<Program> Factory = factory;
    protected readonly HttpClient Client = factory.CreateClient();
    protected readonly IServiceProvider Services = factory.Services;

    // A client that does not follow redirects — needed to assert on SSO 302 Location headers
    // instead of chasing them to an external (non-running) frontend URL.
    protected HttpClient CreateNonRedirectingClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
