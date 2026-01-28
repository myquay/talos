# 09 - Deployment

## Overview

This document covers deployment strategies for Talos, including Docker containerization, reverse proxy configuration, and production considerations. Talos requires GitHub OAuth credentials but no user configuration.

## Deployment Options

1. **Docker Container** (Recommended)
2. **Direct .NET Runtime** (Linux/Windows service)
3. **Platform-as-a-Service** (Azure App Service, etc.)

## Prerequisites

Before deploying Talos, you need:

1. **GitHub OAuth App** - Create at https://github.com/settings/developers
   - Set callback URL to `https://your-domain.com/callback/github`
   - Note the Client ID and Client Secret

2. **Domain with HTTPS** - Required for IndieAuth
3. **Reverse proxy** (Caddy, Nginx, or Traefik) for TLS termination

## Docker Deployment

### Dockerfile

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Install Node.js for Vue.js build
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs

WORKDIR /src

# Copy solution and project files
COPY talos.sln .
COPY src/Talos.Web/Talos.Web.csproj src/Talos.Web/
COPY src/Talos.Core/Talos.Core.csproj src/Talos.Core/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ src/

# Build Vue.js app
WORKDIR /src/src/Talos.Web/ClientApp
RUN npm ci
RUN npm run build

# Build .NET app
WORKDIR /src
RUN dotnet publish src/Talos.Web/Talos.Web.csproj -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

# Create non-root user
RUN addgroup --system --gid 1001 talos \
    && adduser --system --uid 1001 --gid 1001 talos

# Create data directory
RUN mkdir -p /data && chown talos:talos /data

COPY --from=build /app/publish .

# Switch to non-root user
USER talos

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__DefaultConnection="Data Source=/data/talos.db"

ENTRYPOINT ["dotnet", "Talos.Web.dll"]
```

### Docker Compose

```yaml
# docker-compose.yml
version: '3.8'

services:
  talos:
    build: .
    container_name: talos
    restart: unless-stopped
    ports:
      - "8080:8080"
    volumes:
      - talos-data:/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - GitHub__ClientId=${GITHUB_CLIENT_ID}
      - GitHub__ClientSecret=${GITHUB_CLIENT_SECRET}
      - Jwt__SecretKey=${JWT_SECRET_KEY}
      - Talos__BaseUrl=https://talos.example.com
    env_file:
      - .env
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3

volumes:
  talos-data:
```

### Environment File

```bash
# .env (not committed to git!)
GITHUB_CLIENT_ID=your-github-client-id
GITHUB_CLIENT_SECRET=your-github-client-secret
JWT_SECRET_KEY=your-256-bit-secret-key-minimum-32-characters
```

### Build and Run

```bash
# Build image
docker build -t talos:latest .

# Run with Docker Compose
docker-compose up -d

# View logs
docker-compose logs -f talos

# Stop
docker-compose down
```

## Reverse Proxy Configuration

### Caddy (Recommended)

```caddyfile
# Caddyfile
talos.example.com {
    reverse_proxy localhost:8080
    
    # Automatic HTTPS via Let's Encrypt
    tls {
        email admin@example.com
    }
    
    # Security headers
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
        Referrer-Policy "strict-origin-when-cross-origin"
    }
    
    # Logging
    log {
        output file /var/log/caddy/talos.log
    }
}
```

### Nginx

```nginx
# /etc/nginx/sites-available/talos
server {
    listen 80;
    server_name talos.example.com;
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name talos.example.com;

    # SSL configuration
    ssl_certificate /etc/letsencrypt/live/talos.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/talos.example.com/privkey.pem;
    ssl_session_timeout 1d;
    ssl_session_cache shared:SSL:50m;
    ssl_session_tickets off;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
    ssl_prefer_server_ciphers off;

    # HSTS
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains; preload" always;

    # Security headers
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "DENY" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    location / {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

### Traefik (Docker)

```yaml
# docker-compose.yml with Traefik
version: '3.8'

services:
  traefik:
    image: traefik:v2.10
    command:
      - "--providers.docker=true"
      - "--providers.docker.exposedbydefault=false"
      - "--entrypoints.web.address=:80"
      - "--entrypoints.websecure.address=:443"
      - "--certificatesresolvers.letsencrypt.acme.httpchallenge=true"
      - "--certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web"
      - "--certificatesresolvers.letsencrypt.acme.email=admin@example.com"
      - "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json"
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - letsencrypt:/letsencrypt
    restart: unless-stopped

  talos:
    build: .
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.talos.rule=Host(`talos.example.com`)"
      - "traefik.http.routers.talos.entrypoints=websecure"
      - "traefik.http.routers.talos.tls.certresolver=letsencrypt"
      - "traefik.http.middlewares.talos-redirect.redirectscheme.scheme=https"
      - "traefik.http.routers.talos-http.rule=Host(`talos.example.com`)"
      - "traefik.http.routers.talos-http.entrypoints=web"
      - "traefik.http.routers.talos-http.middlewares=talos-redirect"
    environment:
      - GitHub__ClientId=${GITHUB_CLIENT_ID}
      - GitHub__ClientSecret=${GITHUB_CLIENT_SECRET}
      - Jwt__SecretKey=${JWT_SECRET_KEY}
      - Talos__BaseUrl=https://talos.example.com
    volumes:
      - talos-data:/data
    restart: unless-stopped

volumes:
  letsencrypt:
  talos-data:
```

## Production Configuration

### appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Talos": "Information"
    }
  },
  
  "GitHub": {
    "ClientId": "",
    "ClientSecret": "",
    "AuthorizationEndpoint": "https://github.com/login/oauth/authorize",
    "TokenEndpoint": "https://github.com/login/oauth/access_token",
    "UserApiEndpoint": "https://api.github.com/user"
  },
  
  "Jwt": {
    "Issuer": "https://talos.example.com/",
    "Audience": "https://talos.example.com/",
    "SecretKey": "",
    "AccessTokenExpirationMinutes": 15
  },
  
  "IndieAuth": {
    "AuthorizationCodeExpirationMinutes": 10,
    "RefreshTokenExpirationDays": 30,
    "PendingAuthenticationExpirationMinutes": 30
  },
  
  "Talos": {
    "BaseUrl": "https://talos.example.com"
  },
  
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/talos.db"
  }
}
```

### Environment Variables

All sensitive configuration should be via environment variables:

```bash
# Required
GitHub__ClientId=your-github-client-id
GitHub__ClientSecret=your-github-client-secret
Jwt__SecretKey=your-256-bit-secret-key-minimum-32-characters
Talos__BaseUrl=https://talos.example.com

# Optional overrides
Jwt__Issuer=https://talos.example.com/
Jwt__Audience=https://talos.example.com/
ConnectionStrings__DefaultConnection=Data Source=/data/talos.db
```

### Generate Secure Keys

```bash
# Generate JWT secret key (32+ characters)
openssl rand -base64 32
```

## Health Checks

### Health Endpoint

```csharp
// In Program.cs
app.MapHealthChecks("/health");

// Or with detailed checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<TalosDbContext>();
```

### Docker Health Check

```dockerfile
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
```

## Logging

### Structured Logging with Serilog

```csharp
// Install packages
// dotnet add package Serilog.AspNetCore
// dotnet add package Serilog.Sinks.Console
// dotnet add package Serilog.Sinks.File

// In Program.cs
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: "/var/log/talos/talos-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30);
});
```

## Backup Strategy

### Automated Backup Script

```bash
#!/bin/bash
# backup-talos.sh

BACKUP_DIR="/backups/talos"
DATE=$(date +%Y%m%d_%H%M%S)
DB_PATH="/data/talos.db"

# Create backup directory
mkdir -p "$BACKUP_DIR"

# Backup SQLite database
sqlite3 "$DB_PATH" ".backup '$BACKUP_DIR/talos_$DATE.db'"

# Compress
gzip "$BACKUP_DIR/talos_$DATE.db"

# Keep only last 30 days
find "$BACKUP_DIR" -name "talos_*.db.gz" -mtime +30 -delete

echo "Backup completed: talos_$DATE.db.gz"
```

### Cron Schedule

```bash
# /etc/cron.d/talos-backup
0 2 * * * root /opt/talos/backup-talos.sh >> /var/log/talos-backup.log 2>&1
```

## Monitoring

### Basic Monitoring Script

```bash
#!/bin/bash
# monitor-talos.sh

URL="https://talos.example.com/health"
WEBHOOK="https://your-alerting-service/webhook"

response=$(curl -s -o /dev/null -w "%{http_code}" "$URL")

if [ "$response" != "200" ]; then
    curl -X POST "$WEBHOOK" \
        -H "Content-Type: application/json" \
        -d "{\"text\": \"Talos health check failed! Status: $response\"}"
fi
```

## Security Checklist for Production

- [ ] **HTTPS enforced** - All traffic over TLS 1.2+
- [ ] **Strong JWT secret** - At least 32 characters, randomly generated
- [ ] **GitHub credentials secure** - In environment variables, not in code
- [ ] **Database secured** - File permissions set correctly
- [ ] **Non-root user** - Container runs as non-root
- [ ] **Secrets not in code** - Use environment variables
- [ ] **Rate limiting enabled** - Protect against brute force
- [ ] **Logs configured** - No sensitive data logged
- [ ] **Backups automated** - Regular database backups
- [ ] **Updates planned** - Process for security updates

## GitHub OAuth App Setup

1. Go to https://github.com/settings/developers
2. Click "New OAuth App"
3. Fill in:
   - **Application name**: Talos IndieAuth (or your name)
   - **Homepage URL**: `https://talos.example.com`
   - **Authorization callback URL**: `https://talos.example.com/callback/github`
4. Click "Register application"
5. Copy the **Client ID**
6. Generate and copy a **Client Secret**
7. Add these to your `.env` file or environment variables

## Deployment Commands Cheat Sheet

```bash
# Build and deploy
docker-compose build
docker-compose up -d

# View logs
docker-compose logs -f

# Restart
docker-compose restart talos

# Update (with zero downtime)
docker-compose pull
docker-compose up -d

# Backup before update
docker exec talos sqlite3 /data/talos.db ".backup '/data/backup.db'"

# Shell into container
docker exec -it talos /bin/sh

# Check health
curl -f https://talos.example.com/health

# Test auth flow
open "https://talos.example.com/auth?response_type=code&client_id=https://test.example.com/&redirect_uri=https://test.example.com/callback&state=test&code_challenge=test&code_challenge_method=S256&me=https://your-website.com/"
```

## Troubleshooting

### Common Issues

1. **GitHub OAuth error**: Check callback URL matches exactly
2. **Database locked**: Only one instance should access SQLite
3. **Certificate issues**: Ensure Let's Encrypt can reach port 80
4. **Discovery fails**: User's site must be reachable and have rel="me" links

### Debug Mode

```bash
# Run with debug logging
docker run -e ASPNETCORE_ENVIRONMENT=Development \
           -e Logging__LogLevel__Default=Debug \
           talos:latest
```

## Summary

Talos is now ready for production deployment. The recommended setup is:

1. **Docker container** for the application
2. **Caddy or Nginx** as reverse proxy with automatic HTTPS
3. **SQLite** persisted in a Docker volume
4. **Environment variables** for GitHub OAuth and JWT secrets
5. **Automated backups** via cron
6. **Health monitoring** via /health endpoint

The key difference from traditional auth servers: **no user configuration required** - users authenticate via their own website's identity providers.
