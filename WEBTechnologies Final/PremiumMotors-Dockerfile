# PremiumMotors - ASP.NET Core 9 production container
# This file belongs in the same directory as:
#   WEBTechnologies Final.csproj

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy the project file first so dependency restore can be cached by Docker.
COPY ["WEBTechnologies Final.csproj", "./"]
RUN dotnet restore "./WEBTechnologies Final.csproj"

# Copy the application source.
COPY . .

# Publish a framework-dependent ASP.NET Core application into /app/publish.
RUN dotnet publish "./WEBTechnologies Final.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
# Render routes traffic to the port exposed by the container.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000

COPY --from=build /app/publish .

EXPOSE 10000

ENTRYPOINT ["dotnet", "WEBTechnologies Final.dll"]
