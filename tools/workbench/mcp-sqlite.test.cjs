'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { randomUUID } = require('node:crypto');
const { WorkbenchStore } = require('./mcp-store.cjs');
const { SqliteStorage } = require('./mcp-sqlite.cjs');

function fixture(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'junna-sqlite-'));
  const file = path.join(directory, 'state.sqlite');
  const seed = { schemaVersion: 1, workspaceId: 'sqlite-test', revision: 1, assets: {}, history: [], mcpRequests: {}, entityVersions: {},
    data: { title: '测试', meta: {}, cards: [{ uid: 'a', id: 'A', name: '甲', text: '牌效', type: '技能', cost: 1 }],
      terms: [], mechanisms: [], partnerGroups: [], archetypes: [], styles: [], fitPool: [] } };
  const storage = new SqliteStorage(file);
  storage.import(seed);
  storage.close();
  const store = new WorkbenchStore({ file, seed, workspaceId: seed.workspaceId });
  t.after(() => { store.storage.close(); fs.rmSync(directory, { recursive: true, force: true }); });
  return { store, file, seed };
}

test('SQLite saves, replays, conflicts and restores in durable transactions', async t => {
  const { store } = fixture(t);
  const current = store.get({ collection: 'cards', uid: 'a' });
  const input = { requestId: randomUUID(), clientId: 'test', label: '编辑', changes: [
    { collection: 'cards', uid: 'a', expectedVersion: current.version, before: current.entity, after: { ...current.entity, name: '乙' } }
  ] };
  const result = await store.commit(input);
  assert.equal((await store.commit(input)).replayed, true);
  await assert.rejects(store.commit({ ...input, requestId: randomUUID() }), { code: 'VERSION_CONFLICT' });
  assert.equal(store.history().total, 1);
  const saved = store.get({ collection: 'cards', uid: 'a' });
  await store.commit({ requestId: randomUUID(), clientId: 'test', label: '恢复', restore: {
    recordId: result.record.id, side: 'before', expectedVersions: { 'cards/a': saved.version }
  } });
  assert.equal(store.get({ collection: 'cards', uid: 'a' }).entity.name, '甲');
});

test('SQLite keeps image bytes in files and provides URLs, inline exports and incremental changes', async t => {
  const { store } = fixture(t);
  const initial = store.state();
  const image = 'data:image/png;base64,' + Buffer.from('image bytes').toString('base64');
  const current = store.get({ collection: 'cards', uid: 'a' });
  const input = { requestId: randomUUID(), clientId: 'test', label: '换图', assets: { portrait: image }, changes: [
    { collection: 'cards', uid: 'a', expectedVersion: current.version, updates: { art: 'portrait' } }
  ] };
  await store.commit(input);
  const delta = store.state({ incremental: true, assetURLs: true, knownRevision: initial.revision });
  assert.equal(delta.delta, true);
  assert.equal(delta.changes[0].after.art, 'portrait');
  assert.match(delta.assets.portrait, /^\/assets\/[a-f0-9]+\.png$/);
  assert.equal(store.assets(['portrait'], true).portrait, image);
  assert.equal(store.read().assets.portrait, image);
  assert.equal(store.history({ search: '换图', collection: 'cards' }).total, 1);
  assert.equal(store.history({ collection: 'terms' }).total, 0);
  const other = new WorkbenchStore({ file: store.file, seed: { data: store.read({ copy: false }).data }, workspaceId: 'sqlite-test' });
  try { assert.equal((await other.commit(input)).replayed, true); } finally { other.storage.close(); }
});

test('failed asset registration rolls back the entire batch', async t => {
  const { store } = fixture(t);
  const before = store.state();
  const original = store.storage.writeAsset;
  store.storage.writeAsset = () => { throw Error('simulated disk failure'); };
  await assert.rejects(store.commit({ requestId: randomUUID(), clientId: 'test', label: '失败', assets: {
    portrait: 'data:image/png;base64,aGVsbG8='
  }, changes: [{ collection: 'cards', uid: 'a', expectedVersion: store.get({ collection: 'cards', uid: 'a' }).version, updates: { name: '失败的修改' } }] }), /disk failure/);
  store.storage.writeAsset = original;
  assert.equal(store.state().revision, before.revision);
  assert.equal(store.get({ collection: 'cards', uid: 'a' }).entity.name, before.bundle.data.cards[0].name);
  assert.equal(store.history().total, 0);
});

test('web incrementally receives concurrent edits and its own acknowledged save', async t => {
  const { store } = fixture(t);
  const originalFetch = global.fetch;
  t.after(() => { global.fetch = originalFetch; });
  const calls = [];
  global.fetch = async (_url, options) => {
    const request = JSON.parse(options.body);
    if (request.method === 'initialize') return { ok: true, json: async () => ({ result: {} }) };
    const { name, arguments: args } = request.params;
    calls.push({ name, args });
    const value = name === 'workbench_get_state' ? store.state(args) : await store.commit(args);
    return { ok: true, json: async () => ({ result: { structuredContent: value } }) };
  };
  let bundle;
  const initial = store.state({ assetURLs: true });
  const controller = require('./mcp-web-client.js').create({ connection: { baseURL: '', token: '', assetURLs: true },
    initialSnapshot: initial, clientId: 'web', uuid: randomUUID, getAssets: () => bundle.assets,
    onSnapshot: value => { bundle = value; }, onCommit() {}, onStatus() {}, onDraft() {} });
  await controller.start();
  assert.equal(calls[0].args.knownRevision, initial.revision);
  const before = structuredClone(bundle.data.cards[0]);
  await store.commit({ requestId: randomUUID(), clientId: 'other', label: '另一端', changes: [
    { collection: 'cards', uid: 'a', expectedVersion: initial.versions['cards/a'], updates: { notes: '另一端备注' } }
  ] });
  controller.enqueue({ id: randomUUID(), kind: '编辑', label: '网页', patches: [
    { collection: 'cards', uid: 'a', before, after: { ...before, text: '网页牌效' } }
  ] });
  await controller.flush();
  await controller.pull();
  assert.equal(bundle.data.cards[0].text, '网页牌效');
  assert.equal(bundle.data.cards[0].notes, '另一端备注');
  assert.equal(calls.at(-1).args.knownRevision, initial.revision);
  assert.equal(bundle.revision, store.state().revision);
});
