using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace EntraAuth.AspNetCore;

public static class EntraApiServiceCollectionExtensions
{
    public static IServiceCollection AddEntraApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EntraApiAuthenticationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var entra = BindOptions(configuration);
        var authOptions = new EntraApiAuthenticationOptions();
        configure?.Invoke(authOptions);

        services.AddSingleton(entra);

        if (authOptions.TestSigningKey is not null)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => ConfigureJwtBearer(options, entra, authOptions.TestSigningKey));
        }
        else
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(configuration.GetSection(EntraApiOptions.SectionName));
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                ConfigureJwtBearer(options, entra, testSigningKey: null);
            });
        }

        return services;
    }

    public static IServiceCollection AddEntraApiAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var entra = BindOptions(configuration);
        services.AddSingleton<IAuthorizationHandler, ScopeOrAppRoleHandler>();
        services.AddAuthorization(options =>
        {
            foreach (var (name, policy) in entra.Policies)
            {
                options.AddPolicy(name, builder =>
                {
                    builder.RequireAuthenticatedUser();
                    builder.AddRequirements(new ScopeOrAppRoleRequirement(policy.Scopes, policy.AppRoles));
                });
            }

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static EntraApiOptions BindOptions(IConfiguration configuration)
    {
        var entra = new EntraApiOptions();
        configuration.GetSection(EntraApiOptions.SectionName).Bind(entra);
        return entra;
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions options,
        EntraApiOptions entra,
        SecurityKey? testSigningKey)
    {
        var audiences = entra.ResolvedAudiences();
        var issuers = entra.ResolvedIssuers();
        var tenants = entra.ResolvedTenants();

        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidIssuers = issuers;
        options.TokenValidationParameters.ValidAudiences = audiences;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.RequireSignedTokens = true;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(entra.ClockSkewSeconds);
        options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
        options.TokenValidationParameters.NameClaimType = "oid";
        options.TokenValidationParameters.RoleClaimType = "roles";

        if (testSigningKey is not null)
        {
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters.IssuerSigningKey = testSigningKey;
            options.TokenValidationParameters.TryAllIssuerSigningKeys = false;
        }

        options.Events ??= new JwtBearerEvents();
        var previousValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            if (previousValidated is not null)
            {
                await previousValidated(context);
            }

            var tid = context.Principal?.FindFirst("tid")?.Value
                      ?? context.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
            if (string.IsNullOrWhiteSpace(tid) ||
                !tenants.Contains(tid, StringComparer.OrdinalIgnoreCase))
            {
                context.Fail("The token tenant is not allowed.");
            }
        };

        options.Events.OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate =
                "Bearer error=\"invalid_token\", error_description=\"The access token is missing, invalid, or expired\"";
            return Task.CompletedTask;
        };
    }
}
