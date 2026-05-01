import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const {
  clampConfidence,
  confidenceToColor,
  buildGymHoverText,
  buildGymMarkerColors,
  buildGymTrace,
} = await import('../../web/app.js');

test('clampConfidence bounds non-finite and out-of-range values', () => {
  assert.equal(clampConfidence(undefined), 0);
  assert.equal(clampConfidence(-1), 0);
  assert.equal(clampConfidence(2), 1);
  assert.equal(clampConfidence(0.4), 0.4);
});

test('confidenceToColor uses deterministic red→green gradient endpoints', () => {
  assert.equal(confidenceToColor(0), 'rgb(214, 64, 69)');
  assert.equal(confidenceToColor(1), 'rgb(56, 201, 110)');
  assert.equal(confidenceToColor(0.5), 'rgb(135, 133, 90)');
});

test('buildGymHoverText includes confidence and evidence reasons', () => {
  const series = {
    text: ['Gym: Heavy'],
    confidence: [0.82],
    confidenceReasons: [['source:api', 'source:history']],
  };

  const hover = buildGymHoverText(series, 0);

  assert.match(hover, /Gym: Heavy/);
  assert.match(hover, /Confidence: 82%/);
  assert.match(hover, /Evidence: source:api, source:history/);
});

test('buildGymHoverText falls back to missing-provenance-record when reasons absent', () => {
  const series = {
    text: ['Gym: Unknown'],
    confidence: [0.2],
    confidenceReasons: [[]],
  };

  const hover = buildGymHoverText(series, 0);
  assert.match(hover, /Evidence: missing-provenance-record/);
});

test('buildGymTrace maps confidence arrays to marker color and tooltip arrays', () => {
  const series = {
    x: [1, 2],
    y: [3, 4],
    z: [5, 6],
    text: ['A', 'B'],
    confidence: [0, 1],
    confidenceReasons: [['a'], ['b']],
  };

  const trace = buildGymTrace(series);

  assert.deepEqual(trace.marker.color, ['rgb(214, 64, 69)', 'rgb(56, 201, 110)']);
  assert.equal(trace.text.length, 2);
  assert.match(trace.text[0], /Confidence: 0%/);
  assert.match(trace.text[1], /Confidence: 100%/);
});

test('buildGymMarkerColors defaults to low confidence color when metadata missing', () => {
  const series = { x: [1, 2, 3] };
  assert.deepEqual(buildGymMarkerColors(series), [
    'rgb(214, 64, 69)',
    'rgb(214, 64, 69)',
    'rgb(214, 64, 69)',
  ]);
});

test('fixture payload produces deterministic marker colors and fallback evidence copy', async () => {
  const fixturePath = path.resolve(__dirname, '../fixtures/surfaces/latest-confidence-sample.json');
  const payload = JSON.parse(await readFile(fixturePath, 'utf8'));
  const series = payload.series.gymCloud;

  const trace = buildGymTrace(series);

  assert.deepEqual(trace.marker.color, [
    'rgb(56, 201, 110)',
    'rgb(135, 133, 90)',
    'rgb(214, 64, 69)',
  ]);
  assert.match(trace.text[0], /Evidence: source:api, source:history/);
  assert.match(trace.text[2], /Evidence: missing-provenance-record/);
});
