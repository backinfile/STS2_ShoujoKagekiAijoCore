(function(root,factory){
  const api=factory(typeof module==='object'&&module.exports?require('./native-card-pools.js'):root.JunnaNativeCardPools);
  if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaNativePoolComparison=api;
})(typeof globalThis==='object'?globalThis:this,function(native){
  'use strict';
  const keys=['rarity','type','cost','upgradedCost'];
  const isReward=card=>card.pool&&['普通','罕见','稀有'].includes(card.rarity);
  const value=(card,key)=>String((key==='type'&&card.type==='箭矢'?card.arrowSubtype:card[key])??'')||'未分类';
  function compute(cards,{dimensions=[],includeAll=false,modName='纯那'}={}){
    const shared=dimensions.filter(d=>keys.includes(d.key));
    const columns=[{id:'junna',name:modName,cards},...native.characters].map(character=>({...character,cards:character.cards.filter(card=>!card.trashedAt&&!card.reservedAt&&(includeAll||isReward(card)))}));
    const groups=new Map(),collator=new Intl.Collator('zh-CN',{numeric:true});
    columns.forEach((column,index)=>{
      for(const card of column.cards){
        const values=shared.map(d=>value(card,d.key)),key=JSON.stringify(values);
        if(!groups.has(key))groups.set(key,{values,counts:columns.map(()=>0)});
        groups.get(key).counts[index]++;
      }
    });
    const rows=[...groups.values()].sort((a,b)=>{
      for(let i=0;i<shared.length;i++){
        const order=shared[i].order,rank=v=>{const index=order?.indexOf(v);return index>=0?index:order?.length||0;};
        const result=(order?rank(a.values[i])-rank(b.values[i]):0)||collator.compare(a.values[i],b.values[i]);
        if(result)return result;
      }
      return 0;
    });
    return{dimensions:shared,columns:columns.map(({id,name,cards})=>({id,name,total:cards.length})),rows};
  }
  return{compute};
});
