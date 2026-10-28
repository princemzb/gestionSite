using System.IdentityModel.Tokens.Jwt;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GestionSite.Modules.Identite.Authentification;

internal static class AuthentificationServices
{
    internal static IServiceCollection AddAuthentificationIdentite(this IServiceCollection services)
    {
        services.AddOptions<AuthentificationOptions>()
            .BindConfiguration(AuthentificationOptions.Section)
            .ValidateDataAnnotations()
            .Validate(o => o.CleValide(), "Authentification:CleSignature doit être une clé base64 d'au moins 32 octets.")
            .Validate(o => o.DureeJetonAcces > TimeSpan.Zero && o.DureeJetonRenouvellement > o.DureeJetonAcces,
                "Durées de jetons incohérentes.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        // Valeurs par défaut proposées par ADR-10.
        services.AddIdentityCore<Compte>(identite =>
            {
                identite.User.RequireUniqueEmail = true;
                // Longueur seule (ADR-10) : pas de règles de composition, qui poussent aux mots de passe prévisibles.
                identite.Password.RequiredLength = 12;
                identite.Password.RequireDigit = false;
                identite.Password.RequireLowercase = false;
                identite.Password.RequireUppercase = false;
                identite.Password.RequireNonAlphanumeric = false;
                identite.Lockout.AllowedForNewUsers = true;
                identite.Lockout.MaxFailedAccessAttempts = 5;
                identite.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<IdentiteDbContext>();
        services.AddScoped<ServiceAuthentification>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthentificationOptions>>((jwt, reglages) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = reglages.Value.Emetteur,
                    ValidAudience = reglages.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(reglages.Value.Cle()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                jwt.Events = new JwtBearerEvents { OnTokenValidated = DefinirCompteCourant };
            });
        services.AddAuthorization();
        return services;
    }

    // Le compte du jeton devient le compte de la requête (politiques RLS « par compte »).
    private static Task DefinirCompteCourant(TokenValidatedContext context)
    {
        if (Guid.TryParse(context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var compteId) && compteId != Guid.Empty)
        {
            context.HttpContext.RequestServices.GetRequiredService<CompteCourant>().Definir(compteId);
        }
        else
        {
            context.Fail("Jeton sans compte.");
        }

        return Task.CompletedTask;
    }
}
