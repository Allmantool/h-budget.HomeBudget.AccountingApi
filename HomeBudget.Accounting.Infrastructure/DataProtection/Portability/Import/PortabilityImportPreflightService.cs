using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportPreflightService
    {
        private readonly IPortabilityImportTargetProbe _targetProbe;
        private readonly PortabilityImportPreflightOptions _options;

        public PortabilityImportPreflightService(
            IPortabilityImportTargetProbe targetProbe,
            PortabilityImportPreflightOptions options)
        {
            _targetProbe = targetProbe ?? throw new ArgumentNullException(nameof(targetProbe));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
        }

        public async Task<PortabilityImportPreflightResult> ValidateAsync(
            PortabilityImportRequest request,
            string packageRoot,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            cancellationToken.ThrowIfCancellationRequested();
            var package = PortabilityImportPackage.Load(packageRoot, _options, cancellationToken);
            ValidatePackageBinding(request, package);
            var target = await _targetProbe.ProbeAsync(request, cancellationToken).ConfigureAwait(false);
            ValidateTarget(request, target, package);
            var exportId = package.Manifest.GetProperty("exportId").GetGuid();
            var entities = PortabilityImportRules.Validate(package, exportId);
            cancellationToken.ThrowIfCancellationRequested();
            var requiredBytes = checked(package.InventoryBytes * _options.CapacityHeadroomMultiplier);
            if (target.AvailableBytes < requiredBytes)
            {
                throw Reject("import.capacity-insufficient");
            }

            var records = package.CreateExecutionRecords(entities);
            return new PortabilityImportPreflightResult(
                exportId,
                package.PackageSha256,
                package.Manifest.GetProperty("formatVersion").GetString(),
                package.Manifest.GetProperty("completeness").GetString(),
                requiredBytes,
                entities,
                records,
                package.Reconciliation.Clone());
        }

        private static void ValidateRequest(PortabilityImportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Mode is not "validate-only" and not "empty-target" and not "resume" and not "replace")
            {
                throw Reject("import.mode-unsupported");
            }

            if (!request.ActorAuthorized)
            {
                throw Reject("import.authorization-required");
            }

            if (!IsSha256(request.PackageSha256)
                || string.IsNullOrWhiteSpace(request.EnvironmentId)
                || string.IsNullOrWhiteSpace(request.LedgerId)
                || string.IsNullOrWhiteSpace(request.TargetGenerationId)
                || request.TargetGenerationNumber < 0
                || (request.Mode == "resume"
                    && (!IsSha256(request.ExpectedPackageSha256)
                        || string.IsNullOrWhiteSpace(request.ExpectedTargetGenerationId)
                        || request.ResumeMode is not "empty-target" and not "replace")))
            {
                throw Reject("import.request-invalid");
            }
        }

        private static void ValidatePackageBinding(
            PortabilityImportRequest request,
            PortabilityImportPackage package)
        {
            if (!string.Equals(request.PackageSha256, package.PackageSha256, StringComparison.Ordinal))
            {
                throw Reject("import.package-hash-mismatch");
            }

            var version = package.Manifest.GetProperty("formatVersion").GetString();
            if (!version.StartsWith("1.0.", StringComparison.Ordinal))
            {
                throw Reject("import.version-unsupported", "manifest.json", field: "formatVersion");
            }

            if (request.Mode == "resume"
                && !string.Equals(
                    request.ExpectedPackageSha256,
                    package.PackageSha256,
                    StringComparison.Ordinal))
            {
                throw Reject("import.source-binding-conflict");
            }

            if (request.Mode is "empty-target" or "replace"
                && package.Manifest.GetProperty("completeness").GetString() != "full")
            {
                throw Reject("import.partial-replacement", "manifest.json", field: "completeness");
            }
        }

        private static void ValidateTarget(
            PortabilityImportRequest request,
            PortabilityImportTargetSnapshot target,
            PortabilityImportPackage package)
        {
            if (target == null
                || target.EnvironmentId != request.EnvironmentId
                || target.LedgerId != request.LedgerId
                || target.GenerationId != request.TargetGenerationId
                || target.GenerationNumber != request.TargetGenerationNumber)
            {
                throw Reject("import.target-identity-mismatch");
            }

            if (request.Mode == "resume"
                && request.ExpectedTargetGenerationId != target.GenerationId)
            {
                throw Reject("import.target-binding-conflict");
            }

            if (request.Mode == "empty-target" && target.HasBusinessState)
            {
                throw Reject("import.target-not-empty");
            }

            if (request.Mode == "replace")
            {
                if (package.Manifest.GetProperty("completeness").GetString() != "full")
                {
                    throw Reject("import.partial-replacement");
                }

                if (target.IsActive
                    || !request.MaintenanceIsolationConfirmed
                    || !Guid.TryParse(request.PreOperationRecoverySetId, out _))
                {
                    throw Reject("import.replacement-prerequisite");
                }
            }
        }

        private static bool IsSha256(string value) => value?.Length == 64
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

        private static PortabilityImportValidationException Reject(
            string code,
            string path = null,
            string entity = null,
            string field = null) => new(code, path, entity, field);
    }
}
