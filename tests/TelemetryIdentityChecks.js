const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../web/telemetry/app.js'), 'utf8');
const identity = source.slice(source.indexOf('function model('), source.indexOf('\nfunction tag('));
const grouping = source.split(/\r?\n/).find(line => line.startsWith('function groupPile('));
const context = {state: {catalog: {}, relics: {}}, listOf: value => Array.isArray(value) ? value : []};
vm.createContext(context);
vm.runInContext(identity + '\n' + grouping, context);
const cards = [
    {id: {Entry: 'SHARED'}, model_full_id: 'CARD.MOD_A.SHARED'},
    {id: {Entry: 'SHARED'}, model_full_id: 'CARD.MOD_B.SHARED'}
];
assert.equal(context.groupPile(cards).length, 2);
assert.equal(context.meta({id: {Entry: 'SHARED'}, model_full_id: 'Other.SHARED'}).id, 'Other.SHARED');
assert.equal(context.meta({id: {Entry: 'SHARED'}, model_full_id: 'CARD.SHARED'}).id, 'SHARED');
assert.equal(context.meta({id: {Entry: 'SHARED'}}).id, 'SHARED');
assert.equal(context.groupPile([cards[0], cards[0]])[0].count, 2);
assert.equal(context.groupPile([{id: 'SHARED'}, {id: 'SHARED', current_upgrade_level: 1}]).length, 2);
console.log('PASS full identities, missing catalogues, legacy aliases and upgrade-level grouping');
