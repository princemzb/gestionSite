using System.Globalization;

namespace GestionSite.Web.Aide;

/// <summary>
/// Livret PDF du manuel (DOC-02) : imprimé à la construction de l'image (server/Dockerfile, étape « livret ») à partir du même
/// Markdown que l'aide intégrée, puis servi tel quel. Absent en développement tant qu'il n'est pas produit (scripts/manuel).
/// </summary>
internal sealed class LivretAide(string chemin)
{
    internal const string Adresse = "/aide/livret.pdf";

    private string? _libelle;

    internal string Chemin { get; } = chemin;

    /// <summary>
    /// Libellé du lien (« PDF, 4,1 Mo »), ou null si le livret n'est pas disponible. Le livret fait partie de l'image : une fois
    /// trouvé, son libellé est gardé ; absent, il est recherché de nouveau (développement).
    /// </summary>
    internal string? Libelle => _libelle ??= File.Exists(Chemin)
        ? string.Create(CultureInfo.GetCultureInfo("fr-FR"), $"PDF, {new FileInfo(Chemin).Length / 1024d / 1024d:0.0} Mo")
        : null;
}

internal static class AideEndpoints
{
    /// <summary>
    /// Captures du manuel (fictives, publiques comme le manuel : affichées aussi par l'aide des écrans de connexion et
    /// d'activation ; leur adresse porte leur version) et livret PDF, réservé aux comptes connectés comme les pages d'aide.
    /// </summary>
    internal static IEndpointRouteBuilder MapAide(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/aide/images/{nom}", (string nom, ManuelAide manuel, HttpContext contexte) =>
        {
            if (manuel.Image(nom) is not { } octets)
            {
                return Results.NotFound();
            }

            contexte.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(octets, "image/png");
        });
        routes.MapGet(LivretAide.Adresse, (LivretAide livret) => File.Exists(livret.Chemin)
                ? Results.File(livret.Chemin, "application/pdf", "manuel-gestionsite.pdf")
                : Results.NotFound())
            .RequireAuthorization();
        return routes;
    }
}
