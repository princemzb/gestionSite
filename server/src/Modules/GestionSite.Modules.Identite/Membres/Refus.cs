namespace GestionSite.Modules.Identite.Membres;

/// <summary>Refus métier traduit en ProblemDetails (statut, code stable, message utilisateur).</summary>
internal sealed class RefusException(int statut, string code, string message) : Exception(message)
{
    internal int Statut { get; } = statut;

    internal string Code { get; } = code;

    internal static RefusException Introuvable() =>
        new(404, "introuvable", "Ressource introuvable.");

    internal static RefusException Interdit() =>
        new(403, "droit_insuffisant", "Action réservée à l'administrateur de l'organisation.");

    internal static RefusException Invalide(string code, string message) => new(422, code, message);

    internal static RefusException Conflit(string code, string message) => new(409, code, message);
}
