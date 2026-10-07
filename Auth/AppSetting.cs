using Zuhid.Auth.Base;

namespace Zuhid.Auth;

public class AppSetting : SettingBase
{
    public string AppUrl { get; set; } = default!;
    public ConnectionString ConnectionStrings { get; set; } = default!;
    public SsoConfig Sso { get; set; } = default!;

    public class SsoConfig
    {
        public string FrontendUrl { get; set; } = default!;
        public MicrosoftConfig Microsoft { get; set; } = default!;

        public class MicrosoftConfig
        {
            public string TenantId { get; set; } = default!;
            public string ClientId { get; set; } = default!;
            public string ClientSecret { get; set; } = default!;
            public string RedirectUri { get; set; } = default!;
        }

        public GoogleConfig Google { get; set; } = default!;

        public class GoogleConfig
        {
            public string ClientId { get; set; } = default!;
            public string ClientSecret { get; set; } = default!;
            public string RedirectUri { get; set; } = default!;
        }
    };
    public class ConnectionString
    {
        public string Auth { get; set; } = default!;
        public string Log { get; set; } = default!;
    }

    public AppSetting(IConfiguration configuration) : base(configuration)
    {
        ConnectionStrings = new ConnectionString
        {
            Auth = ReplaceCredential(configuration, "Auth"),
            Log = ReplaceCredential(configuration, "Log"),
        };
    }

    private static string ReplaceCredential(IConfiguration configuration, string connString)
    {
        return configuration.GetConnectionString(connString)!
          .Replace("[postgres_credential]", configuration.GetValue<string>("postgres_credential"), StringComparison.Ordinal);
    }
}
