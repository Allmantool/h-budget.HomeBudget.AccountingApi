# API Error Contracts

Accounting API endpoints keep the existing `Result<T>` response envelope:

```json
{
  "payload": null,
  "isSucceeded": false,
  "statusMessage": "Validation failed: Amount must not be zero"
}
```

HTTP status codes are mapped consistently:

- `400 Bad Request`: malformed input, validation failures, invalid GUIDs, invalid enum values.
- `404 Not Found`: valid identifiers that do not resolve to an account, category, contractor, operation, or transfer.
- `409 Conflict`: duplicate handbook keys or other duplicate business keys.
- `500 Internal Server Error`: unexpected server failures, including outbox writes that fail with an unclassified exception.
- `503 Service Unavailable`: infrastructure dependencies are unavailable, including Kafka, EventStoreDB, MongoDB, SQL Server, and timeouts.

Responses must not include stack traces, connection strings, secrets, or raw dependency exception messages.
