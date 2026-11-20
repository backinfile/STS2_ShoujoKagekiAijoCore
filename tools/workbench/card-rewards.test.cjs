'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const domain=require('./workbench-domain.js');
const flow=require('./route-statistics.js');
const reference=require('./pool-reference.js');
const comparison=require('./native-pool-comparison.js');
const {getMod}=require('./mods.cjs');

const workspaceId=getMod('junna').workspaceId;
const card=(id,rarity,fields={})=>({id,uid:id,name:id,rarity,cost:1,type:'技能',...fields});
const normalize=domain.create({seed:{workspaceId,data:{cards:[]}}});

test('reward views agree on native rarities and the Junna game exclusions',()=>{
  const cards=[
    card('starter','基础',{pool:true}),
    card('common','普通',{pool:false}),
    card('uncommon','罕见'),
    card('rare','稀有'),
    card('ancient','远古',{pool:true}),
    card('choice','稀有',{choiceOnly:true}),
    card('generated','普通',{cardPool:'衍生'}),
    card('colorless','罕见',{cardPool:'无色'}),
    card('JK11','罕见',{pool:true}),
    card('reserved','稀有',{reservedAt:'archived'}),
    card('deleted','稀有',{trashedAt:'archived'})
  ];
  const data=normalize({cards});
  const settings={workspaceId,onlyRewards:true};
  const defaults={routes:[],standard:[]};
  assert.deepEqual(flow.compute(data,defaults,settings).cards.map(c=>c.id),['common','uncommon','rare']);
  assert.deepEqual(flow.compute(data,defaults,{...settings,scope:'reserve'}).cards.map(c=>c.id),['reserved']);
  assert.equal(comparison.compute(cards,settings).columns[0].total,3);
  assert.equal(reference.compute(data,settings).total,5);
  assert.equal(reference.compute(data,{...settings,includeAll:true}).total,9);
  assert.deepEqual(data.cards.slice(0,9).map(c=>c.pool),[false,true,true,true,false,false,false,false,false]);
  assert.equal(domain.isRewardCard(cards[8],getMod('karen').workspaceId),true);
});

test('changing the knowledge group main preserves reward eligibility and card counts',()=>{
  const data=normalize({
    cards:[card('main','普通'),card('JK11','罕见')],
    partnerGroups:[{id:'group',uid:'group',mainCardId:'main',grantedCardIds:['JK11'],grantedCardCounts:{JK11:2}}]
  });
  assert.equal(domain.setGroupMain(data,'group','JK11'),true);
  const updated=normalize(data);
  assert.deepEqual(updated.partnerGroups[0],{
    id:'group',uid:'group',title:'JK11',mainCardId:'JK11',mainCardCount:2,
    grantedCardIds:['main'],grantedCardCounts:{main:1}
  });
  assert.deepEqual(updated.cards.map(c=>c.pool),[true,false]);
});
