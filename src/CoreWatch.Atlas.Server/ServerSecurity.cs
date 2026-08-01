using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

public static class ServerSecurity
{
    public const string AntiforgeryHeaderName = "X-CoreWatch-CSRF";

    public static IServiceCollection AddAtlasServerSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(ServerSecurityOptions.SectionName);
        var settings = section.Get<ServerSecurityOptions>()
            ?? new ServerSecurityOptions();
        services
            .AddOptions<ServerSecurityOptions>()
            .Bind(section)
            .Validate(
                options => !string.IsNullOrWhiteSpace(
                    options.DataProtectionKeyPath),
                "DataProtectionKeyPath must not be empty.")
            .Validate(
                options => options.HstsMaxAgeDays is >= 1 and <= 3650,
                "HstsMaxAgeDays must be between 1 and 3650.")
            .ValidateOnStart();

        var keyPath = Path.GetFullPath(
            Path.IsPathRooted(settings.DataProtectionKeyPath)
                ? settings.DataProtectionKeyPath
                : Path.Combine(
                    environment.ContentRootPath,
                    settings.DataProtectionKeyPath));
        var keyDirectory = Directory.CreateDirectory(keyPath);
        RestrictDirectoryToOwner(keyDirectory.FullName);

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("CoreWatch-Atlas.Server")
            .PersistKeysToFileSystem(keyDirectory);
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        services.AddAntiforgery(
            options =>
            {
                options.HeaderName = AntiforgeryHeaderName;
                options.Cookie.Name = "CoreWatchAtlas.Antiforgery";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SuppressXFrameOptionsHeader = false;
            });
        services.AddHsts(
            options =>
            {
                options.MaxAge = TimeSpan.FromDays(settings.HstsMaxAgeDays);
                options.IncludeSubDomains = true;
            });
        return services;
    }

    public static IApplicationBuilder UseAtlasServerSecurity(
        this WebApplication app)
    {
        var settings =
            app.Services.GetRequiredService<IOptions<ServerSecurityOptions>>().Value;
        if (settings.RequireHttps)
        {
            app.UseHsts();
        }

        return app.Use(
            async (context, next) =>
            {
                AddSecurityHeaders(context.Response.Headers);
                if (settings.RequireHttps
                    && !context.Request.IsHttps
                    && !(settings.AllowLoopbackHttp
                        && IsLoopback(context.Connection.RemoteIpAddress)))
                {
                    context.Response.StatusCode =
                        StatusCodes.Status426UpgradeRequired;
                    await context.Response.WriteAsJsonAsync(
                        new
                        {
                            error = "HTTPS is required.",
                        });
                    return;
                }

                await next(context);
            });
    }

    private static void AddSecurityHeaders(IHeaderDictionary headers)
    {
        headers.ContentSecurityPolicy =
            "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; "
            + "form-action 'self'; object-src 'none'; "
            + "img-src 'self' data:; style-src 'self' 'unsafe-inline'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    }

    private static bool IsLoopback(IPAddress? address) =>
        address is null || IPAddress.IsLoopback(address);

    private static void RestrictDirectoryToOwner(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }
}
// CoreWatch Atlas module: ServerSecurity.
