---
sidebar_position: 1
title: Docker Deployment
description: Deploy Ignixa using Docker
---

# Docker Deployment

Ignixa provides official Docker images for easy deployment.

## Official Image

Pull from GitHub Container Registry:

```bash
docker pull ghcr.io/brendankowitz/ignixa-fhir:release
```

### Available Tags

| Tag | Description |
|-----|-------------|
| `release` | Latest stable release |
| `latest` | Latest build from main branch |
| `x.y.z` | Specific version |

## Quick Start

### Development (File System Storage)

```bash
docker run -p 8080:8080 ghcr.io/brendankowitz/ignixa-fhir:release
```

Access at `http://localhost:8080/metadata`.

### With Volume Persistence

```bash
docker run -p 8080:8080 \
  -v ignixa-data:/app/data \
  ghcr.io/brendankowitz/ignixa-fhir:release
```

## Docker Compose

For production deployments with SQL Server:

### docker-compose.yml

```yaml
services:
  ignixa:
    image: ghcr.io/brendankowitz/ignixa-fhir:release
    ports:
      - "8080:8080"
    environment:
      - Storage__Provider=SqlServer
      - Storage__ConnectionString=Server=sql;Database=IgnixaFhir;User Id=sa;Password=${SQL_SA_PASSWORD};TrustServerCertificate=true
    depends_on:
      sql:
        condition: service_healthy

  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=${SQL_SA_PASSWORD}
    volumes:
      - sql-data:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "${SQL_SA_PASSWORD}" -C -Q "SELECT 1"
      interval: 10s
      retries: 10

volumes:
  sql-data:
```

### .env File

```bash
SQL_SA_PASSWORD=YourStrong!Passw0rd
```

### Start the Stack

```bash
docker compose up -d
```

## Environment Variables

All configuration can be passed via environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `Storage__Provider` | Storage backend | `FileSystem` |
| `Storage__ConnectionString` | SQL connection string | - |
| `Storage__DataPath` | File system data path | `/app/data` |
| `Tenancy__Mode` | Tenant mode | `SingleTenant` |
| `Validation__Level` | Validation level | `Spec` |
| `ASPNETCORE_URLS` | Listening URLs | `http://+:8080` |

## Health Checks

Ignixa exposes health endpoints:

```bash
# Liveness probe
curl http://localhost:8080/health/live

# Readiness probe
curl http://localhost:8080/health/ready
```

### Docker Health Check

```yaml
healthcheck:
  test: curl -f http://localhost:8080/health/live || exit 1
  interval: 30s
  timeout: 10s
  retries: 3
```

## Resource Limits

Recommended resource limits:

```yaml
services:
  ignixa:
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 2G
        reservations:
          cpus: '0.5'
          memory: 512M
```

## Networking

### Reverse Proxy

Example with Traefik:

```yaml
services:
  ignixa:
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.ignixa.rule=Host(`fhir.example.org`)"
      - "traefik.http.routers.ignixa.tls=true"
```

### Custom Network

```yaml
networks:
  fhir-network:
    driver: bridge

services:
  ignixa:
    networks:
      - fhir-network
  sql:
    networks:
      - fhir-network
```

## Logging

Configure structured logging:

```bash
docker run -p 8080:8080 \
  -e Logging__LogLevel__Default=Information \
  -e Logging__LogLevel__Ignixa=Debug \
  ghcr.io/brendankowitz/ignixa-fhir:release
```

### View Logs

```bash
docker logs -f ignixa
docker compose logs -f ignixa
```

## Building Custom Image

Extend the base image:

```dockerfile
FROM ghcr.io/brendankowitz/ignixa-fhir:release

# Add custom profiles
COPY ./profiles /app/profiles

# Custom configuration
COPY ./appsettings.Production.json /app/appsettings.Production.json

ENV ASPNETCORE_ENVIRONMENT=Production
```

Build and run:

```bash
docker build -t my-ignixa .
docker run -p 8080:8080 my-ignixa
```

## Kubernetes

For Kubernetes deployments, see the Helm charts in the repository or use the following minimal deployment:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ignixa
spec:
  replicas: 2
  selector:
    matchLabels:
      app: ignixa
  template:
    metadata:
      labels:
        app: ignixa
    spec:
      containers:
        - name: ignixa
          image: ghcr.io/brendankowitz/ignixa-fhir:release
          ports:
            - containerPort: 8080
          env:
            - name: Storage__Provider
              value: SqlServer
            - name: Storage__ConnectionString
              valueFrom:
                secretKeyRef:
                  name: ignixa-secrets
                  key: connection-string
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
```

## Related Documentation

- [Configuration](/docs/getting-started/configuration)
- [Azure Deployment](/docs/server/deployment/azure)
