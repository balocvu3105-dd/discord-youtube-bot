# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

# Run stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Tạo thư mục data/ trong image
# (sẽ bị override bởi Docker volume mount, nhưng tốt để có sẵn)
RUN mkdir -p /app/data /app/logs

ENTRYPOINT ["dotnet", "YouTubeDiscordBot.dll"]