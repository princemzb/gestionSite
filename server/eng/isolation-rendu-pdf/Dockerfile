# Image d'essai de l'isolation du rendu PDF (SOC-05), construite par server/eng/verify-image.sh : l'image livrée
# (argument IMAGE), dont le programme de rendu est remplacé par la sonde. Jamais déployée.
ARG IMAGE

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302 AS sonde
WORKDIR /sonde
COPY Sonde.csproj Program.cs ./
RUN dotnet publish -c Release -o /sortie

FROM ${IMAGE}
COPY --from=sonde /sortie/GestionSite.Modules.Patrimoine.RenduPdf.dll /app/rendu-pdf/
