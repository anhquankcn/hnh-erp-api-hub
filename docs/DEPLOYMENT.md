# Deployment Guide

This guide covers Docker Compose startup, required environment variables, health checks, and monitoring endpoints for Sprint 5.

## Docker Compose

Create a local environment file from the sample:

```sh
cp .env.example .env
```

Edit `.env` and set production-safe values for database, RabbitMQ, and Keycloak secrets. Then build and start the stack:

```sh
docker compose up --build
```

To run in the background:

```sh
docker compose up --build -d
```

To stop the stack:

```sh
docker compose down
```

The default compose stack starts:

- `api` on `${API_PORT:-8008}`
- `postgres` on `${POSTGRES_PORT:-5432}`
- `redis` on `${REDIS_PORT:-6379}`
- `rabbitmq` on `${RABBITMQ_PORT:-5672}`
- RabbitMQ management UI on `${RABBITMQ_MANAGEMENT_PORT:-15672}`

## Environment Variables

Use [.env.example](../.env.example) as the reference for required values.

| Variable | Purpose |
|----------|---------|
| `API_PORT` | Host port mapped to API container port `8008`. |
| `DATABASE_CONNECTION_STRING` | PostgreSQL connection string used by the API. |
| `POSTGRES_DB` | PostgreSQL database name. |
| `POSTGRES_USER` | PostgreSQL username. |
| `POSTGRES_PASSWORD` | PostgreSQL password. |
| `REDIS_CONNECTION_STRING` | Redis connection string. |
| `RABBITMQ_USERNAME` | RabbitMQ username. |
| `RABBITMQ_PASSWORD` | RabbitMQ password. |
| `RABBITMQ_VIRTUAL_HOST` | RabbitMQ virtual host. |
| `RABBITMQ_EXCHANGE_NAME` | RabbitMQ event exchange name. |
| `KEYCLOAK_AUTHORITY` | Keycloak realm authority URL. |
| `KEYCLOAK_AUDIENCE` | JWT audience expected by the API. |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | Require HTTPS metadata for Keycloak discovery in production. |
| `JWT_ISSUER` | Expected JWT issuer, matching the Keycloak realm. |
| `JWT_AUDIENCE` | Expected JWT audience. |

Do not use the sample passwords from `.env.example` in shared or production environments.

## Health Check Endpoints

The API exposes production probes on port `8008`:

| Endpoint | Purpose |
|----------|---------|
| `/health` | Aggregate health response. |
| `/health/live` | Liveness probe. |
| `/health/ready` | Readiness probe for dependencies tagged as ready. |
| `/health/startup` | Startup probe for dependencies tagged as startup. |
| `/api/v1/health` | Deprecated v1 health endpoint. |
| `/api/v2/health` | v2 health endpoint with uptime metadata. |
| `/api/v2/health/detailed` | v2 detailed dependency health report. |

Example checks:

```sh
curl http://localhost:8008/health/live
curl http://localhost:8008/health/ready
curl http://localhost:8008/api/v2/health/detailed
```

## Monitoring

The API publishes Prometheus metrics at:

```text
http://localhost:8008/metrics
```

Prometheus config is available at [infrastructure/monitoring/prometheus.yml](../infrastructure/monitoring/prometheus.yml). Grafana dashboard JSON is available at [infrastructure/monitoring/grafana-dashboard.json](../infrastructure/monitoring/grafana-dashboard.json).

Run Prometheus on the Docker Compose network after the API stack is up. The checked-in Prometheus config targets `erp-api-hub:8008`; for the local compose service name, either change the scrape target to `api:8008` in a local copy or provide an `erp-api-hub` network alias.

```sh
docker run --rm --network sprint-5-a_default \
  -p 9090:9090 \
  -v "$PWD/infrastructure/monitoring/prometheus.yml:/etc/prometheus/prometheus.yml:ro" \
  prom/prometheus
```

Run Grafana:

```sh
docker run --rm --network sprint-5-a_default \
  -p 3000:3000 \
  grafana/grafana
```

Then configure Grafana with a Prometheus data source and import `infrastructure/monitoring/grafana-dashboard.json`.

## API Versioning

Version discovery is available at:

```text
GET /versions
```

The API supports `/api/v1` and `/api/v2`. Successful `/api/v1` responses include:

```text
Sunset: Sat, 31 Dec 2026 00:00:00 GMT
Deprecation: true
```

New integrations should use `/api/v2` when an endpoint has a v2 equivalent.
