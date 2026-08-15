# Cross-account transfer exchange rates

`POST /accounting/cross-accounts-transfer` remains backward compatible. The existing required `multiplier` is the effective rate that the SPA obtains from the Rates API for automatic transfers. Its orientation is `1 sender currency = multiplier recipient currency`; the recipient amount is `amount * multiplier` using .NET `decimal` arithmetic.

`customConversionMultiplier` is an optional decimal request property. When present, it must be greater than zero and the Accounting API uses it instead of `multiplier` for the recipient amount and the persisted `ConversionMultiplier` on both related transactions. It is omitted for automatic transfers.

The SPA resolves automatic rates before submitting the Accounting command. For a custom rate it does not call the Rates API. Same-currency transfers submit an effective multiplier of `1` and do not show exchange-rate controls. The API does not persist rate provenance because the existing event/history model stores only the multiplier; this additive request property does not alter historical event payloads.

The application retains its existing rounding policy: the SPA preview rounds to three decimal places, while the Accounting API persists the authoritative decimal multiplication result without introducing additional rounding.
