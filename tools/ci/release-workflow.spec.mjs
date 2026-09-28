import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';

const execFileAsync = promisify(execFile);
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const workflowPath = join(repositoryRoot, '.github/workflows/release-tag.yml');
const bashExecutable = process.platform === 'win32' ? 'C:\\Program Files\\Git\\bin\\bash.exe' : 'bash';

function shellPath(path) {
	return process.platform === 'win32' ? path.replaceAll('\\', '/') : path;
}

function jobBlock(workflow, jobName) {
	const lines = workflow.split(/\r?\n/);
	const start = lines.findIndex(line => line === `  ${jobName}:`);
	assert.notEqual(start, -1, `Missing workflow job: ${jobName}`);
	const relativeEnd = lines.slice(start + 1).findIndex(line => /^  [a-zA-Z0-9_-]+:$/.test(line));
	const end = relativeEnd === -1 ? lines.length : start + relativeEnd + 1;
	return lines.slice(start, end).join('\n');
}

function dependencies(job) {
	const match = /^    needs:\s*(?:\[([^\]]+)\]|([^\s]+))\s*$/m.exec(job);
	assert.ok(match, 'Job must declare its dependencies with needs');
	return (match[1] ?? match[2]).split(',').map(value => value.trim());
}

function stepRunScript(job, stepName) {
	const lines = job.split('\n');
	const stepStart = lines.findIndex(line => line === `      - name: ${stepName}`);
	assert.notEqual(stepStart, -1, `Missing workflow step: ${stepName}`);
	const runStart = lines.slice(stepStart + 1).findIndex(line => line === '        run: |');
	assert.notEqual(runStart, -1, `Workflow step must use a multiline run block: ${stepName}`);
	const contentStart = stepStart + runStart + 2;
	const content = [];
	for (const line of lines.slice(contentStart)) {
		if (line.startsWith('      - ')) break;
		content.push(line.startsWith('          ') ? line.slice(10) : '');
	}
	return content.join('\n').trimEnd();
}

function assertReleaseWorkflowContract(workflow) {
	const verify = jobBlock(workflow, 'verify-release');
	const apiBuild = jobBlock(workflow, 'build-api-image');
	const workerBuild = jobBlock(workflow, 'build-worker-image');
	const apiPush = jobBlock(workflow, 'push-api-image');
	const workerPush = jobBlock(workflow, 'push-worker-image');
	const record = jobBlock(workflow, 'record-deployment');

	assert.match(apiBuild, /context: \.\s*\n\s+file: \.\/dockerfile\s*\n\s+target: final/);
	assert.match(workerBuild, /context: \.\s*\n\s+file: \.\/worker\.dockerfile\s*\n\s+target: final/);
	assert.doesNotMatch(workflow, /HomeBudget\.Accounting\.Workers\.OperationsConsumer\/Dockerfile/);
	assert.ok(
		verify.indexOf('Validate tag, source, and GitHub Release') < verify.indexOf('Verify release build inputs'),
		'Release inputs must be checked after immutable identity validation',
	);
	const inputValidator = stepRunScript(verify, 'Verify release build inputs');
	assert.match(inputValidator, /dockerfile/);
	assert.match(inputValidator, /worker\.dockerfile/);
	assert.match(inputValidator, /HomeBudget\.Accounting\.Workers\.OperationsConsumer\.csproj/);
	assert.match(inputValidator, /Selected release tag:/);
	assert.match(inputValidator, /Selected release SHA:/);
	assert.match(inputValidator, /Repository\/build context:/);
	assert.match(inputValidator, /Missing required release input:/);
	assert.deepEqual(dependencies(apiBuild), ['verify-release']);
	assert.deepEqual(dependencies(workerBuild), ['verify-release']);
	assert.deepEqual(dependencies(apiPush), ['verify-release', 'build-api-image', 'build-worker-image']);
	assert.deepEqual(dependencies(workerPush), ['verify-release', 'build-api-image', 'build-worker-image']);
	assert.match(record, /needs\.push-api-image\.result == 'success' && needs\.push-worker-image\.result == 'success'/);
	assert.match(record, /needs\.push-api-image\.result != 'success' \|\| needs\.push-worker-image\.result != 'success'/);
}

function canRun(job, results) {
	return dependencies(job).every(dependency => results[dependency] === 'success');
}

async function createReleaseFixture() {
	const root = await mkdtemp(join(tmpdir(), 'accounting-release-inputs-'));
	const workerProject = join(root, 'HomeBudget.Accounting.Workers.OperationsConsumer');
	await mkdir(workerProject);
	await Promise.all([
		writeFile(join(root, 'dockerfile'), 'FROM scratch\n'),
		writeFile(join(root, 'worker.dockerfile'), 'FROM scratch AS final\n'),
		writeFile(join(workerProject, 'HomeBudget.Accounting.Workers.OperationsConsumer.csproj'), '<Project />\n'),
	]);
	return root;
}

async function verifyFixture(root) {
	const workflow = await readFile(workflowPath, 'utf8');
	const validator = stepRunScript(jobBlock(workflow, 'verify-release'), 'Verify release build inputs');
	return execFileAsync(bashExecutable, ['-c', validator], {
		env: {
			...process.env,
			GITHUB_WORKSPACE: shellPath(root),
			RELEASE_TAG: 'v0.0.0-test',
			RELEASE_SHA: 'local-test-sha',
		},
	});
}

test('uses distinct root Dockerfiles and gates either publication on both builds', async () => {
	const workflow = await readFile(workflowPath, 'utf8');
	assertReleaseWorkflowContract(workflow);

	const failedWorkerResults = {
		'verify-release': 'success',
		'build-api-image': 'success',
		'build-worker-image': 'failure',
	};
	assert.equal(canRun(jobBlock(workflow, 'push-api-image'), failedWorkerResults), false);
	assert.equal(canRun(jobBlock(workflow, 'push-worker-image'), failedWorkerResults), false);

	const historicalPathWorkflow = workflow.replace('./worker.dockerfile', './HomeBudget.Accounting.Workers.OperationsConsumer/Dockerfile');
	assert.throws(() => assertReleaseWorkflowContract(historicalPathWorkflow));
	const independentApiPush = workflow.replace(
		'needs: [verify-release, build-api-image, build-worker-image]',
		'needs: [verify-release, build-api-image]',
	);
	assert.throws(() => assertReleaseWorkflowContract(independentApiPush));
});

test('release input validation accepts the exact nonempty build inputs', async () => {
	const root = await createReleaseFixture();
	try {
		const result = await verifyFixture(root);
		assert.match(result.stdout, /Release build inputs verified/);
	} finally {
		await rm(root, { recursive: true, force: true });
	}
});

test('release input validation rejects a missing or moved worker Dockerfile with actionable context', async () => {
	const root = await createReleaseFixture();
	try {
		await rm(join(root, 'worker.dockerfile'));
		await mkdir(join(root, 'HomeBudget.Accounting.Workers.OperationsConsumer'), { recursive: true });
		await writeFile(join(root, 'HomeBudget.Accounting.Workers.OperationsConsumer/Dockerfile'), 'FROM scratch AS final\n');

		await assert.rejects(verifyFixture(root), error => {
			assert.match(error.stderr, /Selected release tag: v0\.0\.0-test/);
			assert.match(error.stderr, /Selected release SHA: local-test-sha/);
			assert.match(error.stderr, /Repository\/build context:/);
			assert.match(error.stderr, /Missing required release input: worker\.dockerfile/);
			return true;
		});
	} finally {
		await rm(root, { recursive: true, force: true });
	}
});

test('release input validation enforces Linux filename case', async () => {
	const root = await createReleaseFixture();
	try {
		await rm(join(root, 'worker.dockerfile'));
		await writeFile(join(root, 'Worker.dockerfile'), 'FROM scratch AS final\n');
		await assert.rejects(verifyFixture(root), error => {
			assert.match(error.stderr, /Missing required release input: worker\.dockerfile/);
			return true;
		});
	} finally {
		await rm(root, { recursive: true, force: true });
	}
});
