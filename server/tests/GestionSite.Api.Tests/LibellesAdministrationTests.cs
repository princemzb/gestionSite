using GestionSite.Web.Administration;
using GestionSite.Web.Session;

namespace GestionSite.Api.Tests;

/// <summary>Libellés des écrans d'administration (US-31) : natures et détails du journal des habilitations, rôles.</summary>
public sealed class LibellesAdministrationTests
{
    private static readonly Guid Eglise = Guid.NewGuid();
    private static readonly Guid Chapelle = Guid.NewGuid();
    private static readonly Dictionary<Guid, string> Sites = new() { [Eglise] = "Église", [Chapelle] = "Chapelle" };

    [Theory]
    [InlineData("Ouverture", "Organisation ouverte")]
    [InlineData("Creation", "Invitation créée")]
    [InlineData("Revision", "Rôles modifiés")]
    [InlineData("Revocation", "Accès révoqué")]
    [InlineData("Acceptation", "Invitation acceptée")]
    [InlineData("Refus", "Invitation refusée")]
    [InlineData("AttributionDroit", "Droit « plans » accordé")]
    [InlineData("RetraitDroit", "Droit « plans » retiré")]
    [InlineData("NouveauLienActivation", "Nouveau lien d'activation")]
    [InlineData("LienReinitialisation", "Lien de réinitialisation créé")]
    [InlineData("MotDePasseReinitialise", "Mot de passe réinitialisé")]
    [InlineData("Autre", "Autre")]
    public void JournalNaturesReadInFrench(string nature, string libelle) => Assert.Equal(libelle, LibellesJournal.Nature(nature));

    [Fact]
    public void ARevisionShowsRolesBeforeAndAfterAndTheRightRemovedWithThem()
    {
        var entree = Entree("Revision",
            avant: [new RoleApi("Technicien", Eglise), new RoleApi("Pasteur", Chapelle)],
            apres: [new RoleApi("Administrateur", null), new RoleApi("Delegue", Eglise)],
            droitsAvant: [new DroitNominatifApi("ModifierPlans", Chapelle)], droitsApres: []);

        Assert.Equal(
            ["Avant : Technicien sur Église ; Pasteur sur Chapelle", "Après : Administrateur de toute l'organisation ; Délégué sur Église",
                "« Modifier les plans » retiré sur Chapelle"],
            LibellesJournal.Details(entree, Sites));
    }

    [Fact]
    public void AnInvitationShowsItsRolesAndAGrantShowsItsSite()
    {
        var creation = Entree("Creation", avant: null, apres: [new RoleApi("Coordination", Guid.NewGuid())], droitsAvant: null, droitsApres: null);
        var attribution = Entree("AttributionDroit", avant: null, apres: null, droitsAvant: [], droitsApres: [new DroitNominatifApi("ModifierPlans", Eglise)]);
        var acceptation = Entree("Acceptation", avant: null, apres: null, droitsAvant: null, droitsApres: null);

        Assert.Equal(["Coordination sur un site"], LibellesJournal.Details(creation, Sites));
        Assert.Equal(["« Modifier les plans » accordé sur Église"], LibellesJournal.Details(attribution, Sites));
        Assert.Empty(LibellesJournal.Details(acceptation, Sites));
        Assert.Equal("aucun rôle", LibellesJournal.Roles([], Sites));
    }

    [Fact]
    public void RolesAreLabelledAndSummarised()
    {
        Assert.Equal("Délégué", LibellesRoles.Libelle("Delegue"));
        Assert.Equal("Administrateur", LibellesRoles.Libelle("Administrateur"));
        Assert.Equal("Inconnu", LibellesRoles.Libelle("Inconnu"));
        Assert.Equal("Administrateur de toute l'organisation ; Technicien sur Temple ; Pasteur sur un site",
            LibellesRoles.Resume([new RoleInviteApi("Administrateur", null, null), new RoleInviteApi("Technicien", Eglise, "Temple"), new RoleInviteApi("Pasteur", Chapelle, null)]));
    }

    private static EntreeJournalApi Entree(
        string nature, IReadOnlyList<RoleApi>? avant, IReadOnlyList<RoleApi>? apres, IReadOnlyList<DroitNominatifApi>? droitsAvant, IReadOnlyList<DroitNominatifApi>? droitsApres) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Luc", nature, null, null, DateTimeOffset.UtcNow, null, avant, apres, droitsAvant, droitsApres);
}
