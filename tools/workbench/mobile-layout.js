(function (root) {
  'use strict';

  function create({ dom, closeInspector, fitCards }) {
    const narrowScreen = matchMedia('(max-width: 760px), (max-width: 1100px) and (pointer: coarse)');
    const filters = dom('sidebar');
    const inspector = dom('inspector');
    const filtersDialog = dom('mobile-filters-dialog');
    const inspectorDialog = dom('mobile-inspector-dialog');
    const filtersButton = dom('mobile-filters-button');
    const inspectorButton = dom('mobile-inspector-button');
    const actionMenu = document.querySelector('.global-actions .menu');
    const menuItems = actionMenu.querySelector('.menu-items');
    const menuSummary = actionMenu.querySelector('summary');
    const firstFileAction = menuItems.firstElementChild;
    const secondaryActions = ['undo', 'redo', 'history-button'].map(id => {
      const button = dom(id);
      const anchor = document.createComment(id + ' position');
      button.before(anchor);
      button.addEventListener('click', () => { actionMenu.open = false; });
      return { button, anchor };
    });
    const filtersAnchor = document.createComment('sidebar position');
    const inspectorAnchor = document.createComment('inspector position');
    filters.before(filtersAnchor);
    inspector.before(inspectorAnchor);
    let preserveSelection = false;
    let updatePending = false;
    let activeTab = null;

    function syncActions() {
      for (const { button, anchor } of secondaryActions) {
        if (narrowScreen.matches) {
          if (button.parentElement !== menuItems) menuItems.insertBefore(button, firstFileAction);
        } else if (button.parentElement !== anchor.parentElement) anchor.after(button);
      }
      const label = narrowScreen.matches ? '操作' : '文件';
      if (menuSummary.textContent !== label) menuSummary.textContent = label;
    }

    function scrollToTop() {
      dom('workspace').scrollTop = 0;
      dom('work-area').scrollTop = 0;
    }

    function openFilters() {
      if (!narrowScreen.matches || filtersDialog.open) return;
      filtersDialog.append(filters);
      filtersDialog.showModal();
      filtersButton.setAttribute('aria-expanded', 'true');
    }

    function openInspector() {
      if (!narrowScreen.matches || inspectorDialog.open) return;
      inspectorDialog.append(inspector);
      inspectorDialog.showModal();
      inspectorButton.setAttribute('aria-expanded', 'true');
      fitCards();
    }

    filtersButton.addEventListener('click', openFilters);
    inspectorButton.addEventListener('click', openInspector);
    dom('mobile-close-filters').addEventListener('click', () => filtersDialog.close());
    dom('mobile-close-inspector').addEventListener('click', () => inspectorDialog.close());

    filtersDialog.addEventListener('close', () => {
      filtersAnchor.after(filters);
      filtersButton.setAttribute('aria-expanded', 'false');
    });

    inspectorDialog.addEventListener('close', () => {
      inspectorAnchor.after(inspector);
      inspectorButton.setAttribute('aria-expanded', 'false');
      if (preserveSelection) preserveSelection = false;
      else closeInspector();
      fitCards();
    });

    function sync() {
      updatePending = false;
      syncActions();
      filtersButton.disabled = !filters.children.length;
      inspectorButton.disabled = !dom('editor-form').children.length && !dom('preview').children.length;
      if (!narrowScreen.matches) return;
      if (!dom('close-inspector').hidden) openInspector();
      const selectedTab = document.querySelector('.tabs [aria-selected="true"]');
      if (selectedTab !== activeTab) {
        activeTab = selectedTab;
        selectedTab?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
      }
    }

    function update() {
      if (updatePending) return;
      updatePending = true;
      queueMicrotask(sync);
    }

    narrowScreen.addEventListener('change', () => {
      if (!narrowScreen.matches) {
        filtersDialog.close();
        if (inspectorDialog.open) {
          preserveSelection = true;
          inspectorDialog.close();
        }
      }
      update();
    });

    return { update, scrollToTop };
  }

  root.JunnaMobileLayout = { create };
})(globalThis);
