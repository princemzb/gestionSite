# Image de préproduction (DEP-01, ADR-08) : API (interne, 127.0.0.1:8081) + application web (publique, :8080).
# Construire depuis la racine du dépôt : docker build --platform linux/amd64 -f server/Dockerfile .
# Compilation sur l’architecture du poste (sortie .NET portable) : évite l’émulation amd64, lente et fragile.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302 AS construction
WORKDIR /depot
COPY global.json dotnet-tools.json ./
COPY server/ server/
# API publiée pour l'architecture cible seulement : bibliothèques natives d'ImageMagick (photos) de cette seule plateforme.
ARG TARGETARCH
RUN dotnet publish server/src/GestionSite.Api -c Release -o /sortie/api --self-contained false \
      -r "linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64)" \
 && dotnet publish server/src/GestionSite.Web -c Release -o /sortie/web

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=construction /sortie/api ./api
COPY --from=construction /sortie/web ./web
COPY server/deploy/demarrer.sh ./demarrer.sh
# Utilisateur non privilégié fourni par l'image (« app ») ; rien d'écrit sur le disque (éphémère).
USER app
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "/app/demarrer.sh"]
