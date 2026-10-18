using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed partial class RecoveryPlanSelector
    {
        public RecoveryPlan Select(RecoveryPlanningRequest request, RecoveryEvidenceInventory inventory)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(inventory);
            ValidateRequest(request);

            var baseline = SelectBaseline(request, inventory.RecoverySets);
            var baselineComponents = ValidateBaseline(request, baseline);
            return request.Mode == RecoveryPlanningMode.SavedPoint
                ? BuildSavedPointPlan(request, baseline, baselineComponents)
                : BuildPointInTimePlan(request, baseline, baselineComponents, inventory.ComponentChains);
        }

        private static RecoveryPlan BuildSavedPointPlan(
            RecoveryPlanningRequest request,
            VerifiedRecoverySetEvidence baseline,
            IReadOnlyDictionary<RecoveryComponent, RecoveryBaselineComponentEvidence> components)
        {
            var watermarks = components.Values
                .OrderBy(item => item.Component)
                .Select(item => new RecoveryComponentWatermark(
                    item.Component,
                    item.CutoffUtc,
                    item.DurableSequence,
                    item.Watermark,
                    "saved-recovery-set",
                    [item.ArtifactId]))
                .ToArray();
            return CreatePlan(
                request,
                baseline,
                baseline.CommonCutoffUtc,
                "saved-recovery-set",
                watermarks,
                [],
                []);
        }

        private static RecoveryPlan BuildPointInTimePlan(
            RecoveryPlanningRequest request,
            VerifiedRecoverySetEvidence baseline,
            IReadOnlyDictionary<RecoveryComponent, RecoveryBaselineComponentEvidence> baselineComponents,
            IReadOnlyCollection<RecoveryComponentChainEvidence> chains)
        {
            var chainMap = RequireOneOfEach(chains, item => item.Component, "component-chain-incomplete");
            foreach (var component in RequiredComponents)
            {
                ValidateChainHeader(request, baselineComponents[component], chainMap[component]);
            }

            var actualCutoff = chainMap.Values.Min(item => item.ResolvedCutoffUtc);
            if (chainMap.Values.Any(item => item.ResolvedCutoffUtc != actualCutoff))
            {
                throw Invalid(
                    "component-cutoff-mismatch",
                    "Component evidence does not resolve to one common actual cutoff.");
            }

            var selectedSegments = new List<RecoveryHistorySegmentEvidence>();
            var watermarks = new List<RecoveryComponentWatermark>();
            var mutations = new List<RecoveryMutationEvidence>();

            foreach (var component in RequiredComponents)
            {
                var baselineComponent = baselineComponents[component];
                var chain = chainMap[component];
                var selected = SelectContinuousSegments(baselineComponent, chain, actualCutoff);
                selectedSegments.AddRange(selected);
                mutations.AddRange(SelectMutations(component, baselineComponent, selected, actualCutoff));
                watermarks.Add(CreateWatermark(baselineComponent, chain, selected));
            }

            ValidateUniqueMutationIdentities(mutations);
            return CreatePlan(
                request,
                baseline,
                actualCutoff,
                "coordinated-continuous-chain",
                watermarks,
                selectedSegments,
                mutations);
        }

        private static RecoveryPlan CreatePlan(
            RecoveryPlanningRequest request,
            VerifiedRecoverySetEvidence baseline,
            DateTimeOffset actualCutoff,
            string precision,
            IReadOnlyCollection<RecoveryComponentWatermark> watermarks,
            IReadOnlyCollection<RecoveryHistorySegmentEvidence> segments,
            IReadOnlyCollection<RecoveryMutationEvidence> mutations)
        {
            var orderedWatermarks = watermarks.OrderBy(item => item.Component)
                .Select(item => item with { ArtifactIds = Array.AsReadOnly(item.ArtifactIds.ToArray()) })
                .ToArray();
            var orderedSegments = segments.OrderBy(item => item.Component)
                .ThenBy(item => item.FromSequenceExclusive)
                .Select(item => item with { Mutations = Array.AsReadOnly(item.Mutations.ToArray()) })
                .ToArray();
            var orderedMutations = mutations.OrderBy(item => item.DurableTimestampUtc)
                .ThenBy(item => item.Component)
                .ThenBy(item => item.DurableSequence)
                .ThenBy(item => item.MutationId, StringComparer.Ordinal)
                .ToArray();
            var fingerprint = Fingerprint(request, baseline.RecoverySetId, actualCutoff, orderedSegments);
            return new RecoveryPlan(
                fingerprint,
                request.Mode,
                baseline.RecoverySetId,
                request.RequestedCutoffUtc,
                actualCutoff,
                precision,
                "durable-commit-and-acceptance-order",
                request.ApplicationVersion,
                request.SchemaVersion,
                baseline.SourceGenerationId,
                request.Target,
                true,
                true,
                false,
                Array.AsReadOnly(orderedWatermarks),
                Array.AsReadOnly(orderedSegments),
                Array.AsReadOnly(orderedMutations));
        }

        private static string Fingerprint(
            RecoveryPlanningRequest request,
            Guid baselineId,
            DateTimeOffset actualCutoff,
            IReadOnlyCollection<RecoveryHistorySegmentEvidence> segments)
        {
            var segmentIds = string.Join(',', segments.Select(item => item.ArtifactId));
            var value = string.Join(
                '|',
                request.Mode,
                baselineId,
                request.RequestedCutoffUtc.ToString("O"),
                actualCutoff.ToString("O"),
                request.Target.TargetId,
                request.Target.GenerationId,
                segmentIds);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }
    }
}
