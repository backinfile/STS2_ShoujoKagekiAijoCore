(function(root){
  'use strict';
  const COLLECTIONS=['mechanisms','terms','cards','partnerGroups','archetypes','styles','fitPool'];
  const META_COLLECTIONS=['powers','relics'];
  const ENTITY_COLLECTIONS=[...COLLECTIONS,...META_COLLECTIONS];
  function entityList(data,collection,create=false){
    if(!META_COLLECTIONS.includes(collection))return data[collection]||[];
    if(create){data.meta=data.meta||{};data.meta[collection]=data.meta[collection]||[];}
    return data.meta?.[collection]||[];
  }
  function rootEntity(data,key){if(key!=='meta')return data[key];const meta={...data.meta};for(const collection of META_COLLECTIONS)delete meta[collection];return meta;}
  function assignRoot(data,key,value){const next=JSON.parse(JSON.stringify(value));if(key==='meta')for(const collection of META_COLLECTIONS)if(!Object.hasOwn(next,collection)&&Object.hasOwn(data.meta||{},collection))next[collection]=data.meta[collection];data[key]=next;}
  const TYPES=['攻击','技能','能力','箭矢','状态','诅咒','任务'],RARITIES=['基础','普通','罕见','稀有','远古','事件','衍生','状态','诅咒','任务'];
  const CARD_POOLS=['角色','无色','事件','衍生','状态','诅咒','任务'];
  function groupCardCount(group,cardId){return Math.max(1,Math.floor(Number(cardId===group.mainCardId?group.mainCardCount:group.grantedCardCounts?.[cardId])||1));}
  function setGroupMain(data,groupUid,cardUid){
    const group=data.partnerGroups.find(entry=>entry.uid===groupUid),card=data.cards.find(entry=>entry.uid===cardUid);
    if(!group||!card||card.trashedAt)throw Error('请选择组内的知识牌');
    const ids=[group.mainCardId,...group.grantedCardIds];
    if(group.mainCardId===card.id){
      if(card.pool)return false;
      const sourceGroup=data.partnerGroups.find(entry=>{
        const source=data.cards.find(candidate=>candidate.id===entry.mainCardId);
        return source?.pool&&!source.trashedAt&&entry.grantedCardIds.length+1===ids.length&&[entry.mainCardId,...entry.grantedCardIds].every(id=>ids.includes(id));
      });
      if(sourceGroup)return setGroupMain(data,sourceGroup.uid,cardUid);
      card.pool=card.rarity!=='基础';
      return card.pool;
    }
    if(!group.grantedCardIds.includes(card.id))throw Error('此牌不属于该知识组');
    const oldMain=data.cards.find(entry=>entry.id===group.mainCardId);
    if(!oldMain)throw Error('知识组缺少主牌');
    const targetGroup=data.partnerGroups.find(entry=>entry.mainCardId===card.id);
    if(targetGroup){
      const targetIds=new Set([targetGroup.mainCardId,...targetGroup.grantedCardIds]);
      if(targetIds.size!==ids.length||ids.some(id=>!targetIds.has(id)))throw Error('此牌已有自己的关联牌列表');
    }else{
      const counts=Object.fromEntries(ids.map(id=>[id,groupCardCount(group,id)]));
      group.mainCardId=card.id;group.grantedCardIds=ids.filter(id=>id!==card.id);
      group.grantedCardCounts=Object.fromEntries(group.grantedCardIds.map(id=>[id,counts[id]]));
      if(counts[card.id]>1)group.mainCardCount=counts[card.id];else delete group.mainCardCount;
      if(!group.title||[oldMain.id,oldMain.name].includes(group.title))group.title=card.name;
    }
    oldMain.pool=false;card.pool=card.rarity!=='基础';
    return true;
  }
  function cardGroup(data,cardUid,originUid=cardUid){
    const card=data.cards.find(entry=>entry.uid===cardUid),origin=data.cards.find(entry=>entry.uid===originUid);if(!card)return null;
    const sourceGroup=data.partnerGroups.find(group=>group.mainCardId===origin?.id);
    const fromSource=sourceGroup&&(sourceGroup.mainCardId===card.id||sourceGroup.grantedCardIds.includes(card.id));
    const group=fromSource?sourceGroup:data.partnerGroups.find(group=>group.mainCardId===card.id);
    if(!group)return null;
    const ids=new Set([group.mainCardId,...group.grantedCardIds]);
    const cards=data.cards.filter(entry=>ids.has(entry.id));
    const memberGroups=data.partnerGroups.filter(other=>
      ids.has(other.mainCardId)&&other.grantedCardIds.length+1===ids.size&&other.grantedCardIds.every(id=>ids.has(id))
    );
    const memberMainIds=new Set(memberGroups.map(other=>other.mainCardId));
    const otherGroups=data.partnerGroups.filter(other=>
      !memberGroups.includes(other)&&data.cards.some(entry=>entry.id===other.mainCardId&&!entry.trashedAt)
    );
    const sharedIds=new Set(otherGroups.flatMap(other=>[other.mainCardId,...other.grantedCardIds]));
    const movableCards=cards.filter(entry=>!entry.trashedAt&&(
      entry.id===group.mainCardId||!sharedIds.has(entry.id)&&(!entry.pool||memberMainIds.has(entry.id))
    ));
    return {group,cards,movableCards,reserveAction:movableCards.some(entry=>entry.reservedAt)?'pool':'reserve'};
  }
  function moveCards(data,cardUid,action,{group=false,originUid=cardUid,at=new Date().toISOString()}={}){
    if(!['trash','reserve','pool'].includes(action))throw Error('未知卡牌操作');
    const selected=data.cards.find(entry=>entry.uid===cardUid);if(!selected||selected.trashedAt)return [];
    let cards=[selected];
    if(group){
      const context=cardGroup(data,cardUid,originUid);
      if(!context)return [];
      cards=context.movableCards;
    }
    const changed=[];
    for(const card of cards){
      if(card.trashedAt)continue;
      if(action==='trash')card.trashedAt=at;
      else if(action==='reserve'){if(card.reservedAt)continue;card.reservedAt=at;}
      else {if(!card.reservedAt)continue;delete card.reservedAt;}
      changed.push(card.uid);
    }
    return changed;
  }
  function create({seed,uid,JunnaFitPool}){
    const clone=x=>JSON.parse(JSON.stringify(x));
  function normalizeData(raw){
    const d=clone(raw);if(!d||typeof d!=='object')throw Error('缺少制作台数据');
    for(const key of COLLECTIONS){if(!Array.isArray(d[key]))d[key]=[];const seen=new Set(),seenUid=new Set();for(const e of d[key]){if(!e||typeof e!=='object')throw Error('条目格式有误');e.uid=String(e.uid||uid());e.id=String(e.id||uid());if(seen.has(e.id))throw Error('编号重复：'+e.id);if(seenUid.has(e.uid))throw Error('内部编号重复：'+e.id);seen.add(e.id);seenUid.add(e.uid);}}
    d.title=String(d.title||'纯那 Mod 设计工作台');d.meta=d.meta&&typeof d.meta==='object'?d.meta:{};
    for(const collection of META_COLLECTIONS){const list=entityList(d,collection),ids=new Set(),uids=new Set();if(!Array.isArray(list))throw Error('条目格式有误：'+collection);for(const entry of list){if(!entry||typeof entry!=='object'||!entry.uid||!entry.id)throw Error('条目缺少编号：'+collection);if(ids.has(entry.id)||uids.has(entry.uid))throw Error('条目编号重复：'+entry.id);ids.add(entry.id);uids.add(entry.uid);}}
    const cardIds=new Set(d.cards.map(c=>c.id)),termIds=new Set(d.terms.map(t=>t.id)),archetypeIds=new Set(d.archetypes.map(a=>a.id)),knowledgeTerm=d.terms.find(t=>t.kind==='关键词'&&t.name==='知识'),oldWordIds=new Set(d.terms.filter(t=>t.kind==='关键词'&&t.name==='言葉').map(t=>t.id));
    d.partnerGroups=d.partnerGroups.filter(g=>cardIds.has(g.mainCardId)).map(g=>({...g,grantedCardIds:[...new Set(Array.isArray(g.grantedCardIds)?g.grantedCardIds:[g.grantedCardId].filter(Boolean))].filter(id=>cardIds.has(id)&&id!==g.mainCardId)})).filter(g=>g.grantedCardIds.length||groupCardCount(g,g.mainCardId)>1);
    const mains=new Set(d.partnerGroups.map(g=>g.mainCardId)),grants=new Set(d.partnerGroups.flatMap(g=>g.grantedCardIds));
    if(mains.size!==d.partnerGroups.length)throw Error('关联牌列表重复');
    for(const g of d.partnerGroups){g.grantedCardCounts=Object.fromEntries(g.grantedCardIds.map(id=>[id,groupCardCount(g,id)]));if(Object.hasOwn(g,'mainCardCount')){const count=groupCardCount(g,g.mainCardId);if(count>1)g.mainCardCount=count;else delete g.mainCardCount;}}
    for(const c of d.cards){
      if(c.arrowFocus!==undefined){
        if(!c.arrowFocus||!['x','y'].every(axis=>typeof c.arrowFocus[axis]==='number'&&Number.isFinite(c.arrowFocus[axis])&&c.arrowFocus[axis]>=0&&c.arrowFocus[axis]<=1))throw Error('箭头聚焦坐标须为0到1：'+c.id);
        c.arrowFocus={x:c.arrowFocus.x,y:c.arrowFocus.y};
      }
      if(c.type==='\u661f\u77e2')c.type='箭矢';
      if(c.type==='言葉')c.type='箭矢';
      if(c.cardPool!==undefined&&!CARD_POOLS.includes(c.cardPool))throw Error('卡池无效：'+c.id);
      c.name=String(c.name||'未命名');c.text=String(c.text??'');c.upgradedText=String(c.upgradedText??c.text);c.type=TYPES.includes(c.type)?c.type:'技能';c.cost=String(c.cost).toUpperCase()==='X'?'X':Math.max(0,Math.floor(Number(c.cost)||0));c.upgradedCost=String(c.upgradedCost??c.cost).toUpperCase()==='X'?'X':Math.max(0,Math.floor(Number(c.upgradedCost??c.cost)||0));c.turns=c.type==='箭矢'?(String(c.turns).trim().toUpperCase()==='X'?'X':Math.max(0,Math.floor(Number(c.turns)||0))):null;c.upgradedTurns=c.type==='箭矢'?(String(c.upgradedTurns??c.turns).trim().toUpperCase()==='X'?'X':Math.max(0,Math.floor(Number(c.upgradedTurns??c.turns)||0))):null;
      if(c.type==='箭矢'){const subtype=c.arrowSubtype??seed.data.cards.find(entry=>entry.id===c.id)?.arrowSubtype??'技能';if(!['攻击','技能','能力'].includes(subtype))throw Error('箭矢子类型无效：'+c.id);c.arrowSubtype=subtype;}
      c.pool=typeof c.pool==='boolean'?c.pool:true;c.rarity=RARITIES.includes(c.rarity)?c.rarity:'普通';c.system=c.system||'stage';c.keywords=[...new Set(Array.isArray(c.keywords)?c.keywords:[])];c.termIds=[...new Set(c.termIds||[])].filter(id=>termIds.has(id));c.archetypeIds=[...new Set(c.archetypeIds||[])].filter(id=>archetypeIds.has(id));c.breakthroughIds=c.termIds.filter(id=>d.terms.some(t=>t.id===id&&t.kind==='突破'));c.naming=c.naming&&typeof c.naming==='object'?c.naming:{};c.art=c.art||'defaultPortrait';
      c.partnerCardIds=[];
      c.keywords=c.keywords.filter(k=>!['言葉','知识牌'].includes(k));c.termIds=c.termIds.filter(id=>!oldWordIds.has(id));
      if(mains.has(c.id)||grants.has(c.id)){c.keywords=[...new Set([...c.keywords,'知识'])];if(knowledgeTerm)c.termIds=[...new Set([...c.termIds,knowledgeTerm.id])];if(!c.text.startsWith('知识。'))c.text='知识。'+c.text;if(!c.upgradedText.startsWith('知识。'))c.upgradedText='知识。'+c.upgradedText;}
    }
    for(const g of d.partnerGroups)d.cards.find(c=>c.id===g.mainCardId).partnerCardIds=[...g.grantedCardIds];
    if(Array.isArray(d.meta.startingDeck))d.meta.startingDeck=d.meta.startingDeck.filter(e=>cardIds.has(e.cardId));
    for(const t of d.terms){t.name=String(t.name||'未命名词条');t.kind=String(t.kind||'效果');t.template=String(t.template??'');t.definition=String(t.definition??'');t.systems=Array.isArray(t.systems)?t.systems:[];}
    for(const m of d.mechanisms){m.name=String(m.name||'未命名机制');m.rules=Array.isArray(m.rules)?m.rules:[];m.templates=Array.isArray(m.templates)?m.templates:[];}
    for(const style of d.styles){style.name=String(style.name||'未命名条目');style.content=String(style.content??'');}
    for(const entry of d.fitPool)JunnaFitPool.normalizeEntry(entry);
    return d;
  }

    return normalizeData;
  }
  const api={COLLECTIONS,ENTITY_COLLECTIONS,CARD_POOLS,entityList,rootEntity,assignRoot,create,cardGroup,moveCards,groupCardCount,setGroupMain};
  if(typeof module==='object'&&module.exports)module.exports=api;
  else root.JunnaWorkbenchDomain=api;
})(typeof globalThis==='object'?globalThis:this);
