// La versión que muestra la web (dashboard/version.js) debe ser la misma del sistema
// (Directory.Build.props, que también fija la del instalador de la PC). Si se sube una y se olvida
// la otra, esta prueba falla.
//
// Ejecutar:  node --test tests/dashboard/version.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { APP_VERSION } from '../../dashboard/version.js';

test('la versión de la web coincide con la de Directory.Build.props', () => {
  const props = readFileSync(new URL('../../Directory.Build.props', import.meta.url), 'utf8');
  const match = props.match(/<Version>([^<]+)<\/Version>/);
  assert.ok(match, 'no se encontró <Version> en Directory.Build.props');
  assert.equal(APP_VERSION, match[1].trim());
});

test('la versión tiene formato X.Y.Z', () => {
  assert.match(APP_VERSION, /^\d+\.\d+\.\d+$/);
});
