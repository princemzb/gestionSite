using GestionSite.Modules.Patrimoine.Composants;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel.Photos;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Evaluations;

/// <summary>
/// Évaluation d'un composant demandée par une inspection (US-12, décision du 30/10) : même enregistrement qu'une
/// évaluation directe (US-11), sans proposition de dossier (le constat crée déjà le dossier, RG-09). Les refus propres à
/// l'inventaire sont rendus au module appelant avec un code stable.
/// </summary>
internal sealed class EvaluationsInventaire(PatrimoineDbContext context, ServiceEvaluations evaluations) : IEvaluationsInventaire
{
    public async Task<EvaluationInventaire> EvaluerAsync(Guid siteId, Guid composantId, SaisieEvaluationInventaire saisie, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        try
        {
            var composant = await context.Composants.SingleOrDefaultAsync(c => c.Id == composantId && c.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                ?? throw new ElementIntrouvableException();
            var etat = string.IsNullOrWhiteSpace(saisie.Etat) ? (EtatComposant?)null : SaisieComposant.Valeur<EtatComposant>(saisie.Etat, "etat");
            var repartition = saisie.Repartition is { } r ? new Repartition(r.Bon, r.Satisfaisant, r.Degrade, r.Mauvais, r.Critique) : null;
            var evaluation = await evaluations.EnregistrerAsync(
                siteId, composant,
                new SaisieEvaluation(saisie.CommandeId, saisie.GrilleVersion, saisie.Constats, saisie.Mesures, etat, repartition, saisie.Remarque, saisie.Photos.Count),
                [.. saisie.Photos.Select(p => new PhotoRecue(p))], saisie.EvaluateurCompteId, cancellationToken).ConfigureAwait(false);
            return new EvaluationInventaire(evaluation.Id, Echelle.Arrondie(evaluation.Note), Echelle.Libelle(evaluation.Note).ToString());
        }
        catch (ElementIntrouvableException)
        {
            throw new RefusInventaireException("composant_introuvable", "Composant introuvable sur ce site.");
        }
        catch (ComposantRetireException retire)
        {
            throw new RefusInventaireException("composant_retire", retire.Message);
        }
        catch (GrilleModifieeException modifiee)
        {
            throw new RefusInventaireException("grille_modifiee", modifiee.Message);
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            throw new RefusInventaireException("commande_reutilisee", reutilisee.Message);
        }
        catch (ModifieEntreTempsException modifie)
        {
            throw new RefusInventaireException("modifie_entre_temps", modifie.Message);
        }
    }
}
