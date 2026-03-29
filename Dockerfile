FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER root
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Charon.Dns/Charon.Dns.csproj", "Charon.Dns/"]
COPY ["Charon.Dns.Lib/Charon.Dns.Lib.csproj", "Charon.Dns.Lib/"]
RUN dotnet restore "Charon.Dns/Charon.Dns.csproj"
COPY . .
RUN rm Charon.Dns/settings.json
RUN mv Charon.Dns/settings.docker.json Charon.Dns/settings.json
WORKDIR "/src/Charon.Dns"
RUN dotnet build "./Charon.Dns.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Charon.Dns.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Charon.Dns.dll"]
