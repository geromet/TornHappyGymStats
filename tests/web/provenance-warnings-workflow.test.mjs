import test from 'node:test';
import assert from 'node:assert/strict';

const {
  MAX_RENDERED_WARNINGS,
  buildProfileLink,
  buildWarningsViewModel,
} = await import('../../web/app.js');

test('buildProfileLink creates Torn profile links for numeric ids only', () => {
  assert.equal(buildProfileLink('owner', '12345'), 'https://www.torn.com/profiles.php?XID=12345');
  assert.equal(buildProfileLink('faction', '777'), 'https://www.torn.com/factions.php?step=profile&ID=777');
  assert.equal(buildProfileLink('company', '42'), 'https://www.torn.com/joblist.php#!p=corpinfo&ID=42');
  assert.equal(buildProfileLink('owner', 'abc'), null);
  assert.equal(buildProfileLink('owner', '12<script>'), null);
  assert.equal(buildProfileLink('unknown', '12'), null);
});

test('buildWarningsViewModel falls back to missing-provenance marker when warnings payload malformed', () => {
  const vm = buildWarningsViewModel({ warnings: null });
  assert.equal(vm.hasFallback, true);
  assert.equal(vm.totalWarnings, 1);
  assert.equal(vm.renderedCount, 1);
  assert.equal(vm.items[0].reasonCode, 'missing-provenance-record');
});

test('buildWarningsViewModel keeps deterministic ordering and text cap', () => {
  const longMessage = 'x'.repeat(400);
  const vm = buildWarningsViewModel({
    warnings: [
      { reasonCode: 'z-reason', sourceIdentifier: '100', scope: 'owner', message: longMessage },
      { reasonCode: 'a-reason', sourceIdentifier: '200', scope: 'owner', message: 'ok' },
    ],
  });

  assert.equal(vm.items[0].reasonCode, 'a-reason');
  assert.equal(vm.items[1].reasonCode, 'z-reason');
  assert.equal(vm.items[1].warningText.length, 280);
});

test('buildWarningsViewModel caps rendered warnings and reports overflow', () => {
  const warnings = Array.from({ length: MAX_RENDERED_WARNINGS + 5 }, (_, i) => ({
    reasonCode: `r-${String(i).padStart(2, '0')}`,
    sourceIdentifier: `${1000 + i}`,
    scope: 'owner',
    message: 'msg',
    manualOverrideApplied: i % 2 === 0,
  }));

  const vm = buildWarningsViewModel({ warnings });

  assert.equal(vm.renderedCount, MAX_RENDERED_WARNINGS);
  assert.equal(vm.truncated, true);
  assert.equal(vm.overflowCount, 5);
  assert.match(vm.items[0].overrideCopy, /Manual override matched|No manual override matched/);
});

test('buildWarningsViewModel omits invalid link target but keeps identifier action text', () => {
  const vm = buildWarningsViewModel({
    warnings: [{
      reasonCode: 'missing-owner',
      sourceIdentifier: '12x',
      scope: 'owner',
      message: 'Need owner',
    }],
  });

  assert.equal(vm.items[0].linkHref, null);
  assert.match(vm.items[0].actionCopy, /identifier only/);
});
