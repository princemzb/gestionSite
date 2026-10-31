using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Patrimoine.PlansBatiment;

internal sealed record PlanLu(Guid Id, Guid BatimentId, Guid? NiveauId, int Version, DateTimeOffset? ModifieLe, string? Auteur);

/// <summary>
/// Version lue d'un plan : 0 tant que rien n'est enregistré (contenu vide). Marqueurs obsolètes : composants retirés,
/// changés de zone ou devenus objets de valeur depuis cette version, à retirer de la suivante (RP-21.7) ; ceux d'un objet
/// de valeur ne sont jamais rendus.
/// </summary>
internal sealed record VersionPlanLue(
    Guid PlanId, Guid BatimentId, Guid? NiveauId, int Version, int Schema, DateTimeOffset? Le, string? Auteur, string? Motif, int? RestaureDe,
    ContenuPlan Contenu, IReadOnlyList<AvertissementPlan> Avertissements, IReadOnlyList<Guid> MarqueursObsoletes);

internal sealed record VersionResumee(int Version, DateTimeOffset Le, string? Auteur, string? Motif, int? RestaureDe);

internal sealed record VersionEnregistree(int Version, IReadOnlyList<AvertissementPlan> Avertissements);

/// <summary>Numéro, auteur et date de la dernière version, sans son contenu.</summary>
internal sealed record EnTeteVersion(int Version, Guid AuteurCompteId, DateTimeOffset CreeLe);

/// <summary>Un plan existe déjà pour ce niveau (ou ce plan masse) : 409 « plan_existant », avec son identifiant.</summary>
internal sealed class PlanExistantException(Guid planId) : Exception("Ce niveau a déjà un plan de bâtiment.")
{
    internal Guid PlanId { get; } = planId;
}

/// <summary>
/// La version lue n'est plus la dernière (409 « version_depassee ») : version gagnante, son auteur et sa date, pour que
/// l'éditeur le dise sans rien écraser (RG-24, RP-21.8).
/// </summary>
internal sealed class VersionPlanDepasseeException(int versionCourante, string? auteur, DateTimeOffset? le)
    : Exception("Ce plan a été modifié entre-temps : rechargez-le avant d'enregistrer.")
{
    internal int VersionCourante { get; } = versionCourante;

    internal string? Auteur { get; } = auteur;

    internal DateTimeOffset? Le { get; } = le;
}

/// <summary>Identifiant d'envoi déjà utilisé pour un autre plan ou par un autre compte (409 « commande_reutilisee »).</summary>
internal sealed class EnvoiPlanReutiliseException() : Exception("Cet identifiant d'envoi a déjà servi pour un autre enregistrement.");

/// <summary>
/// Plans de bâtiment (US-21, S21-02) : lecture et historique pour les comptes internes du site (ConsulterSite), création,
/// enregistrement et restauration pour R/D (ModifierPlans ; droit nominatif à venir, S21-03). Chaque enregistrement crée
/// une version immuable à partir de la version lue ; un renvoi du même enregistrement (même identifiant d'envoi) rend la
/// même version.
/// </summary>
internal sealed class ServicePlansBatiment(
    PatrimoineDbContext context, IAutorisations autorisations, IAnnuaireMembres annuaire, CompteCourant compte, TimeProvider horloge)
{
    internal const int HistoriqueParDefaut = 50;
    internal const int HistoriqueMax = 200;
    internal const string MarqueurRetire = "marqueur_retire";

    internal async Task<IReadOnlyList<PlanLu>> ListerAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var plans = await context.PlansBatiment.AsNoTracking().Where(p => p.SiteId == siteId).OrderBy(p => p.CreeLe).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var dernieres = (await context.VersionsPlanBatiment.AsNoTracking()
                .Where(v => v.SiteId == siteId && !context.VersionsPlanBatiment.Any(s => s.PlanId == v.PlanId && s.Version > v.Version))
                .Select(v => new { v.PlanId, v.Version, v.CreeLe, v.AuteurCompteId })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.PlanId);
        var noms = await annuaire.NomsAsync([.. dernieres.Values.Select(v => v.AuteurCompteId).Distinct()], cancellationToken).ConfigureAwait(false);
        return [.. plans.Select(p => dernieres.TryGetValue(p.Id, out var v)
            ? new PlanLu(p.Id, p.BatimentId, p.NiveauId, v.Version, v.CreeLe, noms.GetValueOrDefault(v.AuteurCompteId))
            : new PlanLu(p.Id, p.BatimentId, p.NiveauId, 0, null, null))];
    }

    /// <summary>
    /// Plan d'un niveau du bâtiment, ou plan masse (niveau absent) d'un bâtiment sans niveau ; sans version tant que rien
    /// n'est enregistré.
    /// </summary>
    internal async Task<Guid> CreerAsync(Guid organisationId, Guid siteId, Guid? batimentId, Guid? niveauId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierPlans, cancellationToken).ConfigureAwait(false);
        var idBatiment = batimentId ?? throw new SaisieInvalideException("batimentId", "Bâtiment obligatoire.");
        var batiment = await context.Batiments.AsNoTracking().SingleOrDefaultAsync(b => b.Id == idBatiment && b.SiteId == siteId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ElementIntrouvableException();
        Niveau? niveau = null;
        if (niveauId is { } id)
        {
            niveau = await context.Niveaux.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id && n.SiteId == siteId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ElementIntrouvableException();
            if (niveau.BatimentId != batiment.Id)
            {
                throw new SaisieInvalideException("niveauId", "Ce niveau n'appartient pas au bâtiment.");
            }
        }
        else if (await context.Niveaux.AnyAsync(n => n.BatimentId == batiment.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new SaisieInvalideException("niveauId", "Un bâtiment à niveaux a un plan par niveau, pas de plan masse.");
        }

        if (await ExistantAsync(batiment.Id, niveau?.Id, cancellationToken).ConfigureAwait(false) is { } existant)
        {
            throw new PlanExistantException(existant);
        }

        var maintenant = horloge.GetUtcNow();
        var plan = new PlanBatiment(Guid.CreateVersion7(maintenant), batiment, niveau, compte.Id, maintenant);
        context.PlansBatiment.Add(plan);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (Violation(doublon, PlanBatiment.ContrainteNiveau))
        {
            context.ChangeTracker.Clear();
            throw new PlanExistantException(await ExistantAsync(batiment.Id, niveau?.Id, cancellationToken).ConfigureAwait(false) ?? Guid.Empty);
        }

        return plan.Id;
    }

    /// <summary>Dernière version du plan (sans version : 0) ou la version demandée.</summary>
    internal async Task<VersionPlanLue> LireAsync(Guid organisationId, Guid siteId, Guid planId, int? version, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        var versions = context.VersionsPlanBatiment.AsNoTracking().Where(v => v.PlanId == plan.Id);
        var lue = version is { } numero
            ? await versions.SingleOrDefaultAsync(v => v.Version == numero, cancellationToken).ConfigureAwait(false) ?? throw new ElementIntrouvableException()
            : await versions.OrderByDescending(v => v.Version).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (lue is null)
        {
            return new VersionPlanLue(
                plan.Id, plan.BatimentId, plan.NiveauId, 0, VersionPlanBatiment.SchemaCourant, null, null, null, null, ContenuPlan.Vide, [], []);
        }

        var contenu = lue.ContenuLu;
        var ids = contenu.Composants.Select(m => m.ComposantId).ToList();
        // Retirés compris : un composant retiré ou introuvable n'est jamais servi s'il est (ou devient) objet de valeur.
        var composants = await context.Composants.AsNoTracking().Where(c => ids.Contains(c.Id) && c.SiteId == siteId)
            .Select(c => new { c.Id, c.ZoneId, c.CorpsEtat, Retire = c.RetireLe != null }).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        var obsoletes = contenu.Composants
            .Where(m => !composants.TryGetValue(m.ComposantId, out var c) || c.Retire || c.ZoneId != m.ZoneId || ValidationPlan.ObjetDeValeur(c.CorpsEtat))
            .Select(m => m.ComposantId).ToList();
        // Fermé par défaut : position servie seulement pour un composant connu du site qui n'est pas un objet de valeur,
        // même dans une ancienne version (reclassé depuis, retiré ensuite ou non).
        contenu = contenu with
        {
            Composants = [.. contenu.Composants.Where(m => composants.TryGetValue(m.ComposantId, out var c) && !ValidationPlan.ObjetDeValeur(c.CorpsEtat))],
        };
        var noms = await annuaire.NomsAsync([lue.AuteurCompteId], cancellationToken).ConfigureAwait(false);
        return new VersionPlanLue(
            plan.Id, plan.BatimentId, plan.NiveauId, lue.Version, lue.Schema, lue.CreeLe, noms.GetValueOrDefault(lue.AuteurCompteId), lue.Motif,
            lue.RestaureDe, contenu, lue.AvertissementsLus, obsoletes);
    }

    /// <summary>Versions du plan, la plus récente d'abord, par pages (<paramref name="avant"/> : numéro exclu).</summary>
    internal async Task<IReadOnlyList<VersionResumee>> HistoriqueAsync(
        Guid organisationId, Guid siteId, Guid planId, int? avant, int? limite, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        var taille = limite ?? HistoriqueParDefaut;
        if (taille is < 1 or > HistoriqueMax)
        {
            throw new SaisieInvalideException("limite", $"De 1 à {HistoriqueMax} versions par page.");
        }

        var versions = await context.VersionsPlanBatiment.AsNoTracking()
            .Where(v => v.PlanId == plan.Id && (avant == null || v.Version < avant)).OrderByDescending(v => v.Version).Take(taille)
            .Select(v => new { v.Version, v.CreeLe, v.AuteurCompteId, v.Motif, v.RestaureDe })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var noms = await annuaire.NomsAsync([.. versions.Select(v => v.AuteurCompteId).Distinct()], cancellationToken).ConfigureAwait(false);
        return [.. versions.Select(v => new VersionResumee(v.Version, v.CreeLe, noms.GetValueOrDefault(v.AuteurCompteId), v.Motif, v.RestaureDe))];
    }

    /// <summary>Nouvelle version à partir de la version lue (0 pour la première), même si le contenu n'a pas changé (RP-21.8).</summary>
    internal async Task<VersionEnregistree> EnregistrerAsync(
        Guid organisationId, Guid siteId, Guid planId, Guid? commandeId, int? versionLue, string? motif, SaisieContenuPlan? contenu,
        CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierPlans, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        var envoi = Envoi(commandeId);
        if (await DejaRecuAsync(plan.Id, envoi, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja; // renvoi reconnu avant les contrôles : une version enregistrée depuis par un autre ne le refuse pas
        }

        var lue = versionLue ?? throw new SaisieInvalideException("version", "Version lue obligatoire.");
        var texte = Motif(motif, obligatoire: false);
        var courante = await CouranteAsync(plan.Id, cancellationToken).ConfigureAwait(false);
        await ExigerCouranteAsync(courante, lue, cancellationToken).ConfigureAwait(false);
        var valide = ValidationPlan.Valider(contenu, await CadreAsync(plan, contenu?.Composants, cancellationToken).ConfigureAwait(false));
        return await AjouterAsync(plan, courante, envoi, valide, texte, restaureDe: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restauration (RP-21.8) : la version demandée est recopiée dans une nouvelle version, motif obligatoire. Les
    /// marqueurs de composants retirés, changés de niveau ou devenus objets de valeur depuis sont retirés (avertissement
    /// « marqueur_retire »).
    /// </summary>
    internal async Task<VersionEnregistree> RestaurerAsync(
        Guid organisationId, Guid siteId, Guid planId, int source, Guid? commandeId, int? versionLue, string? motif, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierPlans, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        var envoi = Envoi(commandeId);
        if (await DejaRecuAsync(plan.Id, envoi, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja;
        }

        var ancienne = await context.VersionsPlanBatiment.AsNoTracking().SingleOrDefaultAsync(v => v.PlanId == plan.Id && v.Version == source, cancellationToken)
            .ConfigureAwait(false) ?? throw new ElementIntrouvableException();

        var lue = versionLue ?? throw new SaisieInvalideException("version", "Version lue obligatoire.");
        var texte = Motif(motif, obligatoire: true);
        var courante = await CouranteAsync(plan.Id, cancellationToken).ConfigureAwait(false);
        await ExigerCouranteAsync(courante, lue, cancellationToken).ConfigureAwait(false);
        var contenu = ancienne.ContenuLu;
        var cadre = await CadreAsync(plan, [.. contenu.Composants.Select(m => new SaisieMarqueur(m.ComposantId, null))], cancellationToken).ConfigureAwait(false);
        var gardes = contenu.Composants.Where(m => cadre.Composants.TryGetValue(m.ComposantId, out var c) && !c.Retire
            && !ValidationPlan.ObjetDeValeur(c.CorpsEtat) && cadre.Zones.TryGetValue(c.ZoneId, out var zone) && zone.NiveauId == plan.NiveauId).ToList();
        var valide = ValidationPlan.Valider(Saisie(contenu with { Composants = gardes }), cadre);
        var retires = contenu.Composants.Except(gardes).Select(m => new AvertissementPlan(MarqueurRetire, m.ComposantId));
        return await AjouterAsync(plan, courante, envoi, valide with { Avertissements = [.. retires, .. valide.Avertissements] }, texte, source, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<VersionEnregistree> AjouterAsync(
        PlanBatiment plan, EnTeteVersion? courante, Guid envoi, PlanValide valide, string? motif, int? restaureDe, CancellationToken cancellationToken)
    {
        var maintenant = horloge.GetUtcNow();
        var auteur = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        var version = new VersionPlanBatiment(
            Guid.CreateVersion7(maintenant), plan, (courante?.Version ?? 0) + 1, envoi, valide.Contenu, valide.Avertissements, motif, restaureDe, auteur, maintenant);
        context.VersionsPlanBatiment.Add(version);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException concurrent) when (Violation(concurrent, VersionPlanBatiment.ContrainteVersion)
            || Violation(concurrent, VersionPlanBatiment.ContrainteCommande))
        {
            // Même envoi reçu en parallèle : même réponse qu'un renvoi. Sinon, une autre version a gagné : 409 qui la nomme.
            context.ChangeTracker.Clear();
            if (await DejaRecuAsync(plan.Id, envoi, cancellationToken).ConfigureAwait(false) is { } deja)
            {
                return deja;
            }

            await ExigerCouranteAsync(await CouranteAsync(plan.Id, cancellationToken).ConfigureAwait(false), version.Version - 1, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        return new VersionEnregistree(version.Version, valide.Avertissements);
    }

    private async Task ExigerCouranteAsync(EnTeteVersion? courante, int lue, CancellationToken cancellationToken)
    {
        var numero = courante?.Version ?? 0;
        if (numero == lue)
        {
            return;
        }

        var auteur = courante is null ? null : (await annuaire.NomsAsync([courante.AuteurCompteId], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(courante.AuteurCompteId);
        throw new VersionPlanDepasseeException(numero, auteur, courante?.CreeLe);
    }

    // Renvoi : même identifiant d'envoi, même plan, même compte → même version ; sinon identifiant réutilisé à tort (409).
    private async Task<VersionEnregistree?> DejaRecuAsync(Guid planId, Guid envoi, CancellationToken cancellationToken)
    {
        var recue = await context.VersionsPlanBatiment.AsNoTracking().Where(v => v.CommandeId == envoi)
            .Select(v => new { v.PlanId, v.AuteurCompteId, v.Version, v.Avertissements }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return recue switch
        {
            null => null,
            _ when recue.PlanId == planId && recue.AuteurCompteId == compte.Id => new VersionEnregistree(
                recue.Version, JsonSerializer.Deserialize<List<AvertissementPlan>>(recue.Avertissements, VersionPlanBatiment.Json) ?? []),
            _ => throw new EnvoiPlanReutiliseException(),
        };
    }

    private async Task<CadrePlan> CadreAsync(PlanBatiment plan, IReadOnlyList<SaisieMarqueur?>? marqueurs, CancellationToken cancellationToken)
    {
        var zones = await context.Zones.AsNoTracking().Where(z => z.BatimentId == plan.BatimentId)
            .Select(z => new ZoneCadre(z.Id, z.NiveauId, z.PartiesCommunes)).ToDictionaryAsync(z => z.Id, cancellationToken).ConfigureAwait(false);
        var niveaux = await context.Niveaux.AsNoTracking().Where(n => n.BatimentId == plan.BatimentId && n.Id != plan.NiveauId)
            .Select(n => n.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = (marqueurs ?? []).Select(m => m?.ComposantId).OfType<Guid>().Distinct().Take(ValidationPlan.MarqueursMax + 1).ToList();
        var composants = await context.Composants.AsNoTracking().Where(c => ids.Contains(c.Id) && c.SiteId == plan.SiteId)
            .Select(c => new ComposantCadre(c.Id, c.ZoneId, c.CorpsEtat, c.RetireLe != null))
            .ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        return new CadrePlan(plan.NiveauId, zones, niveaux.ToHashSet(), composants);
    }

    private Task<EnTeteVersion?> CouranteAsync(Guid planId, CancellationToken cancellationToken) =>
        context.VersionsPlanBatiment.AsNoTracking().Where(v => v.PlanId == planId).OrderByDescending(v => v.Version)
            .Select(v => new EnTeteVersion(v.Version, v.AuteurCompteId, v.CreeLe)).FirstOrDefaultAsync(cancellationToken);

    private async Task<PlanBatiment> PlanAsync(Guid siteId, Guid planId, CancellationToken cancellationToken) =>
        await context.PlansBatiment.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId && p.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new ElementIntrouvableException();

    private async Task<Guid?> ExistantAsync(Guid batimentId, Guid? niveauId, CancellationToken cancellationToken) =>
        await context.PlansBatiment.AsNoTracking().Where(p => p.BatimentId == batimentId && p.NiveauId == niveauId).Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    private static Guid Envoi(Guid? commandeId) => commandeId is { } id && id != Guid.Empty
        ? id
        : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi obligatoire.");

    private static string? Motif(string? motif, bool obligatoire) => string.IsNullOrWhiteSpace(motif)
        ? obligatoire ? throw new SaisieInvalideException("motif", "Motif obligatoire.") : null
        : TexteAffichable.Nettoyer(motif, VersionPlanBatiment.MotifMax)
            ?? throw new SaisieInvalideException("motif", $"Motif : une ligne visible de {VersionPlanBatiment.MotifMax} caractères au plus.");

    // Contenu enregistré remis sous forme de saisie : une restauration repasse par la validation en vigueur.
    private static SaisieContenuPlan Saisie(ContenuPlan contenu) => new(
        contenu.Schema, contenu.Nord, contenu.Altitude, contenu.HauteurSousPlafond,
        [.. contenu.Pieces.Select(p => new SaisiePiece(p.ZoneId, Points(p.Contour)))],
        [.. contenu.Ouvertures.Select(o => new SaisieOuverture(o.Type, Point(o.De), Point(o.A), o.Zones))],
        [.. contenu.Escaliers.Select(e => new SaisieLiaison(e.Type, Points(e.Contour), e.VersNiveauId))],
        [.. contenu.Composants.Select(m => new SaisieMarqueur(m.ComposantId, Point(m.Position)))]);

    private static SaisiePoint Point(PointPlan point) => new(point.X, point.Y);

    private static List<SaisiePoint?> Points(IReadOnlyList<PointPlan> points) => [.. points.Select(p => (SaisiePoint?)Point(p))];

    private static bool Violation(DbUpdateException erreur, string contrainte) =>
        erreur.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation && violation.ConstraintName == contrainte;
}
