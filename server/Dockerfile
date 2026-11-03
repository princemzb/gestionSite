# Image de préproduction (DEP-01, ADR-08) : API (interne, 127.0.0.1:8081 ; planificateur :8082, S10-02) + application web (publique, :8080).
# Construire depuis la racine du dépôt : docker build --platform linux/amd64 -f server/Dockerfile .
# Isolation du rendu PDF (SOC-05) vérifiée par server/eng/verify-image.sh.

# Compte dédié du processus de rendu PDF (SOC-05) : ni groupe de l'API, ni dossier personnel, ni interpréteur.
ARG UID_RENDU=1655

# Compilation sur l’architecture du poste (sortie .NET portable) : évite l’émulation amd64, lente et fragile.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302 AS construction
WORKDIR /depot
COPY global.json dotnet-tools.json ./
COPY server/ server/
# API publiée pour l'architecture cible seulement : bibliothèques natives d'ImageMagick (photos) de cette seule plateforme.
# Processus de rendu PDF publié à part (configuration et dépendances propres, libpdfium.so de la plateforme cible) : son
# compte ne lit pas les dossiers de l'API et de l'application web, fermés aux autres comptes (SOC-05).
ARG TARGETARCH
RUN rid="linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64)" \
 && dotnet publish server/src/GestionSite.Api -c Release -o /sortie/api --self-contained false -r "$rid" \
 && dotnet publish server/src/GestionSite.Web -c Release -o /sortie/web \
 && dotnet publish server/src/Modules/GestionSite.Modules.Patrimoine.RenduPdf -c Release -o /sortie/rendu-pdf --self-contained false \
      -r "$rid" -p:GenerateRuntimeConfigurationFiles=true -p:GenerateDependencyFile=true \
 && chmod -R o= /sortie/api /sortie/web

# Lanceur setuid du rendu PDF (SOC-05), compilé pour la plateforme cible avec la bibliothèque C de l'image finale.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS lanceur
ARG UID_RENDU
RUN apt-get update \
 && apt-get install -y --no-install-recommends gcc libc6-dev \
 && rm -rf /var/lib/apt/lists/*
COPY server/deploy/lanceur-rendu-pdf.c /src/
RUN mkdir /sortie && gcc -std=gnu17 -O2 -Wall -Wextra -Werror -D_FORTIFY_SOURCE=2 -fstack-protector-strong -fPIE -pie \
      -Wl,-z,relro,-z,now -DUID_API="$(id -u app)" -DUID_RENDU="$UID_RENDU" -DGID_RENDU="$UID_RENDU" \
      -DDOTNET='"/usr/share/dotnet/dotnet"' -DPROGRAMME='"/app/rendu-pdf/GestionSite.Modules.Patrimoine.RenduPdf.dll"' \
      -o /sortie/lanceur-rendu-pdf /src/lanceur-rendu-pdf.c

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
ARG UID_RENDU
RUN groupadd --system --gid "$UID_RENDU" rendupdf \
 && useradd --system --uid "$UID_RENDU" --gid rendupdf --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin rendupdf
WORKDIR /app
# API et application web (configuration comprise) : ouvertes au seul groupe « app », jamais au compte du rendu ;
# processus de rendu lisible par tous. Droits posés à la construction et conservés par la copie.
COPY --from=construction --chown=root:app /sortie/ ./
# Lanceur : exécutable par le seul groupe « app », setuid le temps de changer de compte (voir son code source).
COPY --from=lanceur --chown=root:app --chmod=4750 /sortie/lanceur-rendu-pdf /usr/local/libexec/gestionsite/lanceur-rendu-pdf
COPY server/deploy/demarrer.sh ./demarrer.sh
# Utilisateur non privilégié fourni par l'image (« app ») ; rien d'écrit sur le disque (éphémère).
USER app
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    PlansBatiment__RenduPdfLanceur=/usr/local/libexec/gestionsite/lanceur-rendu-pdf
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "/app/demarrer.sh"]
