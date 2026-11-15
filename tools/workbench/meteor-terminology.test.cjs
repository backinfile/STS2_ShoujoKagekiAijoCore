const test=require('node:test'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const domain=require('./workbench-domain.js'),fit=require('./fit-pool-view.js'),seed=require('./seed.json');
const normalize=domain.create({seed,uid:crypto.randomUUID,JunnaFitPool:fit});
test('new and legacy meteor types preserve subtype, countdown, cost and stable identifiers',()=>{
  const variants=['流星','\u7bad\u77e2','\u661f\u77e2','言葉'];
  for(const type of variants)for(const turns of [0,2,'X']){
    const card={uid:'stable-card',id:'JA81B',name:'星屑',type,arrowSubtype:'攻击',cost:0,upgradedCost:0,turns,upgradedTurns:turns==='X'?'X':1,text:'造成X点伤害。消耗。',upgradedText:'造成X点伤害。消耗。'};
    const actual=normalize({...structuredClone(seed.data),cards:[card]}).cards[0];
    for(const key of ['uid','id','name','arrowSubtype','cost','upgradedCost','turns','upgradedTurns','text','upgradedText'])assert.deepEqual(actual[key],card[key],type+'/'+key);
    assert.equal(actual.type,'流星');
  }
});
test('ordinary cards retain their type and have no meteor countdown',()=>{
  for(const type of ['攻击','技能','能力']){
    const card=normalize({...structuredClone(seed.data),cards:[{uid:'ordinary',id:'A',name:'普通牌',type,cost:1,text:'获得格挡。',turns:3,upgradedTurns:2}]}).cards[0];
    assert.equal(card.type,type);assert.equal(card.turns,null);assert.equal(card.upgradedTurns,null);
  }
});
