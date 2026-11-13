'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

function buildPwa(publicDirectory, { editorDirectory } = {}) {
  const assets = path.join(publicDirectory, 'assets');
  fs.mkdirSync(assets, { recursive: true });
  function write(bytes, extension) {
    const name = crypto.createHash('sha256').update(bytes).digest('hex').slice(0, 24) + '.' + extension;
    fs.writeFileSync(path.join(assets, name), bytes);
    return '/assets/' + name;
  }
  const icons = [192, 512].map(size => ({
    src: write(fs.readFileSync(path.join(__dirname, 'pwa', 'icon-' + size + '.png')), 'png'),
    sizes: size + 'x' + size, type: 'image/png', purpose: 'any maskable'
  }));
  const manifest = {
    id: './', name: '少女歌剧工作台' + (editorDirectory ? '（编辑）' : ''),
    short_name: editorDirectory ? '少女歌剧工作台·编辑' : '少女歌剧工作台', lang: 'zh-CN',
    start_url: './', scope: './', display: 'standalone',
    background_color: '#131b21', theme_color: '#1b252d', icons
  };
  const bytes = Buffer.from(JSON.stringify(manifest) + '\n');
  let manifestURL;
  if (editorDirectory) {
    fs.mkdirSync(editorDirectory, { recursive: true });
    fs.writeFileSync(path.join(editorDirectory, 'app.webmanifest'), bytes);
    manifestURL = './app.webmanifest';
  } else {
    // A manifest in /assets/ needs explicit root URLs; its hashed URL survives snapshot publication.
    manifest.id = manifest.start_url = manifest.scope = '/';
    manifestURL = write(Buffer.from(JSON.stringify(manifest) + '\n'), 'webmanifest');
  }
  return '<link data-workbench-pwa rel="manifest" href="' + manifestURL + '">' +
    '<link data-workbench-pwa rel="icon" type="image/png" href="' + icons[0].src + '">' +
    '<link data-workbench-pwa rel="apple-touch-icon" href="' + icons[0].src + '">' +
    '<meta data-workbench-pwa name="theme-color" content="#1b252d">';
}

module.exports = { buildPwa };
