'use strict';
const fs = require('node:fs');
const { SqliteStorage } = require('../mcp-sqlite.cjs');

function migrate(source, destination, assetDirectory) {
  const bundle = JSON.parse(fs.readFileSync(source, 'utf8').replace(/^\uFEFF/, ''));
  const storage = new SqliteStorage(destination, { assetDirectory });
  try {
    storage.import(bundle);
    const imported = storage.read();
    if (JSON.stringify(imported.data) !== JSON.stringify(bundle.data)) {
      // Object key ordering differs after relational reconstruction.
      require('node:assert/strict').deepEqual(imported.data, bundle.data);
    }
    const counts = {
      revision: imported.revision,
      assets: Object.keys(imported.assets).length,
      history: imported.historyTotal,
      receipts: storage.statement('SELECT count(*) AS total FROM receipts').get().total
    };
    require('node:assert/strict').equal(counts.history, bundle.history.length);
    require('node:assert/strict').equal(counts.receipts, Object.keys(bundle.mcpRequests || {}).length);
    console.log(JSON.stringify(counts));
    return counts;
  } finally {
    storage.close();
  }
}

if (require.main === module) migrate(...process.argv.slice(2));
module.exports = { migrate };
