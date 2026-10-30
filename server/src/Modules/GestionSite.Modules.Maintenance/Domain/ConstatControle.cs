using System.Globalization;
using System.Text;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>
/// Constat d'un contrôle (US-12, S12-05, RG-09) : un point de grille en défaut ou une observation d'un rapport, relié au
/// dossier qu'il a ouvert ou au dossier ouvert auquel il a été rattaché (même composant et même critère, PROCESSUS).
/// Ajout seul ; la levée des réserves se lit sur l'état de ces dossiers.
/// </summary>
internal sealed class ConstatControle : IAppartientAOrganisation
{
    internal const int LibelleMax = 300;
    internal const int CleMax = 400;

    /// <summary>Rang des observations : après les points de la grille (1 à 15).</summary>
    internal const int RangObservations = 100;

    private ConstatControle()
    {
    }

    internal ConstatControle(Guid id, ControlePreventif controle, int rang, string libelle, string cle, Signalement dossier, bool rattache, bool danger, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = controle.OrganisationId;
        SiteId = controle.SiteId;
        ControleId = controle.Id;
        OccurrenceId = controle.OccurrenceId;
        Rang = rang;
        Libelle = libelle.Length <= LibelleMax ? libelle : libelle[..LibelleMax];
        Cle = cle;
        DossierId = dossier.Id;
        Rattache = rattache;
        Danger = danger;
        CreeLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid ControleId { get; private set; }

    public Guid OccurrenceId { get; private set; }

    public int Rang { get; private set; }

    public string Libelle { get; private set; } = string.Empty;

    public string Cle { get; private set; } = string.Empty;

    public Guid DossierId { get; private set; }

    /// <summary>Rattaché à un dossier ouvert existant (vrai) ou dossier créé par ce constat (faux).</summary>
    public bool Rattache { get; private set; }

    public bool Danger { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}

/// <summary>
/// Clé d'un critère (anti-doublon, PROCESSUS « Inspection ») : corps d'état et texte normalisé d'un point de grille, ou
/// libellé normalisé d'une observation. Normalisé : sans accents ni casse, ponctuation et espaces réduits. Une
/// reformulation du point par R/D donne une autre clé (nouveau dossier) : la détection de doublons d'US-09 reste là.
/// </summary>
internal static class CleConstat
{
    internal static string Point(string? corpsEtat, string texte) => Borner($"point:{corpsEtat ?? "-"}:{Normaliser(texte)}");

    internal static string Observation(string libelle) => Borner($"observation:{Normaliser(libelle)}");

    internal static string Normaliser(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        var espace = false;
        foreach (var c in texte.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (espace && resultat.Length > 0)
                {
                    resultat.Append(' ');
                }

                resultat.Append(char.ToLowerInvariant(c));
                espace = false;
            }
            else
            {
                espace = true;
            }
        }

        return resultat.ToString();
    }

    private static string Borner(string cle) => cle.Length <= ConstatControle.CleMax ? cle : cle[..ConstatControle.CleMax];
}
