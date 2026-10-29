using System.Security.Cryptography;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Code public d'un site (lien et QR du dépôt public, RG-03). Lisible sans organisation, le code étant la seule
/// clé d'accès ; écrit seulement par l'organisation du site (garde applicative et RLS). Un code par site.
/// </summary>
internal sealed class CodeSitePublic
{
    internal const int Longueur = 10;

    // Sans 0/O, 1/I/L ni U : lisible sur une affiche et dicté sans ambiguïté (≈ 49 bits).
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    private CodeSitePublic()
    {
    }

    internal CodeSitePublic(string code, Guid siteId, Guid organisationId, DateTimeOffset creeLe)
    {
        Code = Normaliser(code) ?? throw new ArgumentException("Code de site public invalide.", nameof(code));
        Identifiants.ExigerNonVide(siteId, nameof(siteId));
        Identifiants.ExigerNonVide(organisationId, nameof(organisationId));
        SiteId = siteId;
        OrganisationId = organisationId;
        CreeLe = Horodatage.Valider(creeLe, nameof(creeLe));
    }

    public string Code { get; private set; } = string.Empty;

    public Guid SiteId { get; private set; }

    public Guid OrganisationId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    internal static CodeSitePublic Nouveau(Guid siteId, Guid organisationId, DateTimeOffset creeLe) =>
        new(RandomNumberGenerator.GetString(Alphabet, Longueur), siteId, organisationId, creeLe);

    /// <summary>Code saisi ou lu dans une adresse (casse et tirets ignorés), ou null s'il ne peut pas exister.</summary>
    internal static string? Normaliser(string? saisi)
    {
        var code = (saisi ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        return code.Length == Longueur && code.All(Alphabet.Contains) ? code : null;
    }
}
