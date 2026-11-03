using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.Notifications;

/// <summary>Centre de notifications de l'appelant dans une organisation (CONTRATS « Centre de notifications », S10-01).</summary>
internal static class NotificationsEndpoints
{
    internal static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/organizations/{orgId:guid}/me/notifications")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(TraduireRefus);
        notifications.MapGet("/", async (Guid orgId, bool? nonLues, Guid? avant, int? limite, ServiceNotifications service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, nonLues ?? false, avant, limite, ct).ConfigureAwait(false)));
        notifications.MapPost("/{id:guid}/read", async (Guid orgId, Guid id, ServiceNotifications service, CancellationToken ct) =>
        {
            await service.MarquerLueAsync(orgId, id, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        notifications.MapPost("/read", async (Guid orgId, Guid? jusqua, ServiceNotifications service, CancellationToken ct) =>
        {
            await service.MarquerToutLuAsync(orgId, jusqua, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return endpoints;
    }

    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (NotificationIntrouvableException)
        {
            return Results.NotFound();
        }
    }
}
