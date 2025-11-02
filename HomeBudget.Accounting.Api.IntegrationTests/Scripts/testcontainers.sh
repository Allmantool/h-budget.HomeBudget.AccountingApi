#!/bin/bash
set -e

echo ">> [Test] Testcontainers.sh executed successfully."

CONFIG="/etc/kafka/server.properties"
LOG_DIRS="/tmp/kraft-combined-logs"
HOST_IP=$(hostname -I | awk '{print $1}') # First IP address of container

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

# Optional: replace localhost in config with container's IP
if grep -q "localhost" "$CONFIG"; then
  echo "Replacing 'localhost' with '$HOST_IP' in Kafka config"
  sed -i "s/localhost/$HOST_IP/g" "$CONFIG"
fi

echo "------------------------------------------------------------"
echo ">> Kafka startup initiated at $(date)"
echo ">> Host IP: $HOST_IP"
echo ">> Config file: $CONFIG"
echo ">> Log dirs: $LOG_DIRS"
echo ">> Initial controllers: $INITIAL_CONTROLLERS"
echo ">> Cluster ID: $CLUSTER_ID"
echo "------------------------------------------------------------"

if [ "${DEBUG_CONFIG:-true}" = "true" ]; then
  echo
  echo "=== Effective Kafka Configuration Preview ==="
  echo "---------------------------------------------"
  grep -E "^(broker\.id|process\.roles|controller\.listener\.names|listener\.security\.protocol\.map|listeners|advertised.listeners|controller\.quorum\.voters|log\.dirs|node.id|inter\.broker\.listener\.name)" "$CONFIG" || echo "(No key parameters found)"
  echo "---------------------------------------------"
  echo
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
  EXISTING_CLUSTER_ID=$(grep ^cluster.id "$LOG_DIRS/meta.properties" | cut -d'=' -f2)
  echo "=== Storage already formatted (cluster.id=$EXISTING_CLUSTER_ID) ==="
fi

echo
echo "=== Final Startup Summary ==="
echo "Node ID: $(grep ^node.id "$CONFIG" | cut -d'=' -f2)"
echo "Process Roles: $(grep ^process.roles "$CONFIG" | cut -d'=' -f2)"
echo "Listeners: $(grep ^listeners "$CONFIG" | cut -d'=' -f2)"
echo "Advertised Listeners: $(grep ^advertised.listeners "$CONFIG" | cut -d'=' -f2)"
echo "Controller Quorum Voters: $(grep ^controller.quorum.voters "$CONFIG" | cut -d'=' -f2)"
echo

echo "=== Starting Kafka ==="
exec kafka-server-start "$CONFIG"
