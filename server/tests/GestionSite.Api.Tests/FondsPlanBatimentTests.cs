using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.S3.Model;
using GestionSite.Api.Infrastructure;
using GestionSite.Modules.Identite.Domain;
using ImageMagick;

namespace GestionSite.Api.Tests;

/// <summary>
/// Fonds de plan de bâtiment importés en image (US-21, S21-04) : métadonnées retirées, formats et tailles refusés, lecture par
/// les seuls internes du site, quota de l'organisation, aucun fichier orphelin. Images synthétiques seulement.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class FondsPlanBatimentTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private const long Quota = 1024L * 1024 * 1024; // valeur par défaut de PlansBatiment:QuotaFondsOctets

    private string Plans => $"{Base}/building-plans";

    [Fact]
    public async Task AJpegWithALocationIsStoredWithoutMetadataAndReadOnlyByInternalAccountsOfTheSite()
    {
        var (responsable, plan) = await PlanAsync();
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var admin = await JetonAsync("admin@inventaire.test");
        var commande = Guid.NewGuid();

        using var importe = await ImporterAsync(plan, responsable, JpegAvecPosition(), "plan-rdc.jpg", commande);
        var fond = await importe.Content.ReadFromJsonAsync<JsonElement>();
        var id = fond.GetProperty("id").GetGuid();
        using var renvoi = await ImporterAsync(plan, responsable, JpegAvecPosition(), "plan-rdc.jpg", commande);
        using var parTechnicien = await ImporterAsync(plan, technicien, JpegAvecPosition(), "plan.jpg");
        using var lecture = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}/backgrounds/{id}", pasteur);
        using var lectureAdmin = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}/backgrounds/{id}", admin); // aucun droit métier implicite
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/building-plans/{plan}/backgrounds/{id}", pasteur);
        using var autrePlan = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{Guid.NewGuid()}/backgrounds/{id}", pasteur);
        using var sansSession = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}/backgrounds/{id}", "jeton-invalide");
        var liste = await LireAsync($"{Plans}/{plan}/backgrounds", pasteur);
        using var listeInconnue = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{Guid.NewGuid()}/backgrounds", pasteur);
        var stockes = await FichiersAsync();

        Assert.Equal(HttpStatusCode.OK, importe.StatusCode);
        Assert.Equal("image/jpeg", fond.GetProperty("typeContenu").GetString());
        Assert.Equal((1600, 900), (fond.GetProperty("largeur").GetInt32(), fond.GetProperty("hauteur").GetInt32()));
        Assert.Equal(id, (await renvoi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()); // même envoi : même fond
        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lecture.StatusCode);
        Assert.Equal("image/jpeg", lecture.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", lecture.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", lecture.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, lectureAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autrePlan.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sansSession.StatusCode);
        Assert.Equal([id], liste.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()));
        Assert.Equal("Rose Responsable", liste[0].GetProperty("auteur").GetString());
        Assert.Equal(HttpStatusCode.NotFound, listeInconnue.StatusCode);
        // Maître et dérivé seulement, sans aucune métadonnée (position GPS comprise), dans le dossier de l'organisation.
        Assert.Equal(2, stockes.Count);
        Assert.All(stockes, cle => Assert.StartsWith($"organisations/{Organisation:D}/plans-batiment/{plan:D}/fonds/{id:D}-", cle, StringComparison.Ordinal));
        foreach (var contenu in (await Task.WhenAll(stockes.Select(LireFichierAsync))).Append(await lecture.Content.ReadAsByteArrayAsync()))
        {
            using var image = new MagickImage(contenu);
            Assert.Null(image.GetExifProfile());
            Assert.Null(image.GetXmpProfile());
            Assert.Equal(-1, contenu.AsSpan().IndexOf("Exif"u8));
        }

        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM patrimoine.fonds_plan_batiment"));
    }

    [Fact]
    public async Task AFondOfAnotherOrganisationAnswersNotFound()
    {
        var (_, plan) = await PlanAsync();
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var (autrePlan, autreFond) = await FondEtrangerAsync();

        using var parSonPlan = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{autrePlan}/backgrounds/{autreFond}", pasteur);
        using var parNotrePlan = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}/backgrounds/{autreFond}", pasteur);
        using var liste = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{autrePlan}/backgrounds", pasteur);
        using var parSonOrganisation = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/sites/{Site}/inventory/building-plans/{autrePlan}/backgrounds/{autreFond}", pasteur);

        Assert.Equal(HttpStatusCode.NotFound, parSonPlan.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, parNotrePlan.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, liste.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, parSonOrganisation.StatusCode);
    }

    [Fact]
    public async Task RefusedFilesAreNeitherDecodedNorStored()
    {
        var (responsable, plan) = await PlanAsync();
        using var petitPng = new MagickImage(MagickColors.White, 8, 8) { Format = MagickFormat.Png };

        using var svg = await ImporterAsync(plan, responsable, "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8.ToArray(), "plan.svg", type: "image/svg+xml");
        using var dwg = await ImporterAsync(plan, responsable, [.. "AC1032"u8, .. new byte[256]], "plan.dwg", type: "application/acad");
        using var trompeuse = await ImporterAsync(plan, responsable, "%PDF-1.7\n%%EOF\n"u8.ToArray(), "plan.png", type: "image/png");
        using var tropGros = await ImporterAsync(plan, responsable, [0xFF, 0xD8, 0xFF, .. new byte[25 * 1024 * 1024]], "plan.jpg");
        using var annonce = await ImporterAsync(plan, responsable, PngAnnonce(20_000, 20_000), "plan.png", type: "image/png");
        using var illisible = await ImporterAsync(plan, responsable, [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10], "plan.jpg");
        using var sansCommande = await ImporterAsync(plan, responsable, petitPng.ToByteArray(), "plan.png", Guid.Empty);
        using var sansFichier = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/backgrounds", responsable, Formulaire(Guid.NewGuid()));
        using var deuxFichiers = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/backgrounds", responsable,
            Formulaire(Guid.NewGuid(), (petitPng.ToByteArray(), "a.png", "image/png"), (petitPng.ToByteArray(), "b.png", "image/png")));
        using var enJson = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/backgrounds", responsable, new { commandeId = Guid.NewGuid() });
        using var planInconnu = await ImporterAsync(Guid.NewGuid(), responsable, petitPng.ToByteArray(), "plan.png");

        foreach (var refus in new[] { svg, dwg, trompeuse, tropGros, annonce, illisible, sansFichier, deuxFichiers, enJson })
        {
            Assert.Equal("fichier", await ChampAsync(refus));
        }

        Assert.Equal("commandeId", await ChampAsync(sansCommande));
        Assert.Equal(HttpStatusCode.NotFound, planInconnu.StatusCode);
        Assert.Empty(await FichiersAsync());
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.fonds_plan_batiment"));
    }

    [Fact]
    public async Task APngStaysAPngAndASendIdCannotBeReusedElsewhere()
    {
        var (responsable, plan) = await PlanAsync();
        var parvis = await CreerAsync($"{Base}/buildings", new { nom = "Parvis" }, await JetonAsync("technicien@inventaire.test"));
        var autre = await CreerAsync(Plans, new { batimentId = parvis }, responsable);
        using var dessin = new MagickImage(MagickColors.White, 5_000, 3_000) { Format = MagickFormat.Png };
        var commande = Guid.NewGuid();

        using var importe = await ImporterAsync(plan, responsable, dessin.ToByteArray(), "plan.png", commande, "image/png");
        using var reutilise = await ImporterAsync(autre, responsable, dessin.ToByteArray(), "plan.png", commande, "image/png");
        var fond = await importe.Content.ReadFromJsonAsync<JsonElement>();
        using var lecture = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}/backgrounds/{fond.GetProperty("id").GetGuid()}", responsable);

        Assert.Equal("image/png", fond.GetProperty("typeContenu").GetString());
        Assert.Equal((5_000, 3_000), (fond.GetProperty("largeur").GetInt32(), fond.GetProperty("hauteur").GetInt32()));
        Assert.Equal((4_096, 2_458), (fond.GetProperty("largeurAffichage").GetInt32(), fond.GetProperty("hauteurAffichage").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, reutilise.StatusCode);
        Assert.Equal("commande_reutilisee", (await reutilise.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal("image/png", lecture.Content.Headers.ContentType?.MediaType);
        using var affichage = new MagickImage(await lecture.Content.ReadAsByteArrayAsync());
        Assert.Equal((4_096u, 2_458u), (affichage.Width, affichage.Height));
        Assert.Equal(2, (await FichiersAsync()).Count);
    }

    [Fact]
    public async Task TheOrganisationQuotaIsCheckedAndARefusedImportLeavesNoFile()
    {
        var (responsable, plan) = await PlanAsync();
        await OccuperAsync(plan, Quota - 100); // reste 100 octets : l'image, une fois traitée, ne tient pas

        using var depasse = await ImporterAsync(plan, responsable, JpegAvecPosition(), "plan.jpg");
        var apresTraitement = await FichiersAsync();
        await OccuperAsync(plan, 100); // plein : refus avant tout décodage
        using var plein = await ImporterAsync(plan, responsable, JpegAvecPosition(), "plan.jpg");

        foreach (var refus in new[] { depasse, plein })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refus.StatusCode);
            Assert.Equal("quota_atteint", (await refus.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Empty(apresTraitement); // fichiers écrits puis retirés
        Assert.Empty(await FichiersAsync());
        Assert.Equal(2, await Database.CountAsync("SELECT count(*) FROM patrimoine.fonds_plan_batiment"));
    }

    [Fact]
    public async Task TheSameSendReceivedTwiceAtOnceKeepsOneFondAndNoExtraFile()
    {
        var (responsable, plan) = await PlanAsync();
        var commande = Guid.NewGuid();

        var reponses = await Task.WhenAll(
            ImporterAsync(plan, responsable, JpegAvecPosition(), "plan.jpg", commande), ImporterAsync(plan, responsable, JpegAvecPosition(), "plan.jpg", commande));
        var ids = await Task.WhenAll(reponses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()));

        Assert.All(reponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(2, (await FichiersAsync()).Count);
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM patrimoine.fonds_plan_batiment"));
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Fact]
    public async Task TheApplicationRoleCannotRewriteOrDeleteAFond()
    {
        await PlanAsync();

        Assert.Equal(0, await Database.CountAsync(
            """
            SELECT count(*) FROM information_schema.role_table_grants
            WHERE grantee = 'gestionsite_app' AND table_schema = 'patrimoine' AND table_name = 'fonds_plan_batiment'
              AND privilege_type IN ('UPDATE', 'DELETE')
            """));
    }

    // Église à un niveau, son plan ; un responsable (nommé) sur le site.
    private async Task<(string Responsable, Guid Plan)> PlanAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        await Database.CountAsync(
            "WITH r AS (UPDATE identite.comptes SET nom_affiche = 'Rose Responsable' WHERE email = 'responsable@inventaire.test' RETURNING 1) SELECT count(*) FROM r");
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var rdc = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        return (responsable, await CreerAsync(Plans, new { batimentId = eglise, niveauId = rdc }, responsable));
    }

    private Task<HttpResponseMessage> ImporterAsync(
        Guid plan, string jeton, byte[] contenu, string nom, Guid? commandeId = null, string type = "image/jpeg") =>
        EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/backgrounds", jeton, Formulaire(commandeId ?? Guid.NewGuid(), (contenu, nom, type)));

    private static MultipartFormDataContent Formulaire(Guid commandeId, params (byte[] Contenu, string Nom, string Type)[] fichiers)
    {
        var formulaire = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(new { commandeId })), "demande" } };
        foreach (var (contenu, nom, type) in fichiers)
        {
            var partie = new ByteArrayContent(contenu);
            partie.Headers.ContentType = new MediaTypeHeaderValue(type);
            formulaire.Add(partie, "fichier", nom);
        }

        return formulaire;
    }

    // Fond d'une autre organisation (plan et bâtiment compris), écrit directement en base sur le même site.
    private async Task<(Guid Plan, Guid Fond)> FondEtrangerAsync()
    {
        var (plan, fond) = (Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(1, await Database.CountAsync(
            """
            WITH b AS (
                INSERT INTO patrimoine.batiments (id, organisation_id, site_id, nom, cree_le)
                VALUES (gen_random_uuid(), @organisation, @site, 'Ailleurs', now()) RETURNING id, organisation_id, site_id),
            p AS (
                INSERT INTO patrimoine.plans_batiment (id, organisation_id, site_id, batiment_id, cree_le)
                SELECT @plan, organisation_id, site_id, id, now() FROM b RETURNING id, organisation_id, site_id),
            f AS (
                INSERT INTO patrimoine.fonds_plan_batiment (id, organisation_id, site_id, plan_id, commande_id, type_contenu, largeur, hauteur,
                    taille_maitre, largeur_affichage, hauteur_affichage, taille_affichage, cle_maitre, cle_affichage, auteur_compte_id, cree_le)
                SELECT @fond, organisation_id, site_id, id, gen_random_uuid(), 'image/png', 10, 10, 1, 10, 10, 1, 'x', 'x', gen_random_uuid(), now()
                FROM p RETURNING 1)
            SELECT count(*) FROM f
            """,
            ("organisation", Guid.NewGuid()), ("site", Site), ("plan", plan), ("fond", fond)));
        return (plan, fond);
    }

    // Place déjà occupée par un fond fictif du plan (sans fichier), pour approcher le quota sans téléverser 1 Gio.
    private async Task OccuperAsync(Guid plan, long octets) => Assert.Equal(1, await Database.CountAsync(
        """
        WITH f AS (
            INSERT INTO patrimoine.fonds_plan_batiment (id, organisation_id, site_id, plan_id, commande_id, type_contenu, largeur, hauteur,
                taille_maitre, largeur_affichage, hauteur_affichage, taille_affichage, cle_maitre, cle_affichage, auteur_compte_id, cree_le)
            SELECT gen_random_uuid(), organisation_id, site_id, id, gen_random_uuid(), 'image/png', 10, 10, @maitre, 10, 10, 1, 'x', 'x', gen_random_uuid(), now()
            FROM patrimoine.plans_batiment WHERE id = @plan RETURNING 1)
        SELECT count(*) FROM f
        """,
        ("plan", plan), ("maitre", (int)(octets - 1))));

    private async Task<List<string>> FichiersAsync()
    {
        using var s3 = Stockage();
        var reponse = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = InfrastructureFixture.Bucket, Prefix = $"organisations/{Organisation:D}/" });
        return [.. (reponse.S3Objects ?? []).Select(o => o.Key)];
    }

    private async Task<byte[]> LireFichierAsync(string cle)
    {
        using var s3 = Stockage();
        using var objet = await s3.GetObjectAsync(InfrastructureFixture.Bucket, cle);
        using var copie = new MemoryStream();
        await objet.ResponseStream.CopyToAsync(copie);
        return copie.ToArray();
    }

    private Amazon.S3.AmazonS3Client Stockage() =>
        ObjectStorageClientFactory.Create(Infrastructure.StorageUrl, InfrastructureFixture.AccessKey, InfrastructureFixture.SecretKey, InfrastructureFixture.Region);

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>JPEG synthétique portant une position GPS et une description.</summary>
    internal static byte[] JpegAvecPosition()
    {
        using var source = new MagickImage(MagickColors.LightGray, 1_600, 900);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(48, 1), new Rational(51, 1), new Rational(24, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(2, 1), new Rational(21, 1), new Rational(3, 1)]);
        exif.SetValue(ExifTag.ImageDescription, "Plan synthétique");
        source.SetProfile(exif);
        source.Format = MagickFormat.Jpeg;
        return source.ToByteArray();
    }

    /// <summary>Vrai PNG de 8 × 8 px dont l'en-tête annonce d'autres dimensions (somme de contrôle recalculée).</summary>
    private static byte[] PngAnnonce(uint largeur, uint hauteur)
    {
        using var petit = new MagickImage(MagickColors.White, 8, 8) { Format = MagickFormat.Png };
        var octets = petit.ToByteArray();
        BinaryPrimitives.WriteUInt32BigEndian(octets.AsSpan(16), largeur);
        BinaryPrimitives.WriteUInt32BigEndian(octets.AsSpan(20), hauteur);
        var crc = 0xFFFFFFFFu;
        foreach (var octet in octets.AsSpan(12, 17))
        {
            crc ^= octet;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(octets.AsSpan(29), ~crc);
        return octets;
    }
}
