(function(root){
  'use strict';
  const backgroundId='junna_knowledge_background';
  const extraTips=card=>(Array.isArray(card?.extraHoverTips)?card.extraHoverTips:[]).filter(tip=>tip&&typeof tip.title==='string'&&typeof tip.description==='string'&&tip.description.trim());
  const background=card=>extraTips(card).find(tip=>tip.id===backgroundId)?.description||'';
  function setBackground(card,description){
    const tips=(Array.isArray(card.extraHoverTips)?card.extraHoverTips:[]).filter(tip=>tip.id!==backgroundId);
    if(String(description).trim())tips.push({id:backgroundId,title:'知识背景',description:String(description).trim()});
    card.extraHoverTips=tips;
  }
  function create({cardByUid,escape}){
    const panel=document.createElement('div');panel.id='card-extra-hover-tips';panel.className='card-extra-hover-tips';panel.setAttribute('role','tooltip');panel.hidden=true;document.body.append(panel);
    let anchor=null;
    function hide(){if(anchor){const ids=(anchor.getAttribute('aria-describedby')||'').split(/\s+/).filter(id=>id&&id!==panel.id);if(ids.length)anchor.setAttribute('aria-describedby',ids.join(' '));else anchor.removeAttribute('aria-describedby');}anchor=null;panel.hidden=true;}
    function resolve(target){if(!(target instanceof Element))return null;const link=target.closest('[data-open-card]');if(link)return{element:link,uid:link.dataset.openCard};const host=target.closest('button,#preview'),face=target.closest('[data-hover-card]')||host?.querySelector('[data-hover-card]');return face?{element:face.closest('.card-button')||face.closest('#preview')||face,uid:face.dataset.hoverCard}:null;}
    function show(target){const found=resolve(target),tips=found&&extraTips(cardByUid(found.uid));if(!tips?.length){hide();return;}if(anchor===found.element&&!panel.hidden)return;hide();anchor=found.element;panel.innerHTML=tips.map(tip=>'<section><h3>'+escape(tip.title)+'</h3><p>'+escape(tip.description)+'</p></section>').join('');panel.hidden=false;const rect=anchor.getBoundingClientRect(),gap=12,edge=12,width=Math.min(300,innerWidth-edge*2);panel.style.width=width+'px';const height=panel.getBoundingClientRect().height;let left=rect.right+gap;if(left+width>innerWidth-edge)left=rect.left-width-gap;if(left<edge)left=Math.min(innerWidth-width-edge,Math.max(edge,rect.left));const top=Math.max(edge,Math.min(rect.top,innerHeight-height-edge));panel.style.left=left+'px';panel.style.top=top+'px';anchor.setAttribute('aria-describedby',[...(anchor.getAttribute('aria-describedby')||'').split(/\s+/).filter(Boolean),panel.id].join(' '));}
    document.addEventListener('pointerover',event=>{if(panel.contains(event.target))return;show(event.target);});
    document.addEventListener('pointerout',event=>{if(anchor?.contains(event.relatedTarget)||panel.contains(event.relatedTarget))return;hide();});
    document.addEventListener('focusin',event=>show(event.target));
    document.addEventListener('focusout',event=>{if(!anchor?.contains(event.relatedTarget))hide();});
    document.addEventListener('scroll',hide,true);document.addEventListener('click',hide);document.addEventListener('keydown',event=>{if(event.key==='Escape')hide();});window.addEventListener('resize',hide);
    return{hide};
  }
  const api={backgroundId,extraTips,background,setBackground,create};if(typeof module==='object'&&module.exports)module.exports=api;else root.JunnaCardHoverTips=api;
})(typeof globalThis==='object'?globalThis:this);
