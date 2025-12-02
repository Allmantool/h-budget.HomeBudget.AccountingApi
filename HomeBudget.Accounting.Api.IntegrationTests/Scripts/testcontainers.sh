#!/bin/bash
set -e

echo ">> [Testcontainers] testcontainers.sh executed successfully."

CONFIG="/etc/kafka/server.properties"
LOG_DIRS="/tmp/kraft-combined-logs"
HOST_IP=$(hostname -I | awk '{print $1}') # First container IP address

echo
echo "------------------------------------------------------------"
echo ">> Kafka startup initiated at $(date)"
echo ">> Host IP: $HOST_IP"
echo ">> Config file: $CONFIG"
echo ">> Log dirs: $LOG_DIRS"
echo "------------------------------------------------------------"
echo

# --- Cluster ID handling ---
if [ -n "$KAFKA_CLUSTER_ID" ]; then
  CLUSTER_ID="$KAFKA_CLUSTER_ID"
  echo "Using provided KAFKA_CLUSTER_ID: $CLUSTER_ID"
else
  CLUSTER_ID=$(kafka-storage random-uuid)
  echo "Generated random cluster ID: $CLUSTER_ID"
fi

# --- Controller voters ---
if [ -n "$KAFKA_CONTROLLER_QUORUM_VOTERS" ]; then
  IFS=',' read -r FIRST_CONTROLLER _ <<< "$KAFKA_CONTROLLER_QUORUM_VOTERS"
  INITIAL_CONTROLLERS="$FIRST_CONTROLLER"
else
  : "${INITIAL_CONTROLLERS:=1@test-kafka:9093}"
fi

# --- Update Kafka listeners dynamically ---
# Replace localhost in config, if any
if grep -q "localhost" "$CONFIG"; then
  echo "Replacing 'localhost' with '$HOST_IP' in Kafka config..."
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
# Inject listeners & advertised listeners for both internal and external access
echo "Updating listeners and advertised.listeners in Kafka config..."
sed -i '/^listeners=/d' "$CONFIG"
sed -i '/^advertised.listeners=/d' "$CONFIG"
sed -i '/^listener.security.protocol.map=/d' "$CONFIG"
sed -i '/^inter.broker.listener.name=/d' "$CONFIG"
sed -i '/^controller.quorum.voters=/d' "$CONFIG"

cat <<EOF >> "$CONFIG"
listeners=PLAINTEXT://0.0.0.0:29092,PLAINTEXT_HOST://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093
advertised.listeners=PLAINTEXT://test-kafka:29092,PLAINTEXT_HOST://localhost:9092
listener.security.protocol.map=PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT,CONTROLLER:PLAINTEXT
controller.quorum.voters=1@test-kafka:9093
inter.broker.listener.name=PLAINTEXT
EOF

echo
echo "=== Effective Kafka configuration preview ==="
grep -E "^(process\.roles|node\.id|controller\.listener\.names|listeners|advertised.listeners|listener\.security\.protocol\.map|controller\.quorum\.voters|inter\.broker\.listener\.name)" "$CONFIG" || echo "(no key params found)"
echo

# --- Format storage if needed ---
echo "=== Checking if storage is formatted ==="
if [ ! -f "$LOG_DIRS/meta.properties" ]; then
  echo "Formatting KRaft storage with cluster.id=$CLUSTER_ID and controllers=$INITIAL_CONTROLLERS ..."
  kafka-storage format \
    --ignore-formatted \
    --cluster-id "$CLUSTER_ID" \
    --config "$CONFIG" \
    || echo "⚠️  Storage format failed (maybe already formatted)"
else
  EXISTING_CLUSTER_ID=$(grep ^cluster.id "$LOG_DIRS/meta.properties" | cut -d'=' -f2)
  echo "Storage already formatted (cluster.id=$EXISTING_CLUSTER_ID)"
fi

echo
echo "=== Final Startup Summary ==="
echo "Node ID: $(grep ^node.id "$CONFIG" | cut -d'=' -f2)"
echo "Process Roles: $(grep ^process.roles "$CONFIG" | cut -d'=' -f2)"
echo "Listeners: $(grep ^listeners "$CONFIG" | cut -d'=' -f2)"
echo "Advertised Listeners: $(grep ^advertised.listeners "$CONFIG" | cut -d'=' -f2)"
echo "Controller Quorum Voters: $(grep ^controller.quorum.voters "$CONFIG" | cut -d'=' -f2)"
grep -E "^(node\.id|process\.roles|listeners|advertised.listeners|controller\.quorum\.voters)" "$CONFIG" || true
echo

echo "=== Starting Kafka ==="
exec kafka-server-start "$CONFIG"
