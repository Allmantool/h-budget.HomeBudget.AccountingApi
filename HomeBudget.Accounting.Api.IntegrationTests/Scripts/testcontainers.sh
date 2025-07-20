#!/bin/bash
set -e

echo ">> [Test] Testcontainers.sh executed successfully."

CONFIG=/etc/kafka/server.properties
LOG_DIRS=/tmp/kraft-combined-logs

# Cluster ID: use env if present, otherwise generate a new one
if [ -n "$KAFKA_CLUSTER_ID" ]; then
  CLUSTER_ID="$KAFKA_CLUSTER_ID"
  echo "Using provided KAFKA_CLUSTER_ID: $CLUSTER_ID"
else
  CLUSTER_ID=$(kafka-storage random-uuid)
  echo "Generated random CLUSTER_ID: $CLUSTER_ID"
fi

# Derive the first controller (fallback to localhost:9092 if unset)
if [ -n "$KAFKA_CONTROLLER_QUORUM_VOTERS" ]; then
  IFS=',' read -r FIRST_CONTROLLER _ <<< "$KAFKA_CONTROLLER_QUORUM_VOTERS"
  INITIAL_CONTROLLERS="$FIRST_CONTROLLER"
else
  INITIAL_CONTROLLERS="1@localhost:9092"
fi

echo "=== Using Kafka config file at: $CONFIG ==="
cat "$CONFIG"

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
kafka-server-start "$CONFIG" || echo "⚠️  Kafka exited with error, container remains running"

# keep container alive for debugging
tail -f /dev/null
