FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Acordanza.Core/Acordanza.Core.csproj Acordanza.Core/
COPY Acordanza.Api/Acordanza.Api.csproj Acordanza.Api/
RUN dotnet restore Acordanza.Api/Acordanza.Api.csproj

COPY Acordanza.Core/ Acordanza.Core/
COPY Acordanza.Api/ Acordanza.Api/
RUN dotnet publish Acordanza.Api/Acordanza.Api.csproj -c Release -o /app/publish --no-restore

# Azure Linux "distroless" runtime: no shell, no package manager, non-root
# user built in — smallest attack surface for a public-facing API.
# "-extra" pulls in ICU/tzdata/openssl, needed for correct date/culture
# handling (tick dates, route names) instead of falling back to invariant
# globalization.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0-distroless-extra AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "Acordanza.Api.dll"]
