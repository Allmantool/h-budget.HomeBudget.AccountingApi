# Payments Consumer Observability

## Status

Implemented and locally verified for Prometheus, Jaeger, deploy Alloy/Tempo tail
sampling, and Loki. Seq remains a runtime verification gap; do not describe the whole
observability path as end-to-end verified until its backend query passes.

## Problem

The Accounting API exports traces to the configured OTLP receiver, exposes Prometheus metrics, and sends structured logs to configured sinks. The payments consumer worker contains substantial instrumentation, but its HTTP listener and the Prometheus discovery path do not currently make `/metrics` reliably reachable from the Compose monitoring stack. Metrics registration is also coupled to the trace-export endpoint. Current instrumentation lacks explicit bounded success/throughput measurements for the Kafka and projection stages, and EventStore append failures use the unbounded stream id as a metric label.

## Goal

Make worker traces, metrics, and structured logs observable through the existing local and deploy Home Ledger monitoring stacks, with a distinct stable service identity, causally correct payment spans, bounded worker metrics, and trace-correlated logs.

## Non-Goals

- Changing payment delivery, Kafka commit, outbox/inbox idempotency, EventStoreDB append, persistent-subscription acknowledgement, or Mongo projection semantics.
- Deploying to or modifying `vm2.linux` or any of its data stores.
- Adding a second telemetry backend or duplicate exporters.
- Redesigning the existing payment pipeline.

## Pre-change Repository Findings

### Confirmed

- Both hosts use `TryAddTracingSupport`; it registers OTLP tracing and Prometheus metrics only when `ObservabilityOptions:TelemetryEndpoint` is non-empty.
- The API listens on Compose port 80 and is statically scraped at `homebudget-accounting-api:80/metrics`.
- The worker calls `UseUrls("http://127.0.0.1:0")`, while deploy Compose advertises port 80. Its metrics endpoint is therefore not reliably reachable from another container.
- Prometheus config uses Docker service discovery for workers, but the Prometheus container has no Docker socket mount. The scrape job cannot discover worker replicas.
- The worker is already connected to `accounting-api-network`, which is also attached to Prometheus.
- Local Compose mounts the API appsettings file into the worker; deploy Compose uses the dedicated worker settings path.
- Traces are exported to Jaeger locally and to Alloy/Tempo in deploy. Alloy applies tail sampling: all error/slow traces plus a 5% baseline.
- Logs use structured Serilog console output, optional Seq, and optional OTLP logs. Deploy Alloy also collects Docker stdout into Loki, so an OTLP log sink must not be added unless explicitly configured.
- API-to-Kafka propagation persists W3C `traceparent`, `tracestate`, and baggage through the outbox and Kafka headers. Worker Kafka and EventStore consumers extract that context. Batched projection work uses one parent and links for the remaining causal contexts.
- Existing metrics cover Kafka failure/lag, EventStore retry/failure/duration/dead-letter, projection duration/failure/delay/lag, and Mongo duration, but not bounded Kafka received/processed success counters or an explicit projection processed counter.
- `homebudget.eventstore.append.failures` currently uses `stream_id`, an unbounded account/month-derived label, in two call sites.

### Assumed

- Static DNS targeting the Compose service name is sufficient for the configured single local worker and for deploy replicas because Docker DNS resolves service tasks; runtime verification will confirm the actual target behavior.
- The mounted deployment settings enable either Seq or Docker stdout collection for logs; local checked-in settings default Seq on but leave its URI empty.

### Unknown / Open Questions

- Whether all required images and secrets for a full isolated Compose payment run are available locally.
- Whether Prometheus preserves more than one worker replica behind a single service target; if replica-level visibility is required, explicit task discovery outside plain Compose may remain a gap.

## Pre-change and Desired Behavior

Today the worker can create and export spans when an OTLP trace endpoint is configured, writes structured logs, and defines useful metrics, but its Prometheus endpoint is coupled to tracing and is unreachable at the address Prometheus expects. Desired behavior is an independently enabled metrics pipeline on a stable container port, a reachable Prometheus target, stable worker resource identity across signals, bounded stage metrics, and trace/log correlation without altering payment semantics.

## Architecture and Consistency Context

- SQL outbox is the durable API-side acceptance point; Kafka publication occurs asynchronously.
- Kafka manual commit happens only after the worker message callback returns. The inbox and deterministic EventStore event identity protect duplicate delivery.
- EventStoreDB is the payment event source of truth. The persistent subscription defers acknowledgement until projection processing succeeds; failures request retry.
- MongoDB payment history is a read model and not the payment write authority.
- The trace crosses durable asynchronous boundaries. Persisted/extracted W3C context is used as a remote parent when available; independently batched events are represented with links rather than a manufactured linear parent chain.

## Requirements

- REQ-001: Worker traces reach the configured Jaeger or Alloy/Tempo backend with a distinct stable `service.name` and consistent resource attributes.
- REQ-002: Worker metrics are exposed independently of trace exporter configuration and are reachable by Prometheus on the configured Compose network and port.
- REQ-003: Kafka consume/process, EventStore append, projection, retry/dead-letter, and reliable lag signals use bounded dimensions.
- REQ-004: Kafka and EventStore context extraction preserves causal parentage and batch links.
- REQ-005: Structured worker logs reach the configured destination and include trace/span identifiers when a span exists, without duplicate delivery or sensitive payload logging.
- REL-001: Kafka commit, inbox/idempotency, EventStore append, persistent-subscription settlement, and Mongo projection behavior remain unchanged.
- NFR-001: No metric label contains account, operation, command, message, stream, or other unbounded identifiers.
- NFR-002: Existing Accounting API telemetry remains functional.

## Acceptance Criteria

- AC-001: Prometheus reports the worker target healthy and worker metrics change after a test payment.
- AC-002: A payment API request produces discoverable worker spans causally connected to the API trace when propagated context exists.
- AC-003: Worker logs are queryable in the configured log backend and correlate by trace id.
- AC-004: API `/metrics`, traces, and logs continue to work.
- AC-005: Focused context, metrics, host configuration, and payment pipeline tests pass; relevant projects build.

## Failure Scenarios and Edge Cases

- FAIL-001: Missing trace endpoint must disable only trace export, not worker metrics.
- FAIL-002: Invalid or absent W3C headers must start a new worker trace rather than fabricate a parent.
- FAIL-003: Retried/redelivered events retain causal context and record bounded retry/failure outcomes.
- EDGE-001: A projection batch containing multiple causal contexts uses one deterministic parent and span links for the rest.

## Test Strategy

Use unit/component tests for W3C extraction, parent/link resolution, metric instruments and labels, and host endpoint enablement. Use the existing Testcontainers integration fixture for Kafka/EventStore/Mongo behavior where executable. Use an isolated Compose project plus Prometheus, Jaeger or Tempo, and Loki/Seq HTTP APIs for runtime evidence, without deleting volumes or touching remote systems.

## Implementation Plan

1. Add focused failing tests for metrics registration independent of tracing and bounded stage instrumentation where practical.
2. Split trace and metrics enablement, bind the worker to a stable Compose listener, and replace broken worker scrape discovery with the smallest reachable target.
3. Add missing bounded counters/durations and remove the unbounded EventStore stream label.
4. Align worker service identity and logs across application and Compose configuration without adding duplicate sinks.
5. Build/test, validate Compose, run isolated end-to-end verification if local prerequisites permit, inspect diffs, and complete independent review.

## Verification Strategy

- `dotnet build HomeBudgetAccountingApi.sln --configuration Release --no-incremental`
- `dotnet test HomeBudget.Components.Operations.Tests/HomeBudget.Components.Operations.Tests.csproj --configuration Release`
- Relevant integration tests for worker tracing and payment projection.
- `Orchestration/scripts/validate-compose.ps1` and resolved local/deploy Compose configuration.
- Isolated Compose runtime queries against Prometheus targets/query API, Jaeger or Tempo search API, and Loki or Seq API.
- TDD exception: Compose reachability and collector wiring are configuration behavior best proven by config validation and runtime checks rather than a unit test.

## Requirement Traceability

| Requirement / Criterion | Implementation | Test or evidence | Status |
|---|---|---|---|
| REQ-001 | Shared telemetry registration; worker/Compose identity | Resource/config inspection; activity listener context test | Verified in process/config |
| REQ-002 | Worker host and Prometheus/Compose configuration | Live worker host `/metrics` test; Prometheus config validation | Verified in process/config |
| REQ-003 / NFR-001 | `TelemetryMetrics` and stage call sites | Metric listener tests; source/diff audit | Verified |
| REQ-004 | Existing propagation helpers and stage spans | Kafka parentage and projection batch-link tests | Verified |
| REQ-005 | Shared Serilog configuration and Compose log path | Serilog/Alloy/Seq/Grafana configuration inspection | Config verified; backend query pending |
| REL-001 | No delivery-boundary changes | Focused diff and Testcontainers payment-flow tests | Verified |
| AC-001..AC-004 | Runtime stack | Isolated Compose evidence | Pending local runtime configuration |
| AC-005 | Solution/tests/config gates | Accounting fast gate, focused integration tests, Compose and Prometheus validation | Verified |

## Progress and Resume State

- Implemented: corrected local/deploy include-relative mounts, restored the Prometheus alert mount, split local/deploy Grafana datasource provisioning, added a source-built isolated override, made metrics independent from tracing, added stable worker discovery, bounded all EventStore event types, separated attempt/commit/append/duplicate/projection metric semantics, stored the authoritative append context, marked poison/commit failures as errors, and removed the second console sink.
- Local runtime passed: the source-built worker was one healthy Prometheus target; a synthetic API payment reached `Projected`; every expected worker counter advanced; and Jaeger returned a 21-span API/Kafka/EventStore/projection trace. A controlled malformed message produced an error span, exception event, dead-letter metrics, a successful offset-commit metric, and one trace-correlated structured stdout error without the payload.
- Accounting API regression passed: its Prometheus target stayed healthy, API metric series were present, and `HomeBudget-Accounting-Api` spans remained in Jaeger.
- Tests passed: 21 focused component tests, two focused Testcontainers integration tests (including serialized EventStore metadata), and a release solution build with no errors.
- Deploy pipeline runtime passed in isolated temporary containers: Alloy retained a fast worker error after the 60-second decision (`accounting-payments-worker` and `errors` policies both executed), and Tempo returned the error `kafka.consume` span with its exception event. Loki returned the correlated worker consume and error events by worker `service_name` and trace id, once each.
- Open runtime evidence: a clean Seq 2026.1 volume became stuck in an uninterruptible Docker Desktop filesystem wait before the query API started. Seq is therefore not claimed as runtime verified.
- No remote environment was used.

## Metric Contracts

Prometheus converts dots to underscores and counters to `_total`.

| Instrument | Contract |
|---|---|
| `homebudget.kafka.messages.received` | One Kafka delivery returned to this consumer instance. |
| `homebudget.kafka.processing.attempts` | One completed attempt with bounded `persisted`, `duplicate`, `ignored`, or `deadletter` outcome. |
| `homebudget.kafka.offset.commit.outcomes` | One offset commit attempt, labelled by commit outcome and processing outcome; this is not a business-success count. |
| `homebudget.kafka.messages.deadlettered` | One malformed or retry-exhausted message written to the dead-letter stream. |
| `homebudget.eventstore.write.outcomes` | One event result: new `appended`, idempotent `duplicate`, or `failed`; event type is `added`, `updated`, `removed`, `deadletter`, or `unknown`. |
| `homebudget.projection.events.projected` | Number of payment events successfully included in a Mongo projection update. |
| `homebudget.projection.batch.attempts` | One projection batch attempt with `success` or `failure`. |

No metric label contains an account, operation, command, message, stream, or other unbounded identifier.

## Sampling and Discovery

Prometheus uses DNS A discovery for the unique alias `accounting-payments-worker-metrics` on port 80. With `PAYMENT_CONSUMERS_AMOUNT=1`, one target is expected. With multiple replicas, each task IP is a distinct `instance`; the healthy target count must equal the replica count.

Deploy Alloy waits 60 seconds, then retains every payment API route trace, every trace containing the worker service, every error and slow trace, plus a 5% baseline of unrelated traffic. A successful payment search therefore does not depend on the 5% baseline. The window covers the normal asynchronous path. Durable work arriving later can be evaluated after the initial decision; the worker-service policy retains that worker segment, but operators should not assume all very late stages arrived in the first decision batch.

## Operator Guide

In Grafana Explore select Prometheus and query:

```promql
up{job="accounting-workers"}
sum by (outcome) (rate(homebudget_kafka_processing_attempts_total[5m]))
sum by (outcome, event_type) (increase(homebudget_eventstore_write_outcomes_total[1h]))
increase(homebudget_projection_events_projected_total[1h])
```

For traces, select Jaeger locally or Tempo in deploy and search by W3C trace id or exact service name `homebudget-accounting-payments-consumer-worker`. A normal payment contains `kafka.consume`, `payment.process`, `eventstore.append`, projection, and Mongo spans.

For Loki logs, use:

```logql
{service_name="homebudget-accounting-payments-consumer-worker"} |= "<trace-id>"
```

For Seq, query `@Properties['service.name'] = 'homebudget-accounting-payments-consumer-worker' and @TraceId = '<trace-id>'`. Compact JSON stdout uses `@tr` and `@sp`. Confirm only one copy of each event before declaring log delivery verified.
