using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>Secrets à usage unique (32 octets aléatoires) : seule l'empreinte SHA-256 est conservée.</summary>
internal static class SecretsAleatoires
{
    internal const int Taille = 32;

    internal static (string Secret, byte[] Empreinte) Nouveau()
    {
        var octets = RandomNumberGenerator.GetBytes(Taille);
        return (Base64UrlEncoder.Encode(octets), SHA256.HashData(octets));
    }

    /// <summary>Empreinte d'un secret présenté, ou null s'il est mal formé.</summary>
    internal static byte[]? Empreinte(string? secret)
    {
        try
        {
            var octets = Base64UrlEncoder.DecodeBytes(secret ?? string.Empty);
            return octets.Length == Taille ? SHA256.HashData(octets) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
