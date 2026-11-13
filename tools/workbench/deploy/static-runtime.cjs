'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const zlib = require('node:zlib');

function externalize(html, directory) {
  const assets = path.join(directory, 'assets');
  fs.mkdirSync(assets, { recursive: true });
  function write(content, extension) {
    const filename = crypto.createHash('sha256').update(content).digest('hex').slice(0, 24) + '.' + extension;
    const file = path.join(assets, filename);
    if (!fs.existsSync(file)) {
      fs.writeFileSync(file, content);
      fs.writeFileSync(file + '.gz', zlib.gzipSync(content));
    }
    return '/assets/' + filename;
  }
  return html.replace(/<style>([\s\S]*?)<\/style>/g, (_, css) =>
    '<link rel="stylesheet" data-workbench-runtime href="' + write(css.replace(/url\((['"]?)assets\//g, 'url($1/assets/'), 'css') + '">')
    .replace(/<script>([\s\S]*?)<\/script>/g, (_, script) =>
      '<script data-workbench-runtime src="' + write(script, 'js') + '"></script>');
}

module.exports = { externalize };
