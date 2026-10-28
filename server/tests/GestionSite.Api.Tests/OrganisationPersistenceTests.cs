using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>Aller-retour réel dans PostgreSQL : correspondance, date UTC et limite du nom.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class OrganisationPersistenceTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task OrganisationIsStoredAndReadBackUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString);
        using var client = factory.CreateClient(); // démarre l'hôte : migrations appliquées

        // Limite exacte en unités UTF-16 (198 + une paire de substitution), acceptée par varchar(200).
        var nom = new string('a', Organisation.NomMaxLength - 2) + "🕊";
        var creeLe = new DateTimeOffset(2026, 10, 28, 9, 30, 15, TimeSpan.FromHours(2)).AddTicks(1_234_560);
        var organisation = new Organisation(Guid.NewGuid(), nom, creeLe);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
            context.Organisations.Add(organisation);
            await context.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
            var relue = await context.Organisations.AsNoTracking().SingleAsync(o => o.Id == organisation.Id);

            Assert.Equal(nom, relue.Nom);
            Assert.Equal(creeLe, relue.CreeLe); // microsecondes comprises
            Assert.Equal(organisation.CreeLe, relue.CreeLe);
            Assert.Equal(TimeSpan.Zero, relue.CreeLe.Offset);
        }

        Assert.Equal(1L, await database.CountAsync(
            "SELECT count(*) FROM identite.organisations WHERE id = @id AND cree_le = @date",
            ("id", organisation.Id), ("date", creeLe.ToUniversalTime())));
    }
}
