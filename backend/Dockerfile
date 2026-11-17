# Multi-stage production-oriented Dockerfile for EduScroll.API (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build props and project files first for Docker layer caching
COPY Directory.Build.props ./
COPY src/EduScroll.Domain/EduScroll.Domain.csproj src/EduScroll.Domain/
COPY src/EduScroll.Application/EduScroll.Application.csproj src/EduScroll.Application/
COPY src/EduScroll.Infrastructure/EduScroll.Infrastructure.csproj src/EduScroll.Infrastructure/
COPY src/EduScroll.API/EduScroll.API.csproj src/EduScroll.API/

RUN dotnet restore src/EduScroll.API/EduScroll.API.csproj

# Copy source code and publish Release artifacts (non-production appsettings excluded via .csproj + .dockerignore)
COPY src/ src/
RUN dotnet publish src/EduScroll.API/EduScroll.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage using minimal ASP.NET Core 10 image and built-in non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Container serves HTTP behind a TLS-terminating reverse proxy/ingress by default;
# set HttpsRedirection__Enabled=true only when binding HTTPS directly in Kestrel.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV HttpsRedirection__Enabled=false
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "EduScroll.API.dll"]
