(() => {
  'use strict';
  if (!document.querySelector('link[rel="manifest"]') || !window.isSecureContext) return;
  const standalone = matchMedia('(display-mode: standalone)');
  const button = document.createElement('button');
  button.id = 'install-app';
  button.className = 'button';
  button.dataset.workbenchPwa = '';
  button.textContent = '安装应用';
  button.hidden = true;
  const actions = document.querySelector(document.body.classList.contains('readonly')
    ? '.global-actions' : '.global-actions .menu-items');
  if (!actions) return;
  actions.append(button);
  let pending = null;
  const sync = () => { button.hidden = !pending || standalone.matches || navigator.standalone === true; };
  window.addEventListener('beforeinstallprompt', event => {
    event.preventDefault();
    pending = event;
    sync();
  });
  window.addEventListener('appinstalled', () => { pending = null; sync(); });
  standalone.addEventListener('change', sync);
  button.addEventListener('click', async () => {
    if (!pending) return;
    const prompt = pending;
    pending = null;
    sync();
    button.closest('details')?.removeAttribute('open');
    try {
      await prompt.prompt();
      await prompt.userChoice;
    } catch (error) {
      console.warn('工作台安装未完成', error);
    }
  });
})();
