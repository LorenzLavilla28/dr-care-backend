FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DrCare.Domain/DrCare.Domain.csproj src/DrCare.Domain/
COPY src/DrCare.Application/DrCare.Application.csproj src/DrCare.Application/
COPY src/DrCare.Infrastructure/DrCare.Infrastructure.csproj src/DrCare.Infrastructure/
COPY src/DrCare.Api/DrCare.Api.csproj src/DrCare.Api/
RUN dotnet restore src/DrCare.Api/DrCare.Api.csproj
COPY src/ src/
RUN dotnet publish src/DrCare.Api/DrCare.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV CHROME_BIN=/usr/bin/chromium
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "DrCare.Api.dll"]
