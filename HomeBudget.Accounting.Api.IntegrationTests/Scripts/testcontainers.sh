#!/bin/bash
set -e

echo ">> [Test] Testcontainers.sh executed successfully."

CONFIG="/etc/kafka/server.properties"
LOG_DIRS="/tmp/kraft-combined-logs"

# Cluster ID: use env if present, otherwise generate a new one
if [ -n "$KAFKA_CLUSTER_ID" ]; then
  CLUSTER_ID="$KAFKA_CLUSTER_ID"
  echo "Using provided KAFKA_CLUSTER_ID: $CLUSTER_ID"
else
  CLUSTER_ID=$(kafka-storage random-uuid)
  echo "Generated random CLUSTER_ID: $CLUSTER_ID"
fi

# Derive controllers or use fallback
if [ -n "$KAFKA_CONTROLLER_QUORUM_VOTERS" ]; then
  IFS=',' read -r FIRST_CONTROLLER _ <<< "$KAFKA_CONTROLLER_QUORUM_VOTERS"
  INITIAL_CONTROLLERS="$FIRST_CONTROLLER"
else
  : "${INITIAL_CONTROLLERS:=1@test-kafka:9093}"
fi

echo "------------------------------------------------------------"
echo ">> Kafka startup initiated at $(date)"
echo ">> Config: $CONFIG"
echo ">> Initial controllers: $INITIAL_CONTROLLERS"
echo ">> Cluster ID: $CLUSTER_ID"
echo "------------------------------------------------------------"

if [ "${DEBUG_CONFIG:-false}" = "true" ]; then
  cat "$CONFIG"
fi

echo "=== Checking if storage is formatted ==="
if [ ! -f "$LOG_DIRS/meta.properties" ]; then
  echo "=== Formatting storage for controllers: $INITIAL_CONTROLLERS, cluster.id=$CLUSTER_ID ==="
  kafka-storage format \
    --ignore-formatted \
    --cluster-id "$CLUSTER_ID" \
    --config "$CONFIG" \
    --initial-controllers "$INITIAL_CONTROLLERS:$CLUSTER_ID" \
    || echo "⚠️  Storage format failed (maybe already formatted)"
else
  echo "=== Storage already formatted (cluster.id=$(grep ^cluster.id "$LOG_DIRS/meta.properties" | cut -d'=' -f2)) ==="
fi

echo "=== Starting Kafka ==="
exec kafka-server-start "$CONFIG"
