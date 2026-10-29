using Microsoft.AspNetCore.Http;

namespace GestionSite.SharedKernel;

/// <summary>
/// Saisie refusée d'un champ (422 « donnee_invalide » + champ). Distincte d'ArgumentException : une erreur interne
/// n'est jamais présentée comme une erreur de saisie.
/// </summary>
public sealed class SaisieInvalideException(string champ, string message) : Exception(message)
{
    public string Champ { get; } = champ;
}

/// <summary>Refus d'accès : 404 hors périmètre de l'appelant (rien n'est révélé), 403 site visible mais action non permise.</summary>
public sealed class AccesRefuseException(bool siteVisible) : Exception("Accès refusé.")
{
    public bool SiteVisible { get; } = siteVisible;
}

/// <summary>Traduction HTTP des refus communs aux modules (filtre d'endpoint), et forme de leurs ProblemDetails.</summary>
public static class RefusCommuns
{
    public static async ValueTask<object?> Traduire(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (AccesRefuseException refus)
        {
            return refus.SiteVisible ? Probleme(StatusCodes.Status403Forbidden, "Action non permise.", "interdit") : Results.NotFound();
        }
        catch (SaisieInvalideException invalide)
        {
            return SaisieInvalide(invalide.Champ);
        }
    }

    public static IResult SaisieInvalide(string champ) =>
        Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Donnée invalide.",
            extensions: new Dictionary<string, object?> { ["code"] = "donnee_invalide", ["champ"] = champ });

    public static IResult Probleme(int statut, string titre, string code) =>
        Results.Problem(statusCode: statut, title: titre, extensions: new Dictionary<string, object?> { ["code"] = code });
}
