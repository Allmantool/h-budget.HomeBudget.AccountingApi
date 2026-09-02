# Change Specifications

Use a specification for non-trivial behavior, contract, persistence, security, performance-sensitive, cross-project, or architectural work. It is the durable record of the intended behavior, decisions, traceability, verification evidence, and resume state.

For a typo, formatting-only edit, comment correction, or other plainly non-behavioral change, use the lightweight path: state why a full specification is unnecessary and run proportionate validation.

For full SDD, copy [TEMPLATE.md](TEMPLATE.md) to a concise, feature-specific filename such as `payment-replay-reliability.md`. Do not overwrite an existing specification merely to force it into the template; evolve it when the work needs new traceability or progress information.

Keep the specification current during implementation. Use `Draft`, `Ready`, `In Progress`, `Implemented`, and `Verified` status accurately. `Verified` requires evidence for every applicable acceptance criterion, not only a successful build or test command.

The `.codex/skills/home-ledger-sdd`, `home-ledger-tdd`, and `home-ledger-distributed-verification` skills provide the detailed workflow. Existing operational runbooks remain authoritative for current operational behavior.
