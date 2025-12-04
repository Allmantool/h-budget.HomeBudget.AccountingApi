#!/bin/bash
set -e

# -------------------------------------------------------------------------
# To work correctly with bash, should be stored like UTF-8 without BOM  (Encoding) + LF (End of line sequence)
# -------------------------------------------------------------------------

CONFIG="/etc/kafka/server.properties"
LOG_DIRS="/tmp/kraft-combined-logs"

echo "------------------------------------------------------------"
echo ">> Kafka startup initiated at $(date)"
echo "------------------------------------------------------------"

# -------------------------------------------------------------------------
# Environment variables from .NET
# -------------------------------------------------------------------------
# TC_HOST       -> external host IP (for clients outside Docker network)
# C_HOST_NAME   -> internal container hostname (for inter-container communication)
# KAFKA_INTERNAL_PORT / KAFKA_EXTERNAL_PORT -> mapped ports

HOST_IP="${TC_HOST:-$(hostname -I | awk '{print $1}')}"
INTERNAL_HOST="${C_HOST_NAME:-test-kafka}"
INTERNAL_PORT="${KAFKA_INTERNAL_PORT:-29092}"
EXTERNAL_PORT="${KAFKA_EXTERNAL_PORT:-9092}"
CONTROLLER_PORT=9093

echo "HOST_IP=$HOST_IP (external)"
echo "INTERNAL_HOST=$INTERNAL_HOST (internal)"
echo "Internal port (mapped) : $INTERNAL_PORT"
echo "External port (mapped) : $EXTERNAL_PORT"

# -------------------------------------------------------------------------
# Clean existing related config keys
# -------------------------------------------------------------------------
sed -i '/^listeners=/d' "$CONFIG"
sed -i '/^advertised.listeners=/d' "$CONFIG"
sed -i '/^listener.security.protocol.map=/d' "$CONFIG"
sed -i '/^controller.quorum.voters=/d' "$CONFIG"
sed -i '/^inter.broker.listener.name=/d' "$CONFIG"

# -------------------------------------------------------------------------
# Write dynamic listener configuration
# -------------------------------------------------------------------------
cat <<EOF >> "$CONFIG"
# Listeners binding to all interfaces
listeners=PLAINTEXT_INTERNAL://0.0.0.0:${INTERNAL_PORT},PLAINTEXT_EXTERNAL://0.0.0.0:${EXTERNAL_PORT},CONTROLLER://0.0.0.0:${CONTROLLER_PORT}

# Advertised listeners
# INTERNAL -> use container hostname for other containers
# EXTERNAL -> use host IP for external clients
advertised.listeners=PLAINTEXT_INTERNAL://${INTERNAL_HOST}:${INTERNAL_PORT},PLAINTEXT_EXTERNAL://${HOST_IP}:${EXTERNAL_PORT}

listener.security.protocol.map=PLAINTEXT_INTERNAL:PLAINTEXT,PLAINTEXT_EXTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT

# KRaft requirements
controller.quorum.voters=1@${INTERNAL_HOST}:${CONTROLLER_PORT}
inter.broker.listener.name=PLAINTEXT_INTERNAL
EOF

echo "=== Final Kafka config ==="
grep -E "^(listeners|advertised.listeners|controller.quorum.voters)" "$CONFIG"

# -------------------------------------------------------------------------
# Format storage (only once)
# -------------------------------------------------------------------------
if [ ! -f "$LOG_DIRS/meta.properties" ]; then
  CLUSTER_ID="${KAFKA_CLUSTER_ID:-$(kafka-storage random-uuid)}"
  echo "Formatting storage with cluster ID: $CLUSTER_ID"
  kafka-storage format --cluster-id "$CLUSTER_ID" --config "$CONFIG" --ignore-formatted
fi

echo "=== Starting Kafka ==="
exec kafka-server-start "$CONFIG"
