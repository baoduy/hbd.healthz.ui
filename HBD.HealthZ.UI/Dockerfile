﻿FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY ["HBD.HealthZ.UI/HBD.HealthZ.UI.csproj", "HBD.HealthZ.UI/"]
RUN dotnet restore "HBD.HealthZ.UI/HBD.HealthZ.UI.csproj"
COPY . .
WORKDIR "/src/HBD.HealthZ.UI"
RUN dotnet build "HBD.HealthZ.UI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HBD.HealthZ.UI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HBD.HealthZ.UI.dll"]
