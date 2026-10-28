using System.Text.Json;

namespace GestionSite.Modules.Identite.Domain;

internal enum NatureHabilitation
{
    Ouverture,
    Creation,
    Revision,
    Revocation,
    Acceptation,
    Refus,
}

/// <summary>Rôle tel qu'enregistré dans le journal (instantané, indépendant des changements ultérieurs).</summary>
internal sealed record RoleJournalise(string Role, Guid? SiteId);

/// <summary>
/// Entrée du journal des habilitations (RG-31) : qui, quand, pourquoi, et les rôles avant et après.
/// Ajout seulement : le rôle applicatif n'a ni UPDATE ni DELETE sur cette table.
/// </summary>
internal sealed class EvenementHabilitation : IAppartientAOrganisation
{
    // Mêmes règles qu'un nom affiché : une ligne, visible, 200 caractères au plus.
    internal const int MotifMaxLength = NomAffichable.MaxLength;

    private EvenementHabilitation()
    {
    }

    /// <summary>
    /// Seule fabrique d'entrées : les rôles après sont l'état du membre, sauf révocation et refus (aucun).
    /// Révision et révocation exigent un motif (RG-31 : auteur, motif et valeur précédente).
    /// </summary>
    internal static EvenementHabilitation Pour(
        Membre membre, NatureHabilitation nature, Guid? auteurCompteId, DateTimeOffset le, string? motif, IReadOnlyCollection<RoleJournalise>? avant)
    {
        if (nature is NatureHabilitation.Revision or NatureHabilitation.Revocation && string.IsNullOrWhiteSpace(motif))
        {
            throw new ArgumentException("Un motif est requis pour réviser ou révoquer une habilitation.", nameof(motif));
        }

        var apres = nature is NatureHabilitation.Revocation or NatureHabilitation.Refus ? null : Instantane(membre);
        return new EvenementHabilitation(membre, nature, auteurCompteId, le, motif, avant, apres);
    }

    private EvenementHabilitation(
        Membre membre, NatureHabilitation nature, Guid? auteurCompteId, DateTimeOffset le, string? motif,
        IReadOnlyCollection<RoleJournalise>? avant, IReadOnlyCollection<RoleJournalise>? apres)
    {
        ArgumentNullException.ThrowIfNull(membre);
        Le = Horodatage.Valider(le, nameof(le));
        Id = Guid.CreateVersion7(Le); // ordonné dans le temps : départage et pagination stables
        OrganisationId = membre.OrganisationId;
        MembreId = membre.Id;
        CompteId = membre.CompteId;
        Nature = nature;
        AuteurCompteId = auteurCompteId;
        Motif = NettoyerMotif(motif);
        RolesAvant = avant is null ? null : JsonSerializer.Serialize(avant);
        RolesApres = apres is null ? null : JsonSerializer.Serialize(apres);
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid MembreId { get; private set; }

    /// <summary>Compte concerné.</summary>
    public Guid CompteId { get; private set; }

    public NatureHabilitation Nature { get; private set; }

    /// <summary>Compte auteur ; null pour la commande d'exploitation de la plateforme.</summary>
    public Guid? AuteurCompteId { get; private set; }

    public DateTimeOffset Le { get; private set; }

    public string? Motif { get; private set; }

    /// <summary>Rôles avant l'action (JSON), null s'il n'y en avait pas.</summary>
    public string? RolesAvant { get; private set; }

    public string? RolesApres { get; private set; }

    // Facultatif ; vide : pas de motif.
    private static string? NettoyerMotif(string? motif) =>
        string.IsNullOrWhiteSpace(motif) ? null : NomAffichable.Valider(motif, nameof(motif));

    internal static IReadOnlyCollection<RoleJournalise> Instantane(Membre membre) =>
        [.. membre.Roles.OrderBy(r => r.Role).ThenBy(r => r.SiteId).Select(r => new RoleJournalise(r.Role.ToString(), r.SiteId))];
}
