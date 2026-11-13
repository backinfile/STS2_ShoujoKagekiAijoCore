'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { DatabaseSync } = require('node:sqlite');
const { ENTITY_COLLECTIONS, entityList, rootEntity, assignRoot } = require('./workbench-domain.js');

class SqliteStorage {
  constructor(file, { assetDirectory = path.join(path.dirname(file), 'assets') } = {}) {
    this.assetDirectory = assetDirectory;
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.mkdirSync(assetDirectory, { recursive: true });
    this.db = new DatabaseSync(file);
    this.db.exec(`
      PRAGMA busy_timeout=5000;
      PRAGMA synchronous=FULL;
      CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS entries (
        collection TEXT NOT NULL, uid TEXT NOT NULL, position INTEGER NOT NULL,
        value TEXT, counter INTEGER NOT NULL, updated INTEGER NOT NULL,
        PRIMARY KEY(collection, uid)
      );
      CREATE TABLE IF NOT EXISTS history (id TEXT PRIMARY KEY, seq INTEGER NOT NULL, value TEXT NOT NULL);
      CREATE INDEX IF NOT EXISTS history_seq ON history(seq);
      CREATE TABLE IF NOT EXISTS receipts (id TEXT PRIMARY KEY, hash TEXT NOT NULL, value TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS assets (
        id TEXT PRIMARY KEY, filename TEXT NOT NULL, mime TEXT NOT NULL, size INTEGER NOT NULL
      );
    `);
    this.statements = new Map();
  }

  statement(sql) {
    if (!this.statements.has(sql)) this.statements.set(sql, this.db.prepare(sql));
    return this.statements.get(sql);
  }

  transaction(run) {
    this.db.exec('BEGIN IMMEDIATE');
    this.writing = true;
    try {
      const result = run();
      this.db.exec('COMMIT');
      return result;
    } catch (error) {
      this.db.exec('ROLLBACK');
      throw error;
    } finally {
      this.writing = false;
    }
  }

  signature() {
    return this.statement("SELECT value FROM metadata WHERE key='revision'").get()?.value;
  }

  receipt(id) {
    const row = this.statement('SELECT hash, value FROM receipts WHERE id=?').get(id);
    return row && { hash: row.hash, result: JSON.parse(row.value) };
  }

  record(id) {
    const row = this.statement('SELECT value FROM history WHERE id=?').get(id);
    return row && JSON.parse(row.value);
  }

  records() {
    return this.statement('SELECT value FROM history ORDER BY seq').all().map(row => JSON.parse(row.value));
  }

  asset(id, inline = false) {
    const row = this.statement('SELECT * FROM assets WHERE id=?').get(id);
    if (!row) return undefined;
    if (!inline) return '/assets/' + row.filename;
    return 'data:' + row.mime + ';base64,' + fs.readFileSync(path.join(this.assetDirectory, row.filename)).toString('base64');
  }

  writeAsset(id, value) {
    const match = /^data:([^;,]+);base64,([\s\S]+)$/.exec(value);
    if (!match) throw Error('图片数据格式无效');
    const bytes = Buffer.from(match[2], 'base64');
    const extension = { 'image/png': 'png', 'image/jpeg': 'jpg', 'image/webp': 'webp', 'image/gif': 'gif', 'image/svg+xml': 'svg' }[match[1]];
    if (!extension) throw Error('图片类型无效');
    const filename = crypto.createHash('sha256').update(bytes).digest('hex').slice(0, 24) + '.' + extension;
    const previous = this.statement('SELECT filename FROM assets WHERE id=?').get(id);
    if (previous && previous.filename !== filename) {
      const error = Error('图片编号已存在');
      error.code = 'ASSET_CONFLICT';
      throw error;
    }
    const destination = path.join(this.assetDirectory, filename);
    if (!fs.existsSync(destination)) {
      const temporary = destination + '.pending-' + crypto.randomUUID();
      let descriptor;
      try {
        descriptor = fs.openSync(temporary, 'wx');
        fs.writeFileSync(descriptor, bytes);
        fs.fsyncSync(descriptor);
        fs.closeSync(descriptor);
        descriptor = undefined;
        fs.renameSync(temporary, destination);
      } finally {
        if (descriptor !== undefined) fs.closeSync(descriptor);
        if (fs.existsSync(temporary)) fs.unlinkSync(temporary);
      }
    }
    if (process.platform !== 'win32') {
      const directory = fs.openSync(this.assetDirectory, 'r');
      try { fs.fsyncSync(directory); } finally { fs.closeSync(directory); }
    }
    this.statement('INSERT OR IGNORE INTO assets VALUES (?, ?, ?, ?)').run(id, filename, match[1], bytes.length);
    return '/assets/' + filename;
  }

  read({ full = false } = {}) {
    if (!this.writing) this.db.exec('BEGIN');
    try {
      const stored = this.statement("SELECT value FROM metadata WHERE key='bundle'").get();
      if (!stored) throw Error('SQLite 工作台尚未初始化');
      const bundle = JSON.parse(stored.value);
      bundle.revision = Number(this.signature());
      bundle.entityVersions = {};
      bundle.cardUpdatedAt = {};
      for (const collection of ENTITY_COLLECTIONS) entityList(bundle.data, collection, true).length = 0;
      for (const row of this.statement('SELECT * FROM entries ORDER BY position, rowid').all()) {
        bundle.entityVersions[row.collection + '/' + row.uid] = row.counter;
        if (row.collection === 'cards') bundle.cardUpdatedAt[row.uid] = row.updated;
        if (row.value === null) continue;
        const value = JSON.parse(row.value);
        if (row.collection === 'root') assignRoot(bundle.data, row.uid, value);
        else entityList(bundle.data, row.collection, true).push(value);
      }
      bundle.assets = Object.fromEntries(this.statement('SELECT id, filename FROM assets').all().map(row => [row.id, '/assets/' + row.filename]));
      bundle.historyTotal = this.statement('SELECT count(*) AS total FROM history').get().total;
      bundle.history = full ? this.records() : [];
      bundle.mcpRequests = {};
      if (full) {
        for (const id of Object.keys(bundle.assets)) bundle.assets[id] = this.asset(id, true);
        for (const row of this.statement('SELECT * FROM receipts').all()) bundle.mcpRequests[row.id] = { hash: row.hash, result: JSON.parse(row.value) };
      }
      if (!this.writing) this.db.exec('COMMIT');
      return bundle;
    } catch (error) {
      if (!this.writing) this.db.exec('ROLLBACK');
      throw error;
    }
  }

  save(bundle, patches, result, requestId, requestHash, assets) {
    const entry = this.statement(`INSERT INTO entries VALUES (?, ?, ?, ?, ?, ?)
      ON CONFLICT(collection, uid) DO UPDATE SET position=excluded.position, value=excluded.value,
      counter=excluded.counter, updated=excluded.updated`);
    for (const patch of patches) {
      entry.run(patch.collection, patch.uid, patch.afterIndex ?? -1,
        patch.after === null ? null : JSON.stringify(patch.after),
        bundle.entityVersions[patch.collection + '/' + patch.uid] || 0, bundle.revision);
    }
    const reordered = new Set(patches.filter(patch => patch.collection !== 'root' && patch.beforeIndex !== patch.afterIndex).map(patch => patch.collection));
    const position = this.statement('UPDATE entries SET position=? WHERE collection=? AND uid=?');
    for (const collection of reordered) entityList(bundle.data, collection).forEach((value, index) => position.run(index, collection, value.uid));
    for (const [id, value] of Object.entries(assets)) this.writeAsset(id, value);
    if (result.record) this.statement('INSERT INTO history VALUES (?, ?, ?)').run(result.record.id, result.record.seq, JSON.stringify(result.record));
    this.statement("INSERT OR REPLACE INTO metadata VALUES ('revision', ?)").run(String(bundle.revision));
    this.statement('INSERT INTO receipts VALUES (?, ?, ?)').run(requestId, requestHash, JSON.stringify(result));
  }

  import(bundle) {
    if (this.signature() !== undefined) throw Error('目标 SQLite 工作台已有数据');
    this.transaction(() => {
      const { assets, history, mcpRequests, entityVersions, cardUpdatedAt, historyTotal, ...metadata } = bundle;
      metadata.data = JSON.parse(JSON.stringify(bundle.data));
      for (const collection of ENTITY_COLLECTIONS) entityList(metadata.data, collection, true).length = 0;
      this.statement("INSERT INTO metadata VALUES ('bundle', ?)").run(JSON.stringify(metadata));
      this.statement("INSERT INTO metadata VALUES ('revision', ?)").run(String(bundle.revision));
      const updated = {};
      for (const record of history || []) for (const patch of record.patches) updated[patch.collection + '/' + patch.uid] = record.seq;
      const insert = this.statement('INSERT OR REPLACE INTO entries VALUES (?, ?, ?, ?, ?, ?)');
      for (const collection of ENTITY_COLLECTIONS) entityList(bundle.data, collection).forEach((value, index) => {
        const key = collection + '/' + value.uid;
        insert.run(collection, value.uid, index, JSON.stringify(value), entityVersions?.[key] || 0, updated[key] || 0);
      });
      for (const uid of ['title', 'meta']) insert.run('root', uid, -1, JSON.stringify(rootEntity(bundle.data, uid)), entityVersions?.['root/' + uid] || 0, updated['root/' + uid] || 0);
      for (const [key, counter] of Object.entries(entityVersions || {})) {
        const slash = key.indexOf('/');
        this.statement('INSERT OR IGNORE INTO entries VALUES (?, ?, -1, NULL, ?, ?)').run(key.slice(0, slash), key.slice(slash + 1), counter, updated[key] || 0);
      }
      for (const [id, value] of Object.entries(assets || {})) this.writeAsset(id, value);
      for (const record of history || []) this.statement('INSERT INTO history VALUES (?, ?, ?)').run(record.id, record.seq, JSON.stringify(record));
      for (const [id, receipt] of Object.entries(mcpRequests || {})) this.statement('INSERT INTO receipts VALUES (?, ?, ?)').run(id, receipt.hash, JSON.stringify(receipt.result));
    });
  }

  close() { this.db.close(); }
}

module.exports = { SqliteStorage };
