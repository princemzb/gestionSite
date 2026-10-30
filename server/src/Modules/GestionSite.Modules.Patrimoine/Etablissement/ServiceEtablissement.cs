using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Composants;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Etablissement;

internal sealed record ClassementLu(
    bool Erp, string? Type, IReadOnlyList<string> TypesSecondaires, int? Categorie, int? Effectif, bool LocauxSommeil, bool MonumentHistorique,
    string PermisAvant1997, DateOnly? DerniereVisiteCommission);

internal sealed record BatimentClasse(Guid Id, string Nom, ClassementLu? Classement, uint Version);

internal sealed record EtablissementLu(bool? SalariesPresents, uint Version, IReadOnlyList<BatimentClasse> Batiments);

/// <summary>
/// Classement ERP des bâtiments et données du site pour le préventif (US-12, S12-01) : lus par les comptes internes du
/// site, déclarés par R/D (« Planifier le préventif »), à la version lue. Chaque changement est tracé au journal de
/// l'inventaire, champ par champ, avec la valeur précédente (RG-31).
/// </summary>
internal sealed class ServiceEtablissement(PatrimoineDbContext context, IAutorisations autorisations, CompteCourant compte, TimeProvider horloge)
{
    internal async Task<EtablissementLu> LireAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var site = await context.Etablissements.AsNoTracking().Where(e => e.SiteId == siteId)
            .Select(e => new { e.SalariesPresents, Version = EF.Property<uint>(e, "xmin") }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var batiments = await context.Batiments.AsNoTracking().Where(b => b.SiteId == siteId).OrderBy(b => b.Nom)
            .Select(b => new { Batiment = b, Version = EF.Property<uint>(b, "xmin") }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new EtablissementLu(
            site?.SalariesPresents, site?.Version ?? 0,
            [.. batiments.Select(b => new BatimentClasse(b.Batiment.Id, b.Batiment.Nom, Lire(b.Batiment.Classement), b.Version))]);
    }

    /// <summary>Classement d'un bâtiment, à la version lue ; sans changement, rien n'est écrit.</summary>
    internal async Task ClasserAsync(Guid organisationId, Guid siteId, Guid batimentId, DemandeClassement demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var batiment = await context.Batiments.SingleOrDefaultAsync(b => b.Id == batimentId && b.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new ElementIntrouvableException();
        context.Entry(batiment).Property<uint>("xmin").OriginalValue = SaisieComposant.Version(demande.Version);
        var maintenant = horloge.GetUtcNow();
        var classement = ClassementErp.Valider(
            demande.Erp, Facultatif<TypeErp>(demande.Type, "type"),
            demande.TypesSecondaires?.Select(t => SaisieComposant.Valeur<TypeErp>(t, "typesSecondaires")).ToList(),
            demande.Categorie, demande.Effectif, demande.LocauxSommeil, demande.MonumentHistorique,
            Facultatif<ReponseConnue>(demande.PermisAvant1997, "permisAvant1997"), demande.DerniereVisiteCommission, maintenant);
        var avant = batiment.Classement?.Releve() ?? Vide(classement.Releve());
        var apres = classement.Releve();
        var changes = apres.Keys.Where(champ => !Equals(avant[champ], apres[champ])).ToList();
        if (changes.Count == 0)
        {
            return;
        }

        batiment.Classer(classement);
        context.Tracer(compte.Id, maintenant, siteId, CibleInventaire.Batiment, batiment.Id, NatureEvenementInventaire.Modification,
            changes.ToDictionary(champ => champ, champ => avant[champ]), changes.ToDictionary(champ => champ, champ => apres[champ]));
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Salariés présents sur le site, à la version lue (0 : jamais déclaré) ; sans changement, rien n'est écrit.</summary>
    internal async Task DeclarerAsync(Guid organisationId, Guid siteId, DemandeEtablissement demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var salaries = demande.SalariesPresents ?? throw new SaisieInvalideException("salariesPresents", "Indiquer si des salariés travaillent sur le site.");
        var version = SaisieComposant.Version(demande.Version);
        var maintenant = horloge.GetUtcNow();
        var etablissement = await context.Etablissements.SingleOrDefaultAsync(e => e.SiteId == siteId, cancellationToken).ConfigureAwait(false);
        if ((etablissement is null) != (version == 0))
        {
            throw new ModifieEntreTempsException();
        }

        if (etablissement is null)
        {
            context.Etablissements.Add(new EtablissementSite(siteId, context.OrganisationRequise, salaries, maintenant));
        }
        else
        {
            context.Entry(etablissement).Property<uint>("xmin").OriginalValue = version;
            if (etablissement.SalariesPresents == salaries)
            {
                return;
            }

            etablissement.Declarer(salaries, maintenant);
        }

        context.Tracer(compte.Id, maintenant, siteId, CibleInventaire.Site, siteId, NatureEvenementInventaire.Modification,
            new { salariesPresents = etablissement is null ? (bool?)null : !salaries }, new { salariesPresents = salaries });
        try
        {
            await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException premiere) when (etablissement is null && premiere.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
        })
        {
            throw new ModifieEntreTempsException(); // première déclaration enregistrée en parallèle
        }
    }

    internal static ClassementLu? Lire(ClassementErp? c) => c is null
        ? null
        : new ClassementLu(
            c.Erp, c.Type?.ToString(), [.. c.TypesSecondaires.Select(t => t.ToString())], c.Categorie, c.Effectif, c.LocauxSommeil, c.MonumentHistorique,
            c.PermisAvant1997.ToString(), c.DerniereVisiteCommission);

    private static Dictionary<string, object?> Vide(Dictionary<string, object?> champs) => champs.ToDictionary(c => c.Key, _ => (object?)null);

    private static T? Facultatif<T>(string? texte, string champ) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(texte) ? null : SaisieComposant.Valeur<T>(texte, champ);
}
