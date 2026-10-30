using System.Security.Cryptography;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Observation d'un rapport de vérification : chacune est un écart à lever (un constat), dans une zone du site.</summary>
internal sealed record ObservationControle(string Libelle, Guid ZoneId, bool Danger);

/// <summary>
/// Rapport PDF d'une vérification extérieure (US-12, S12-04, décision du 30/10) : obligatoire pour une vérification
/// réglementaire faite par un intervenant extérieur, facultatif ailleurs ; 10 Mo au plus ; reconnu par sa signature
/// (« %PDF- » en tête), jamais d'après le nom ou le type annoncé. Conservé tant que le bâtiment existe (jamais supprimé).
/// </summary>
internal static class RapportControle
{
    internal const string TypeContenu = "application/pdf";
    internal const int ObservationsMax = 50;
    internal const int LibelleObservationMax = 300;

    private static ReadOnlySpan<byte> Signature => "%PDF-"u8;

    /// <summary>Empreinte SHA-256 (hexadécimal) d'un rapport reçu.</summary>
    internal static string Empreinte(byte[] contenu) => Convert.ToHexStringLower(SHA256.HashData(contenu));

    /// <summary>Contenu contrôlé et son empreinte SHA-256 (hexadécimal), ou une saisie invalide (champ « rapport »).</summary>
    internal static string Valider(byte[] contenu)
    {
        if (contenu.Length is 0 or > SharedKernel.Photos.FormulaireAvecPhotos.DocumentMax)
        {
            throw new SaisieInvalideException("rapport", "Rapport PDF de 10 Mo au plus.");
        }

        if (!contenu.AsSpan().StartsWith(Signature))
        {
            throw new SaisieInvalideException("rapport", "Le rapport doit être un fichier PDF.");
        }

        return Empreinte(contenu);
    }

    /// <summary>Rapport exigé : vérification réglementaire faite par un prestataire (intervenant extérieur).</summary>
    internal static bool Exige(NaturePlan nature, BasePlan fondement, string? prestataire) =>
        nature == NaturePlan.VerificationExterieure && fondement == BasePlan.Reglementaire && prestataire is not null;

    /// <summary>Clé de stockage propre au contrôle et au fichier : un envoi ne remplace jamais le rapport d'un autre.</summary>
    internal static string Cle(Guid organisationId, Guid controleId, Guid fichierId) =>
        $"organisations/{organisationId:D}/controles/{controleId:D}/rapport-{fichierId:D}.pdf";
}
