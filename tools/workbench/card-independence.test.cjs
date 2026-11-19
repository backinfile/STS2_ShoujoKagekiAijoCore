const test=require('node:test'),assert=require('node:assert/strict');
const domain=require('./workbench-domain.js');
const normalize=domain.create({seed:{data:{cards:[]}},uid:()=>{throw Error('Missing fixture UID');}});
const terms=[{id:'knowledge',uid:'knowledge',kind:'关键词',name:'知识'},{id:'other',uid:'other',kind:'关键词',name:'消耗'}];
const card=(id,fields={})=>({id,uid:id,name:id,type:'能力',text:'获得1点格挡。',upgradedText:'获得2点格挡。',...fields});
const group=(main,grants,fields={})=>({id:'group-'+main,uid:'group-'+main,mainCardId:main,grantedCardIds:Object.keys(grants),grantedCardCounts:grants,...fields});

test('making a card independent removes outgoing and incoming knowledge grants, including archived sources',()=>{
  const data=normalize({
    cards:[card('selected',{pool:false,cardPool:'角色',keywords:['消耗'],termIds:['other'],art:'chosen-art'}),card('active'),card('archived',{trashedAt:'2026-11-17T00:00:00Z'}),card('other')],
    terms,
    partnerGroups:[group('selected',{active:1},{mainCardCount:2}),group('active',{selected:3,other:2}),group('archived',{selected:1})]
  });
  const selected=data.cards[0];
  assert.equal(domain.canMakeCardIndependent(data,selected),true);
  domain.makeCardIndependent(data,selected.uid);
  const saved=normalize(data);
  assert.deepEqual(saved.cards[0],{
    ...selected,partnerCardIds:[],keywords:['消耗'],termIds:['other'],text:'获得1点格挡。',upgradedText:'获得2点格挡。'
  });
  assert.deepEqual(saved.partnerGroups,[group('active',{other:2})]);
  assert.deepEqual(saved.cards[1].partnerCardIds,['other']);
  assert.deepEqual(saved.cards[2].partnerCardIds,[]);
  assert.equal(saved.cards[2].trashedAt,'2026-11-17T00:00:00Z');
  assert.equal(domain.canMakeCardIndependent(saved,saved.cards[0]),false);
});

test('making a card independent clears saved knowledge markers even after the links were already removed',()=>{
  const data=normalize({terms,cards:[card('selected',{
    text:'知识。 知识。\n获得1点格挡。参考知识来源。',
    upgradedText:'知识。获得2点格挡。',keywords:['知识','消耗'],termIds:['knowledge','other']
  })]});
  const selected=data.cards[0];
  assert.equal(domain.canMakeCardIndependent(data,selected),true);
  domain.makeCardIndependent(data,selected.uid);
  const saved=normalize(data);
  assert.equal(saved.cards[0].text,'获得1点格挡。参考知识来源。');
  assert.equal(saved.cards[0].upgradedText,'获得2点格挡。');
  assert.deepEqual(saved.cards[0].keywords,['消耗']);
  assert.deepEqual(saved.cards[0].termIds,['other']);
  assert.equal(domain.canMakeCardIndependent(saved,saved.cards[0]),false);
});
