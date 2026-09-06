import { RELEASE_RULES } from './tools/ci/release-policy.mjs';

export const BOOTSTRAP_VERSION = '1.0.0';

export function isStableTag(tag) {
	return /^v\d+\.\d+\.\d+$/.test(tag);
}

export default {
	branches: ['master'],
	tagFormat: 'v${version}',
	plugins: [
		[
			'@semantic-release/commit-analyzer',
			{
				preset: 'conventionalcommits',
				parserOpts: { noteKeywords: ['BREAKING CHANGE', 'BREAKING CHANGES'] },
				releaseRules: RELEASE_RULES,
			},
		],
		[
			'@semantic-release/release-notes-generator',
			{
				preset: 'conventionalcommits',
				parserOpts: { noteKeywords: ['BREAKING CHANGE', 'BREAKING CHANGES'] },
			},
		],
	],
};
