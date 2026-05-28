#!/usr/bin/env sh
set -eu

POSTGRES_HOST="${POSTGRES_HOST:-postgres}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"
REDIS_HOST="${REDIS_HOST:-redis}"
REDIS_PORT="${REDIS_PORT:-6379}"
RABBITMQ_HOST="${RABBITMQ_HOST:-rabbitmq}"
RABBITMQ_PORT="${RABBITMQ_PORT:-5672}"
TIMEOUT_SECONDS="${HEALTH_CHECK_TIMEOUT_SECONDS:-60}"
SLEEP_SECONDS="${HEALTH_CHECK_SLEEP_SECONDS:-2}"

check_port() {
  name="$1"
  host="$2"
  port="$3"

  if nc -z "$host" "$port"; then
    printf '%s is reachable at %s:%s\n' "$name" "$host" "$port"
    return 0
  fi

  printf '%s is not reachable at %s:%s\n' "$name" "$host" "$port" >&2
  return 1
}

wait_for_port() {
  name="$1"
  host="$2"
  port="$3"
  elapsed=0

  while [ "$elapsed" -lt "$TIMEOUT_SECONDS" ]; do
    if check_port "$name" "$host" "$port"; then
      return 0
    fi

    sleep "$SLEEP_SECONDS"
    elapsed=$((elapsed + SLEEP_SECONDS))
  done

  printf 'Timed out after %ss waiting for %s at %s:%s\n' "$TIMEOUT_SECONDS" "$name" "$host" "$port" >&2
  return 1
}

wait_for_port "postgres" "$POSTGRES_HOST" "$POSTGRES_PORT"
wait_for_port "redis" "$REDIS_HOST" "$REDIS_PORT"
wait_for_port "rabbitmq" "$RABBITMQ_HOST" "$RABBITMQ_PORT"

printf 'All dependencies are reachable.\n'

if [ "$#" -gt 0 ]; then
  exec "$@"
fi
