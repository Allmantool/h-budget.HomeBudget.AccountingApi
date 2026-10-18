using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed partial class RecoveryPlanSelector
    {
        private static readonly RecoveryComponent[] RequiredComponents = Enum.GetValues<RecoveryComponent>();

        private static void ValidateRequest(RecoveryPlanningRequest request)
        {
            RequireUtc(request.RequestedCutoffUtc, "cutoff-not-utc");
            if (!request.SuppressNotifications)
            {
                throw Invalid("notification-suppression-required", "Recovery replay must suppress notifications.");
            }

            if (!request.SuppressExternalSideEffects)
            {
                throw Invalid("external-side-effect-suppression-required", "Recovery replay must suppress external side effects.");
            }

            if (!request.Target.IsIsolated || !request.Target.IsEmpty ||
                !string.Equals(request.Target.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal) ||
                !string.Equals(request.Target.LedgerId, request.LedgerId, StringComparison.Ordinal))
            {
                throw Invalid("target-not-isolated", "Recovery planning requires an isolated empty target for the selected scope.");
            }

            if (string.Equals(request.Target.GenerationId, request.Target.ActiveGenerationId, StringComparison.Ordinal) ||
                string.Equals(request.Target.GenerationId, request.SourceGenerationId, StringComparison.Ordinal))
            {
                throw Invalid("target-generation-active", "Recovery planning requires an inactive new target generation.");
            }

            if (request.Mode == RecoveryPlanningMode.SavedPoint && !request.RecoverySetId.HasValue)
            {
                throw Invalid("recovery-set-required", "Saved-point recovery requires a recovery-set identity.");
            }
        }

        private static VerifiedRecoverySetEvidence SelectBaseline(
            RecoveryPlanningRequest request,
            IReadOnlyCollection<VerifiedRecoverySetEvidence> recoverySets)
        {
            if (recoverySets is null)
            {
                throw Invalid("baseline-unavailable", "No recovery baseline evidence is available.");
            }

            var scoped = recoverySets.Where(item =>
                    string.Equals(item.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal) &&
                    string.Equals(item.LedgerId, request.LedgerId, StringComparison.Ordinal) &&
                    string.Equals(item.SourceGenerationId, request.SourceGenerationId, StringComparison.Ordinal))
                .ToArray();
            VerifiedRecoverySetEvidence baseline;
            if (request.Mode == RecoveryPlanningMode.SavedPoint)
            {
                var matches = scoped.Where(item => item.RecoverySetId == request.RecoverySetId).ToArray();
                if (matches.Length > 1)
                {
                    throw Invalid("baseline-identity-conflict", "Recovery-set identity evidence is duplicated.");
                }

                baseline = matches.SingleOrDefault();
            }
            else
            {
                baseline = scoped.Where(item => item.CommonCutoffUtc <= request.RequestedCutoffUtc)
                    .OrderByDescending(item => item.CommonCutoffUtc)
                    .FirstOrDefault();
            }

            return baseline ?? throw Invalid(
                "baseline-unavailable",
                "No recovery baseline exists at or before the requested cutoff.");
        }

        private static IReadOnlyDictionary<RecoveryComponent, RecoveryBaselineComponentEvidence> ValidateBaseline(
            RecoveryPlanningRequest request,
            VerifiedRecoverySetEvidence baseline)
        {
            RequireUtc(baseline.CommonCutoffUtc, "baseline-invalid");
            if (baseline.CommonCutoffUtc > request.RequestedCutoffUtc)
            {
                throw Invalid("cutoff-unavailable", "The selected recovery point is after the requested cutoff.");
            }

            if (!baseline.IsComplete || !baseline.IsProtected || !baseline.IsIndependentCopy ||
                !baseline.IsImmutableOrOffline)
            {
                throw Invalid("baseline-unprotected", "The selected recovery baseline is not independently protected and complete.");
            }

            if (!baseline.IsIntegrityVerified)
            {
                throw Invalid("checksum-unverified", "The selected recovery baseline failed integrity verification.");
            }

            if (!string.Equals(baseline.ApplicationVersion, request.ApplicationVersion, StringComparison.Ordinal) ||
                !string.Equals(baseline.SchemaVersion, request.SchemaVersion, StringComparison.Ordinal))
            {
                throw Invalid("version-incompatible", "The selected recovery baseline is not version compatible.");
            }

            var components = RequireOneOfEach(
                baseline.Components,
                item => item.Component,
                "baseline-component-incomplete");
            foreach (var component in RequiredComponents)
            {
                var evidence = components[component];
                RequireUtc(evidence.CutoffUtc, "baseline-invalid");
                if (string.IsNullOrWhiteSpace(evidence.ArtifactId) || string.IsNullOrWhiteSpace(evidence.Watermark))
                {
                    throw Invalid("baseline-invalid", "Recovery baseline component evidence is incomplete.");
                }

                ValidateTrust(evidence.IsChecksumVerified, evidence.IsVersionCompatible);
                RequireCoverage(component, evidence.Coverage);
            }

            return components;
        }

        private static void ValidateChainHeader(
            RecoveryPlanningRequest request,
            RecoveryBaselineComponentEvidence baseline,
            RecoveryComponentChainEvidence chain)
        {
            RequireUtc(chain.RecoverableFromUtc, "continuous-history-invalid");
            RequireUtc(chain.RecoverableToUtc, "continuous-history-invalid");
            RequireUtc(chain.ResolvedCutoffUtc, "continuous-history-invalid");
            if (request.RequestedCutoffUtc < chain.RecoverableFromUtc ||
                request.RequestedCutoffUtc > chain.RecoverableToUtc)
            {
                throw Invalid("cutoff-unavailable", "The requested cutoff is outside a verified component interval.");
            }

            if (chain.ResolvedCutoffUtc > request.RequestedCutoffUtc ||
                chain.ResolvedCutoffUtc < baseline.CutoffUtc ||
                chain.ResolvedSequence < baseline.DurableSequence ||
                string.IsNullOrWhiteSpace(chain.ResolvedWatermark) ||
                string.IsNullOrWhiteSpace(chain.Precision))
            {
                throw Invalid("actual-cutoff-invalid", "A component resolved an invalid actual cutoff.");
            }
        }

        private static IReadOnlyCollection<RecoveryHistorySegmentEvidence> SelectContinuousSegments(
            RecoveryBaselineComponentEvidence baseline,
            RecoveryComponentChainEvidence chain,
            DateTimeOffset actualCutoff)
        {
            if (actualCutoff <= baseline.CutoffUtc)
            {
                return Array.Empty<RecoveryHistorySegmentEvidence>();
            }

            var selected = new List<RecoveryHistorySegmentEvidence>();
            var cursorUtc = baseline.CutoffUtc;
            var cursorSequence = baseline.DurableSequence;
            if (chain.Segments is null)
            {
                throw Invalid("continuous-history-gap", "A required continuous-history segment is missing.");
            }

            foreach (var segment in chain.Segments.OrderBy(item => item.FromSequenceExclusive))
            {
                if (segment.ToInclusiveUtc <= cursorUtc || segment.ToSequenceInclusive <= cursorSequence)
                {
                    continue;
                }

                RequireUtc(segment.FromExclusiveUtc, "continuous-history-invalid");
                RequireUtc(segment.ToInclusiveUtc, "continuous-history-invalid");
                if (segment.Component != chain.Component || segment.FromExclusiveUtc != cursorUtc ||
                    segment.FromSequenceExclusive != cursorSequence ||
                    segment.ToInclusiveUtc <= segment.FromExclusiveUtc ||
                    segment.ToSequenceInclusive <= segment.FromSequenceExclusive ||
                    string.IsNullOrWhiteSpace(segment.ArtifactId) ||
                    string.IsNullOrWhiteSpace(segment.Watermark))
                {
                    throw Invalid("continuous-history-gap", "A required continuous-history segment is missing.");
                }

                ValidateTrust(segment.IsChecksumVerified, segment.IsVersionCompatible);
                RequireCoverage(chain.Component, segment.Coverage);
                selected.Add(segment);
                cursorUtc = segment.ToInclusiveUtc;
                cursorSequence = segment.ToSequenceInclusive;
                if (cursorUtc >= actualCutoff)
                {
                    break;
                }
            }

            if (cursorUtc < actualCutoff)
            {
                throw Invalid("continuous-history-gap", "A required continuous-history segment is missing.");
            }

            return selected;
        }

        private static IReadOnlyCollection<RecoveryMutationEvidence> SelectMutations(
            RecoveryComponent component,
            RecoveryBaselineComponentEvidence baseline,
            IReadOnlyCollection<RecoveryHistorySegmentEvidence> segments,
            DateTimeOffset actualCutoff)
        {
            var mutations = new List<RecoveryMutationEvidence>();
            foreach (var segment in segments)
            {
                if (segment.Mutations is null)
                {
                    throw Invalid("mutation-order-invalid", "Mutation evidence is incomplete.");
                }

                foreach (var mutation in segment.Mutations)
                {
                    RequireUtc(mutation.DurableTimestampUtc, "mutation-order-invalid");
                    if (mutation.Component != component || mutation.DurableSequence <= segment.FromSequenceExclusive ||
                        mutation.DurableSequence > segment.ToSequenceInclusive ||
                        mutation.DurableTimestampUtc <= segment.FromExclusiveUtc ||
                        mutation.DurableTimestampUtc > segment.ToInclusiveUtc)
                    {
                        throw Invalid("mutation-order-invalid", "Mutation evidence is outside its durable segment.");
                    }

                    if (mutation.DurableSequence > baseline.DurableSequence &&
                        mutation.DurableTimestampUtc <= actualCutoff)
                    {
                        mutations.Add(mutation);
                    }
                }
            }

            return mutations;
        }

        private static RecoveryComponentWatermark CreateWatermark(
            RecoveryBaselineComponentEvidence baseline,
            RecoveryComponentChainEvidence chain,
            IReadOnlyCollection<RecoveryHistorySegmentEvidence> segments)
        {
            var last = segments.LastOrDefault();
            if (last is not null && chain.ResolvedSequence > last.ToSequenceInclusive)
            {
                throw Invalid("continuous-history-gap", "The resolved watermark is outside the selected history chain.");
            }

            return new RecoveryComponentWatermark(
                baseline.Component,
                chain.ResolvedCutoffUtc,
                chain.ResolvedSequence,
                chain.ResolvedWatermark,
                chain.Precision,
                new[] { baseline.ArtifactId }.Concat(segments.Select(item => item.ArtifactId)).ToArray());
        }

        private static void ValidateUniqueMutationIdentities(IReadOnlyCollection<RecoveryMutationEvidence> mutations)
        {
            if (mutations.GroupBy(item => item.MutationId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                throw Invalid("mutation-identity-conflict", "Recovery mutation identities are not unique.");
            }
        }

        private static IReadOnlyDictionary<RecoveryComponent, T> RequireOneOfEach<T>(
            IReadOnlyCollection<T> items,
            Func<T, RecoveryComponent> component,
            string code)
        {
            if (items is null)
            {
                throw Invalid(code, "Required component evidence is incomplete.");
            }

            var groups = items.GroupBy(component).ToArray();
            if (groups.Any(group => group.Count() != 1) ||
                groups.Length != RequiredComponents.Length ||
                RequiredComponents.Any(required => groups.All(group => group.Key != required)))
            {
                throw Invalid(code, "Required component evidence is incomplete or duplicated.");
            }

            return groups.ToDictionary(group => group.Key, group => group.Single());
        }

        private static void ValidateTrust(bool checksumVerified, bool versionCompatible)
        {
            if (!checksumVerified)
            {
                throw Invalid("checksum-unverified", "Recovery evidence failed checksum verification.");
            }

            if (!versionCompatible)
            {
                throw Invalid("version-incompatible", "Recovery evidence is not version compatible.");
            }
        }

        private static void RequireCoverage(RecoveryComponent component, RecoveryHistoryCoverage actual)
        {
            var required = RecoveryCoveragePolicy.Required(component);
            if ((actual & required) != required)
            {
                throw Invalid("history-coverage-incomplete", "Required authoritative history coverage is incomplete.");
            }
        }

        private static void RequireUtc(DateTimeOffset value, string code)
        {
            if (value.Offset != TimeSpan.Zero)
            {
                throw Invalid(code, "Recovery planning timestamps must be UTC.");
            }
        }

        private static RecoveryPlanningException Invalid(string code, string message) => new(code, message);
    }
}
