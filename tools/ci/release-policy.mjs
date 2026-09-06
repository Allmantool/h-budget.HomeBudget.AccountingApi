export const CONVENTIONAL_TYPES = Object.freeze([
	'build',
	'chore',
	'ci',
	'docs',
	'feat',
	'fix',
	'perf',
	'refactor',
	'revert',
	'style',
	'test',
]);

export const RELEASE_RULES = Object.freeze([
	{ breaking: true, release: 'major' },
	{ type: 'feat', release: 'minor' },
	{ type: 'fix', release: 'patch' },
	{ type: 'perf', release: 'patch' },
	{ type: 'revert', release: 'patch' },
	{ type: 'refactor', release: 'patch' },
	{ type: 'chore', release: 'patch' },
	{ type: 'build', release: 'patch' },
	{ type: 'ci', release: 'patch' },
	{ type: 'docs', release: false },
	{ type: 'style', release: false },
	{ type: 'test', release: false },
]);

const branchRules = Object.freeze([
	{ pattern: /^(?:feature|feat)\/.+/, type: 'feat' },
	{ pattern: /^(?:bug|bugfix|fix|hotfix)\/.+/, type: 'fix' },
	{ pattern: /^perf\/.+/, type: 'perf' },
	{ pattern: /^refactor\/.+/, type: 'refactor' },
	{ pattern: /^chore\/.+/, type: 'chore' },
	{ pattern: /^docs\/.+/, type: 'docs' },
	{ pattern: /^test\/.+/, type: 'test' },
	{ pattern: /^ci\/.+/, type: 'ci' },
	{ pattern: /^build\/.+/, type: 'build' },
	{ pattern: /^tech\/.+/, type: 'chore' },
	{ pattern: /^(?:automation\/deps|dependabot|renovate)\/.+/, type: 'chore' },
]);
const titlePattern = /^(?<type>[a-z]+)(?:\([^\)\r\n]+\))?(?<breaking>!)?: (?<description>[^\r\n]+)$/;
const releaseWeight = Object.freeze({ patch: 1, minor: 2, major: 3 });

export function parseConventionalTitle(title) {
	const match = titlePattern.exec(title);
	if (!match || !CONVENTIONAL_TYPES.includes(match.groups.type)) return undefined;
	return { type: match.groups.type, breaking: Boolean(match.groups.breaking) };
}

export function validatePullRequest(branch, title) {
	const expectedType = branchRules.find(rule => rule.pattern.test(branch))?.type;
	if (!expectedType)
		return `Unsupported branch name "${branch}". Use an approved prefix followed by a non-empty description.`;

	const parsedTitle = parseConventionalTitle(title);
	if (!parsedTitle) return `Invalid PR title "${title}". Use <type>(<scope>): <description>.`;
	if (parsedTitle.type !== expectedType)
		return `Branch "${branch}" expects a ${expectedType}: PR title, received ${parsedTitle.type}:`;
	return undefined;
}

export function classifyReleaseType(messages) {
	let releaseType;
	for (const message of messages) {
		const parsedTitle = parseConventionalTitle(message.split(/\r?\n/, 1)[0]);
		const breaking = parsedTitle?.breaking || /(^|\r?\n)BREAKING CHANGES?: .+/m.test(message);
		const candidate = breaking ? 'major' : RELEASE_RULES.find(rule => rule.type === parsedTitle?.type)?.release;
		if (candidate && (!releaseType || releaseWeight[candidate] > releaseWeight[releaseType])) releaseType = candidate;
	}
	return releaseType;
}

function readOption(name) {
	const optionIndex = process.argv.indexOf(name);
	return optionIndex === -1 ? undefined : process.argv[optionIndex + 1];
}

if (import.meta.main) {
	const branch = readOption('--branch');
	const title = readOption('--title');
	if (!branch || !title) {
		console.error('Usage: node tools/ci/release-policy.mjs --branch <branch> --title <title>');
		process.exitCode = 1;
	} else {
		const error = validatePullRequest(branch, title);
		if (error) {
			console.error(`Release-policy validation failed: ${error}`);
			process.exitCode = 1;
		} else console.log(`Release-policy validation passed for ${branch}.`);
	}
}
