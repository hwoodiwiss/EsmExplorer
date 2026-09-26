import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../../src/EsmParser.Web/wwwroot/js/dataRoot.js', import.meta.url), 'utf8');
const root = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const file = (name, text) => ({ name, kind: 'file', getFile: async () => new Blob([text]) });
const dir = (name, entries) => ({
  name, kind: 'directory',
  async *values() { yield* entries; },
  async getFileHandle(name) { const e = entries.find(e => e.name === name && e.kind === 'file'); if (!e) throw Error('missing'); return e; },
  async getDirectoryHandle(name) { const e = entries.find(e => e.name === name && e.kind === 'directory'); if (!e) throw Error('missing'); return e; },
});

test('directory handles resolve case-insensitively and stream file slices', async () => {
  globalThis.window = { showDirectoryPicker: async () => dir('Data', [file('Meshes.bsa', 'archive'), dir('Meshes', [file('Test.nif', 'model')])]) };
  assert.equal(await root.pickDataRoot(), 'Data');
  assert.deepEqual(await root.listFiles(), ['Meshes.bsa']);
  const opened = await root.openFile('DATA\\meshes\\TEST.NIF');
  assert.equal(opened.length, 5);
  assert.deepEqual(await root.readFileChunk(opened.id, 1, 3), new TextEncoder().encode('ode'));
  root.closeFile(opened.id);
  await assert.rejects(root.readFileChunk(opened.id, 0, 1));
  assert.equal(await root.openFile('missing.nif'), null);
  assert.equal(await root.openFile('../outside.nif'), null);
  window.showDirectoryPicker = async () => { throw Error('cancelled'); };
  assert.equal(await root.pickDataRoot(), null);
  assert.notEqual(await root.openFile('meshes.bsa'), null);
});
