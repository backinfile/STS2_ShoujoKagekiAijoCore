const JunnaCardReferences=(()=>{
  'use strict';
  const escapePattern=value=>value.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
  const namePattern=name=>(/^[A-Za-z0-9_]/.test(name)?'(?<![A-Za-z0-9_])':'')+escapePattern(name)+'(?:\\+)?'+(/[A-Za-z0-9_]$/.test(name)?'(?![A-Za-z0-9_])':'');
  function findQuotedCard(label,cards){return cards.find(card=>card.name===label||card.id===label)||(/\+$/.test(label)?cards.find(card=>card.name===label.slice(0,-1)||card.id===label.slice(0,-1)):null);}
  function stripBrackets(text,cards){return String(text??'').replace(/《([^》]+)》/g,(match,label)=>{const card=findQuotedCard(label,cards);if(!card)return match;const suffix=label.endsWith('+')&&!card.name.endsWith('+')?'+':'';return card.name+suffix;});}
  function referencesFor(card,cards){
    if(!card)return [];
    const text=String(card.text??'')+'\n'+String(card.upgradedText??''),uids=new Set(card.design?.cardReferenceUids||[]),ids=new Set(card.partnerCardIds||[]);
    uids.add(card.uid);
    for(const match of text.matchAll(/《([^》]+)》/g)){const target=findQuotedCard(match[1],cards);if(target)uids.add(target.uid);}
    return cards.filter(target=>target.name&&(uids.has(target.uid)||ids.has(target.id))&&(new RegExp(namePattern(target.name)).test(text)||new RegExp(namePattern(target.id)).test(text))).sort((a,b)=>b.name.length-a.name.length);
  }
  function normalizeText(text,references){let value=stripBrackets(text,references);const targets=references.filter(card=>card.name!==card.id).sort((a,b)=>b.id.length-a.id.length);if(targets.length)value=value.replace(new RegExp(targets.map(card=>namePattern(card.id)).join('|'),'g'),label=>{const card=targets.find(card=>label===card.id||label===card.id+'+');return card.name+(label===card.id+'+'?'+':'');});return value;}
  function protect(text,references){
    const value=normalizeText(text,references),values=[];
    if(!references.length)return {text:value,values};
    const ordered=references.slice().sort((a,b)=>b.name.length-a.name.length),pattern=new RegExp(ordered.map(card=>namePattern(card.name)).join('|'),'g');
    return {text:value.replace(pattern,label=>{const card=ordered.find(card=>label===card.name||label===card.name+'+'),token='\uE000'+String.fromCodePoint(0xF0000+values.length)+'\uE001';values.push({token,card,text:label,upgraded:label===card.name+'+'});return token;}),values};
  }
  function renameInText(text,references,targetUid,nextName){
    const protectedText=protect(text,references);let value=protectedText.text;
    for(const item of protectedText.values)value=value.split(item.token).join(item.card.uid===targetUid?nextName+(item.upgraded?'+':''):item.text);
    return value;
  }
  return {stripBrackets,referencesFor,normalizeText,protect,renameInText};
})();
if(typeof module!=='undefined'&&module.exports)module.exports=JunnaCardReferences;
