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

# Cài Python 3 + TikTokLive để chạy tiktok_check.py
RUN apt-get update && apt-get install -y python3 python3-pip --no-install-recommends \
    && pip3 install --break-system-packages TikTokLive==6.6.5 \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Copy script Python vào image
COPY tiktok_check.py /app/tiktok_check.py

ENTRYPOINT ["dotnet", "YouTubeDiscordBot.dll"]