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

const technicalMaintenanceTypes = Object.freeze(CONVENTIONAL_TYPES.filter(type => type !== 'feat'));
const branchRules = Object.freeze([
	{ pattern: /^(?:feature|feat)\/.+/, allowedTypes: Object.freeze(['feat']) },
	{ pattern: /^(?:bug|bugfix|fix|hotfix)\/.+/, allowedTypes: Object.freeze(['fix']) },
	{ pattern: /^perf\/.+/, allowedTypes: Object.freeze(['perf']) },
	{ pattern: /^refactor\/.+/, allowedTypes: Object.freeze(['refactor']) },
	{ pattern: /^chore\/.+/, allowedTypes: Object.freeze(['chore']) },
	{ pattern: /^docs\/.+/, allowedTypes: Object.freeze(['docs']) },
	{ pattern: /^test\/.+/, allowedTypes: Object.freeze(['test']) },
	{ pattern: /^ci\/.+/, allowedTypes: Object.freeze(['ci']) },
	{ pattern: /^build\/.+/, allowedTypes: Object.freeze(['build']) },
	{ pattern: /^(?:tech|codex)\/.+/, allowedTypes: technicalMaintenanceTypes },
	{ pattern: /^(?:automation\/deps|dependabot|renovate)\/.+/, allowedTypes: Object.freeze(['chore']) },
]);
const titlePattern = /^(?<type>[a-z]+)(?:\([^\)\r\n]+\))?(?<breaking>!)?: (?<description>[^\r\n]+)$/;
const releaseWeight = Object.freeze({ patch: 1, minor: 2, major: 3 });

export function parseConventionalTitle(title) {
	const match = titlePattern.exec(title);
	if (!match) return undefined;
	return {
		type: match.groups.type,
		breaking: Boolean(match.groups.breaking),
		supported: CONVENTIONAL_TYPES.includes(match.groups.type),
	};
}

export function validatePullRequest(branch, title) {
	const branchRule = branchRules.find(rule => rule.pattern.test(branch));
	if (!branchRule)
		return `Unsupported branch name "${branch}". Use an approved prefix followed by a non-empty description.`;

	const parsedTitle = parseConventionalTitle(title);
	if (!parsedTitle) return `Invalid PR title "${title}". Use <type>(<scope>): <description>.`;
	if (!parsedTitle.supported) {
		return [
			'Release-policy validation failed.',
			'',
			'Unsupported PR type:',
			`  ${parsedTitle.type}`,
			'',
			'Supported Conventional Commit types:',
			`  ${CONVENTIONAL_TYPES.join(', ')}`,
			'',
			'For branch:',
			`  ${branch}`,
		].join('\n');
	}
	if (!branchRule.allowedTypes.includes(parsedTitle.type)) {
		const branchFamily = `${branch.split('/', 1)[0]}/*`;
		const isTechnicalMaintenance = branchFamily === 'tech/*' || branchFamily === 'codex/*';
		return [
			'Release-policy validation failed.',
			'',
			'Branch:',
			`  ${branch}`,
			'',
			'PR title:',
			`  ${title}`,
			'',
			'Parsed Conventional Commit type:',
			`  ${parsedTitle.type}`,
			'',
			`Allowed types for ${branchFamily}:`,
			`  ${branchRule.allowedTypes.join(', ')}`,
			...(isTechnicalMaintenance
				? [
					'',
					'Reason:',
					`  ${branchFamily} is reserved for technical/maintenance work.`,
					'  Feature releases should normally use feat/* or feature/*.',
					'',
					'Example:',
					'  ci(release): update semantic versioning',
				]
				: []),
		].join('\n');
	}
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
