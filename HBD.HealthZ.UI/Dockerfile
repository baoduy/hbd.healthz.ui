# The build stage runs on the build platform and cross-publishes for the target
# architecture, so a multi-arch build never emulates the SDK under QEMU.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src

COPY ["global.json", "Directory.Build.props", "./"]
COPY ["HBD.HealthZ.UI/HBD.HealthZ.UI.csproj", "HBD.HealthZ.UI/"]
# A RID-specific restore/publish resolves native assets for this one platform only,
# instead of the portable publish's 22-directory runtimes/ tree.
RUN dotnet restore "HBD.HealthZ.UI/HBD.HealthZ.UI.csproj" \
        -r "linux-musl-$(echo "$TARGETARCH" | sed 's/amd64/x64/')"

COPY . .
RUN dotnet publish "HBD.HealthZ.UI/HBD.HealthZ.UI.csproj" \
        -c Release \
        -r "linux-musl-$(echo "$TARGETARCH" | sed 's/amd64/x64/')" \
        --no-self-contained \
        --no-restore \
        -o /app/publish \
        -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
# Kestrel listens on 8080 in the .NET 8+ images; TLS terminates at the ingress.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "HBD.HealthZ.UI.dll"]
