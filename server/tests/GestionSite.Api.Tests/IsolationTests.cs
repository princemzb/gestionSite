using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Double barrière d'isolation (ADR-10, S06-02) sur PostgreSQL réel : filtre applicatif, puis RLS
/// forcée, y compris pour le rôle propriétaire et sans aucun filtre applicatif.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class IsolationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private readonly Guid _organisationA = Guid.NewGuid();
    private readonly Guid _organisationB = Guid.NewGuid();
    private readonly Guid _siteA = Guid.NewGuid();
    private readonly Guid _siteB = Guid.NewGuid();
    private readonly Guid _membreA = Guid.NewGuid();
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using var client = _api.CreateClient(); // migrations appliquées par le rôle propriétaire

        await SeedAsync(_organisationA, _siteA, _membreA);
        await SeedAsync(_organisationB, _siteB, Guid.NewGuid());
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ApplicationFilterShowsOnlyTheCurrentOrganisation()
    {
        await using var scope = Scope(_organisationA);
        var context = Context(scope);

        Assert.Equal([_organisationA], await context.Organisations.Select(o => o.Id).ToListAsync());
        Assert.Equal([_siteA], await context.Sites.Select(s => s.Id).ToListAsync());
        Assert.Equal([_membreA], await context.Membres.Select(m => m.Id).ToListAsync());
        Assert.All(await context.RolesAttribues.ToListAsync(), r => Assert.Equal(_organisationA, r.OrganisationId));
    }

    [Fact]
    public async Task DatabaseStillIsolatesWhenTheApplicationFilterIsForgotten()
    {
        await using var scope = Scope(_organisationA);
        var context = Context(scope);

        Assert.Equal([_siteA], await context.Sites.IgnoreQueryFilters().Select(s => s.Id).ToListAsync());
        Assert.Equal(1, await context.Organisations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await context.Membres.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await context.RolesAttribues.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task WithoutCurrentOrganisationNothingIsVisible()
    {
        await using var scope = Scope(organisation: null);
        var context = Context(scope);

        Assert.Equal(0, await context.Sites.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await context.Organisations.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task PooledConnectionsNeverKeepThePreviousOrganisation()
    {
        await using (var scope = Scope(_organisationA))
        {
            Assert.Equal(1, await Context(scope).Sites.IgnoreQueryFilters().CountAsync());
        }

        await using (var scope = Scope(_organisationB))
        {
            Assert.Equal([_siteB], await Context(scope).Sites.IgnoreQueryFilters().Select(s => s.Id).ToListAsync());
        }

        await using (var scope = Scope(organisation: null))
        {
            Assert.Equal(0, await Context(scope).Sites.IgnoreQueryFilters().CountAsync());
        }
    }

    [Theory]
    [InlineData("gestionsite_app")]
    [InlineData("gestionsite_migrator")] // propriétaire des tables : FORCE ROW LEVEL SECURITY
    public async Task RawSqlCannotReadAnotherOrganisationWhateverTheRole(string role)
    {
        var connectionString = role == "gestionsite_app" ? _database.ApplicationConnectionString : _database.MigrationsConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM identite.sites"));
        await ScalarAsync(connection, $"SELECT set_config('{OrganisationCourante.VariableSession}', '{_organisationA}', false)");
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM identite.sites"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM identite.sites WHERE organisation_id = '{_organisationB}'"));
    }

    [Fact]
    public async Task DatabaseRefusesWritingIntoAnotherOrganisation()
    {
        await using var connection = new NpgsqlConnection(_database.ApplicationConnectionString);
        await connection.OpenAsync();
        await ScalarAsync(connection, $"SELECT set_config('{OrganisationCourante.VariableSession}', '{_organisationA}', false)");

        var refus = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(connection,
            $"INSERT INTO identite.sites (id, organisation_id, nom, cree_le) VALUES ('{Guid.NewGuid()}', '{_organisationB}', 'Intrus', now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState); // violation de politique RLS

        var deplacement = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(connection,
            $"UPDATE identite.sites SET organisation_id = '{_organisationB}' WHERE id = '{_siteA}'"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, deplacement.SqlState);
    }

    [Fact]
    public async Task ApplicationRefusesWritingIntoAnotherOrganisation()
    {
        await using var scope = Scope(_organisationA);
        var context = Context(scope);
        context.Sites.Add(new Site(Guid.NewGuid(), _organisationB, "Intrus", Instant));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("CREATE TABLE identite.intrus (id int)")]
    [InlineData("ALTER TABLE identite.sites DISABLE ROW LEVEL SECURITY")]
    [InlineData("DROP POLICY isolation_organisation ON identite.sites")]
    public async Task ApplicationRoleCannotChangeTheSchema(string ddl)
    {
        await using var connection = new NpgsqlConnection(_database.ApplicationConnectionString);
        await connection.OpenAsync();

        var refus = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(connection, ddl));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState); // droit absent ou « must be owner »
    }

    [Fact]
    public async Task RoleCannotTargetASiteOfAnotherOrganisationEvenInSql()
    {
        // Superutilisateur (ignore la RLS) : seule la clé étrangère composite peut encore refuser.
        var refus = await Assert.ThrowsAsync<PostgresException>(() => _database.CountAsync(
            "WITH ajout AS (INSERT INTO identite.roles_attribues (id, organisation_id, membre_id, role, site_id) " +
            "VALUES (@id, @organisation, @membre, 'Technicien', @site) RETURNING 1) SELECT count(*) FROM ajout",
            ("id", Guid.NewGuid()), ("organisation", _organisationA), ("membre", _membreA), ("site", _siteB)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refus.SqlState);
    }

    [Fact]
    public async Task ConcurrentRevisionOfAMemberIsDetected()
    {
        await using var premier = Scope(_organisationA);
        await using var second = Scope(_organisationA);
        var membrePremier = await Context(premier).Membres.SingleAsync(m => m.Id == _membreA);
        var membreSecond = await Context(second).Membres.SingleAsync(m => m.Id == _membreA);

        membrePremier.Revoquer(Instant);
        await Context(premier).SaveChangesAsync();
        membreSecond.Revoquer(Instant.AddMinutes(1));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Context(second).SaveChangesAsync());
    }

    [Fact]
    public async Task AnAccountIsMemberOfAnOrganisationOnlyOnce()
    {
        await using var scope = Scope(_organisationA);
        var context = Context(scope);
        var compte = await context.Membres.Where(m => m.Id == _membreA).Select(m => m.CompteId).SingleAsync();
        context.Membres.Add(new Membre(Guid.NewGuid(), _organisationA, compte, Instant));

        var refus = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(refus.InnerException).SqlState);
    }

    [Fact]
    public async Task MemberRolesAreStoredPerSite()
    {
        await using var scope = Scope(_organisationA);
        var membre = await Context(scope).Membres.Include(m => m.Roles).SingleAsync(m => m.Id == _membreA);

        Assert.Contains(membre.Roles, r => r.Role == Role.Administrateur && r.SiteId is null);
        Assert.Contains(membre.Roles, r => r.Role == Role.Technicien && r.SiteId == _siteA);
    }

    private async Task SeedAsync(Guid organisationId, Guid siteId, Guid membreId)
    {
        await using var scope = Scope(organisationId);
        var context = Context(scope);
        var site = new Site(siteId, organisationId, $"Site {siteId:N}", Instant);
        var membre = new Membre(membreId, organisationId, Guid.NewGuid(), Instant);
        membre.Accorder(Role.Administrateur, site: null);
        membre.Accorder(Role.Technicien, site);
        context.AddRange(new Organisation(organisationId, $"Organisation {organisationId:N}", Instant), site, membre);
        await context.SaveChangesAsync();
    }

    private AsyncServiceScope Scope(Guid? organisation)
    {
        var scope = _api.Services.CreateAsyncScope();
        if (organisation is { } id)
        {
            scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(id);
        }

        return scope;
    }

    private static IdentiteDbContext Context(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
