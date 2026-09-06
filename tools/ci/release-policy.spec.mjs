import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import { analyzeCommits } from '@semantic-release/commit-analyzer';

import releaseConfig, { BOOTSTRAP_VERSION, isStableTag } from '../../release.config.mjs';
import { classifyReleaseType, validatePullRequest } from './release-policy.mjs';

const semanticReleaseConfig = releaseConfig.plugins[0][1];
const silentLogger = { log() {} };

async function analyze(messages) {
	return analyzeCommits(semanticReleaseConfig, {
		commits: messages.map((message, index) => ({ hash: `synthetic-${index}`, message })),
		cwd: process.cwd(),
		logger: silentLogger,
	});
}

test('classifies patch, minor, and major releases from v1.2.3', async () => {
	assert.equal(classifyReleaseType(['fix(payments): correct payment balance']), 'patch');
	assert.equal(await analyze(['fix(payments): correct payment balance']), 'patch');
	assert.equal(classifyReleaseType(['feat(payments): add command status endpoint']), 'minor');
	assert.equal(await analyze(['feat(payments): add command status endpoint']), 'minor');
	assert.equal(classifyReleaseType(['feat(payments)!: replace payment operation contract']), 'major');
	assert.equal(await analyze(['feat(payments)!: replace payment operation contract']), 'major');
});

test('selects the highest semantic impact and recognizes breaking footers', async () => {
	const messages = [
		'fix(payments): correct payment balance',
		'feat(payments): add command status endpoint',
		'docs: update API notes',
	];
	assert.equal(classifyReleaseType(messages), 'minor');
	assert.equal(await analyze(messages), 'minor');

	const breaking = ['fix(payments): correct payment balance\n\nBREAKING CHANGE: legacy response fields were removed'];
	assert.equal(classifyReleaseType(breaking), 'major');
	assert.equal(await analyze(breaking), 'major');
});

test('does not release docs, test, or style changes alone', async () => {
	const messages = ['docs: update API notes', 'test(payments): add coverage', 'style: format source'];
	assert.equal(classifyReleaseType(messages), undefined);
	assert.equal(await analyze(messages), null);
});

test('keeps deployable maintenance changes aligned with the SPA policy', async () => {
	for (const message of [
		'perf(payments): reduce projection work',
		'revert: restore payment recovery',
		'refactor(payments): simplify command handler',
		'chore(deps): update Mongo driver',
		'build(api): update container base image',
		'ci(release): harden release workflow',
	]) {
		assert.equal(classifyReleaseType([message]), 'patch', message);
		assert.equal(await analyze([message]), 'patch', message);
	}
});

test('validates squash-safe PR title semantics without using branch names for release impact', () => {
	assert.equal(validatePullRequest('feature/payment-status', 'feat(payments): add command status endpoint'), undefined);
	assert.equal(validatePullRequest('hotfix/duplicate-payment', 'fix(payments): prevent duplicate payment'), undefined);
	assert.match(validatePullRequest('feature/payment-status', 'fix(payments): prevent duplicate payment'), /expects a feat/);
	assert.match(validatePullRequest('unsupported/payment-status', 'feat(payments): add command status endpoint'), /Unsupported branch name/);
});

test('defines deterministic bootstrap, invalid-tag, and rerun semantics', () => {
	assert.equal(releaseConfig.tagFormat, 'v${version}');
	assert.deepEqual(releaseConfig.branches, ['master']);
	assert.equal(BOOTSTRAP_VERSION, '1.0.0');
	assert.equal(isStableTag('v1.2.3'), true);
	assert.equal(isStableTag('v0.0.291-build1'), false);
	assert.equal(isStableTag('migration-test'), false);
	assert.equal(classifyReleaseType([]), undefined);
});

test('keeps PR validation read-only and makes master push the sole automatic tag path', async () => {
	const [prWorkflow, releaseWorkflow] = await Promise.all([
		readFile(new URL('../../.github/workflows/pr-release-policy.yml', import.meta.url), 'utf8'),
		readFile(new URL('../../.github/workflows/update_semver.yml', import.meta.url), 'utf8'),
	]);

	assert.match(prWorkflow, /pull_request:/);
	assert.match(prWorkflow, /contents: read/);
	assert.doesNotMatch(prWorkflow, /contents: write|semantic-release|git tag|gh release/);
	assert.match(releaseWorkflow, /push:\s*\n\s+branches: \[master\]/);
	assert.match(releaseWorkflow, /group: accounting-release-master/);
	assert.match(releaseWorkflow, /fetch-depth: 0/);
	assert.match(releaseWorkflow, /contents: write/);
	assert.match(releaseWorkflow, /npx --no-install semantic-release/);
	assert.doesNotMatch(releaseWorkflow, /git push --force|git tag -f/);
});

test('propagates normalized SemVer and the release SHA into both release images', async () => {
	const [workflow, apiDockerfile, workerDockerfile] = await Promise.all([
		readFile(new URL('../../.github/workflows/release-tag.yml', import.meta.url), 'utf8'),
		readFile(new URL('../../dockerfile', import.meta.url), 'utf8'),
		readFile(new URL('../../HomeBudget.Accounting.Workers.OperationsConsumer/Dockerfile', import.meta.url), 'utf8'),
	]);

	assert.match(workflow, /BUILD_VERSION=\$\{\{ needs\.verify-release\.outputs\.release_version \}\}/);
	assert.match(workflow, /BUILD_SHA=\$\{\{ needs\.verify-release\.outputs\.release_sha \}\}/);
	assert.match(workflow, /org\.opencontainers\.image\.version=\$\{\{ needs\.verify-release\.outputs\.release_version \}\}/);
	for (const dockerfile of [apiDockerfile, workerDockerfile]) {
		assert.match(dockerfile, /ARG BUILD_VERSION/);
		assert.match(dockerfile, /ARG BUILD_SHA/);
		assert.match(dockerfile, /\/p:Version=\$BUILD_VERSION/);
		assert.match(dockerfile, /\/p:InformationalVersion=\$BUILD_VERSION\+\$BUILD_SHA/);
	}
});

test('records artifact-only deployment metadata from release outputs without Git discovery', async () => {
	const workflow = await readFile(new URL('../../.github/workflows/release-tag.yml', import.meta.url), 'utf8');
	const recordDeployment = workflow.slice(workflow.indexOf('  record-deployment:'));

	assert.doesNotMatch(recordDeployment, /actions\/checkout/);
	assert.match(recordDeployment, /RELEASE_VERSION: \$\{\{ needs\.verify-release\.outputs\.release_version \}\}/);
	assert.match(recordDeployment, /DOCKERHUB_USERNAME: \$\{\{ secrets\.DOCKERHUB_USERNAME \}\}/);
	assert.match(recordDeployment, /API_IMAGE="\$DOCKERHUB_USERNAME\/\$ACCOUNTING_API_IMAGE_NAME:\$RELEASE_VERSION"/);
	assert.match(recordDeployment, /WORKER_IMAGE="\$DOCKERHUB_USERNAME\/\$ACCOUNTING_WORKER_IMAGE_NAME:\$RELEASE_VERSION"/);
	assert.match(recordDeployment, /gh release view "\$RELEASE_TAG" --repo "\$GITHUB_REPOSITORY"/);
	assert.match(recordDeployment, /gh release edit "\$RELEASE_TAG" --repo "\$GITHUB_REPOSITORY"/);
	assert.match(recordDeployment, /:\s*"\$\{RELEASE_TAG:\?Release tag is required\}"/);
	assert.match(recordDeployment, /:\s*"\$\{RELEASE_VERSION:\?Release version is required\}"/);
	assert.match(recordDeployment, /:\s*"\$\{RELEASE_SHA:\?Release SHA is required\}"/);
});
