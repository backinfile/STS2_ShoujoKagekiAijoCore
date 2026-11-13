// Presentation-only adaptation of the existing workbench. No service or editor transport.
(() => {
  'use strict';
  const allowed = new Set(['upgrade','clear-filters','meta','copy-template','cards-by-term']);
  const blocked = '#edit-title,#inspector-footer,#knowledge-edit,.global-actions> :not(.readonly-status):not(#install-app),[data-remove-grant],[data-restore-card],[data-restore],[data-delete-archetype],[data-edit-archetype]';
  document.addEventListener('click', event => {
    const target = event.target.closest?.('button,a,summary,input,select,textarea');
    if (!target) return;
    if (target.closest(blocked) || (target.hasAttribute('data-action') && !allowed.has(target.dataset.action))) {
      event.preventDefault(); event.stopImmediatePropagation();
    }
  }, true);
  document.addEventListener('submit', event => {event.preventDefault();event.stopImmediatePropagation();}, true);
  function present() {
    observer.disconnect();
    for (const button of document.querySelectorAll('[data-action]')) if (!allowed.has(button.dataset.action)) button.hidden = true;
    for (const node of document.querySelectorAll('[data-edit]')) {
      if (node.textContent.trim() === '编辑') node.textContent = '查看';
      for (const attr of ['aria-label','title']) if (node.hasAttribute(attr)) node.setAttribute(attr,node.getAttribute(attr).replace(/^编辑/,'查看'));
    }
    const title = document.getElementById('inspector-title');
    if (title?.textContent.startsWith('编辑')) title.textContent=title.textContent.replace(/^编辑/,'详情');
    const form = document.getElementById('editor-form');
    if (form) {
      for (const node of form.querySelectorAll('[data-add-grant]')) node.closest('.field')?.remove();
      for (const node of form.querySelectorAll('[data-tag-search]')) node.remove();
      for (const node of form.querySelectorAll('input,textarea,select')) {
        if (node.type === 'checkbox') {
          if (!node.checked) node.closest('label')?.remove();
          else {node.disabled=true;node.setAttribute('aria-readonly','true');}
          continue;
        }
        const value = document.createElement('div');
        value.className='readonly-value' + (node.classList.contains('code-field')?' code-field':'');
        value.textContent=node.tagName==='SELECT'?node.selectedOptions[0]?.textContent||'—':node.value||'—';
        node.replaceWith(value);
      }
    }
    observer.observe(document.body,{childList:true,subtree:true});
  }
  const observer=new MutationObserver(present);
  present();
})();
