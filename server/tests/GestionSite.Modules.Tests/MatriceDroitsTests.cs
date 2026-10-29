using GestionSite.Modules.Identite.Autorisation;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Modules.Tests;

/// <summary>
/// La matrice du code reproduit docs/metier/PERMISSIONS.md (T, R/D, P/C, A). Copie du tableau tenue à la
/// main : toute modification du document doit modifier ce test et la matrice dans la même PR.
/// </summary>
public sealed class MatriceDroitsTests
{
    // Lecture directe du tableau de PERMISSIONS.md (+ décisions DEC-04) : « T R D P C A ».
    [Theory]
    [InlineData(Droit.ConsulterSite, "T R D P C")]
    [InlineData(Droit.DeposerSignalement, "T R D P C")]
    [InlineData(Droit.QualifierAffecter, "R D")]
    [InlineData(Droit.DocumenterIntervention, "T R D")]
    [InlineData(Droit.CloturerIntervention, "T R D")]
    [InlineData(Droit.RouvrirArbitrer, "R D")]
    [InlineData(Droit.Relancer, "R D P C")]
    [InlineData(Droit.LancerIa, "T R D")]
    [InlineData(Droit.EngagerBudget, "R D")]
    [InlineData(Droit.EnregistrerAccordFinance, "R D")]
    [InlineData(Droit.ConsommerStock, "T R D")]
    [InlineData(Droit.ReceptionnerAjusterStock, "R D")]
    [InlineData(Droit.VoirPosition, "T R D")]
    [InlineData(Droit.LireContactsDeclarant, "T R D P C")]
    [InlineData(Droit.CorrigerContactDeclarant, "R D")]
    [InlineData(Droit.LireJustificatifs, "R D P C")]
    [InlineData(Droit.EcrireJustificatifs, "R D")]
    [InlineData(Droit.ValiderEstimation, "R D")]
    [InlineData(Droit.LireMontants, "R D P C")]
    [InlineData(Droit.ModifierInventaire, "T R D")]
    [InlineData(Droit.ModifierGrilles, "R D")]
    [InlineData(Droit.EvaluerComposant, "T R D")]
    [InlineData(Droit.ModifierPlans, "R D")]
    [InlineData(Droit.GererComptes, "A")]
    [InlineData(Droit.AutoriserSupport, "A")]
    public void RolesMatchThePermissionTable(Droit droit, string roles)
    {
        var attendus = roles.Split(' ').Select(Lettre).Order();

        Assert.Equal(attendus, MatriceDroits.RolesPour(droit).Order());
    }

    [Fact]
    public void EveryRightIsInTheMatrix()
    {
        Assert.All(Enum.GetValues<Droit>(), d => Assert.NotEmpty(MatriceDroits.RolesPour(d)));
    }

    [Fact]
    public void AdministratorHasNoBusinessRightAndBusinessRolesNoAdministration()
    {
        foreach (var droit in Enum.GetValues<Droit>())
        {
            var roles = MatriceDroits.RolesPour(droit);
            Assert.Equal(MatriceDroits.EstOrganisationnelle(droit), roles.Contains(Role.Administrateur));
            Assert.True(!MatriceDroits.EstOrganisationnelle(droit) || roles.Count == 1);
        }
    }

    private static Role Lettre(string lettre) => lettre switch
    {
        "T" => Role.Technicien,
        "R" => Role.Responsable,
        "D" => Role.Delegue,
        "P" => Role.Pasteur,
        "C" => Role.Coordination,
        "A" => Role.Administrateur,
        _ => throw new ArgumentOutOfRangeException(nameof(lettre), lettre, null),
    };
}
