using Microsoft.AspNetCore.Identity;
using System.Net.Mail;
using System.Reflection;
using Zuhid.Auth.Base;
using Zuhid.Auth.Entities;

namespace Zuhid.Auth;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var appSetting = new AppSetting(builder.Configuration);
        builder.Services
            .AddIdentity<User, Role>()
            .AddEntityFrameworkStores<AuthContext>()
            .AddDefaultTokenProviders();
        builder.AddServices(appSetting);
        builder.AddScopedByConvention(Assembly.GetAssembly(typeof(AuthContext))!, [
            "Composer",
            "Mapper",
            "Repository",
            "Service",
            "Validator",
            "Client"
        ]);
        builder.AddPostgres<AuthContext>(appSetting.ConnectionStrings.Auth);
        builder.Services.AddTransient(_ => new SmtpClient("localhost", 1025));
        builder.Services.AddHttpClient();
        // builder.Services.AddSingleton(new SmtpClient(appSetting.Smtp.Host, appSetting.Smtp.Port));
        // builder.Services.AddScoped<NotificationService>();

        var app = builder.BuildServices(appSetting);
        app.Run();
    }
}
