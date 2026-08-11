FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Acordanza.Core/Acordanza.Core.csproj Acordanza.Core/
COPY Acordanza.Worker/Acordanza.Worker.csproj Acordanza.Worker/
RUN dotnet restore Acordanza.Worker/Acordanza.Worker.csproj

COPY Acordanza.Core/ Acordanza.Core/
COPY Acordanza.Worker/ Acordanza.Worker/
RUN dotnet publish Acordanza.Worker/Acordanza.Worker.csproj -c Release -o /app/publish --no-restore

# Runtime, not aspnet: the worker drains a queue and calls out over HTTP — it has no
# use for the ASP.NET Core shared framework, so this image never carries it (plan 22).
# Same distroless/"-extra" reasoning as the API's Dockerfile otherwise.
FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0-distroless-extra AS final
WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "Acordanza.Worker.dll"]
