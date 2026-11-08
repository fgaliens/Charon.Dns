FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source
COPY ["src/Charon.Dns/Charon.Dns.csproj", "src/Charon.Dns/"]
COPY ["src/Charon.Dns.Lib/Charon.Dns.Lib.csproj", "src/Charon.Dns.Lib/"]
COPY ["src/Charon.Dns.Configuration/Charon.Dns.Configuration.csproj", "src/Charon.Dns.Configuration/"]
RUN dotnet restore "src/Charon.Dns/Charon.Dns.csproj"
COPY . .
RUN rm src/Charon.Dns/settings.json
RUN mv src/Charon.Dns/settings.docker.json src/Charon.Dns/settings.json
RUN rm src/Charon.Dns/zones.json
RUN mv src/Charon.Dns/zones.docker.json src/Charon.Dns/zones.json
WORKDIR "/source/src/Charon.Dns"
RUN dotnet build "./Charon.Dns.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Charon.Dns.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Charon.Dns.dll"]
