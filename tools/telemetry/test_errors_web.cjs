const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
const fs=require('node:fs');
const folder=path.resolve(process.argv[3]);fs.mkdirSync(folder,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const context=await browser.newContext({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-Observatory-Access':'local-error-preview'}});
  await context.route('**/*',async route=>{const response=await route.fetch();await route.fulfill({response,headers:{...response.headers(),'content-security-policy':"default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"}});});
  const page=await context.newPage(),errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.goto(process.argv[2]+'/errors.html');
  const ready=()=>page.waitForFunction(()=>document.getElementById('connection').textContent==='已更新');
  await ready();assert.equal(await page.locator('[data-error]').count(),20);
  await page.getByRole('button',{name:'下一页'}).click();await ready();assert.equal(await page.locator('[data-error]').count(),4);
  console.log('PASS pagination');
  await page.locator('#build').selectOption('0.111.0');await ready();assert.equal(await page.locator('#total').textContent(),'1 类');
  await page.reload();await ready();assert.equal(await page.locator('#build').inputValue(),'0.111.0');
  console.log('PASS version filter and URL restore');
  await page.locator('[data-error]').click();await page.locator('#detail pre').waitFor();
  await page.locator('.error-mods summary').click();assert.ok((await page.locator('.error-mods').textContent()).includes('v3.2.1'));assert.ok((await page.locator('#detail').textContent()).includes('PC · Windows'));
  console.log('PASS platform, game build and other mod versions shown');
  assert.ok((await page.locator('#detail').textContent()).includes('<img src=x onerror='));
  assert.equal(await page.locator('#detail img').count(),0);assert.equal(await page.evaluate(()=>window.errorXss),undefined);
  console.log('PASS error details escape hostile markup');
  await page.screenshot({path:path.join(folder,'errors-desktop.png')});
  await page.setViewportSize({width:390,height:844});await page.locator('[data-error]').click();await page.locator('#detail pre').waitFor();
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.screenshot({path:path.join(folder,'errors-mobile.png')});console.log('PASS mobile layout');
  await page.locator('input[name="search"]').fill('no-such-local-error');await page.getByRole('button',{name:'查询',exact:true}).click();await ready();
  assert.equal(await page.locator('#errors').textContent(),'暂无报错');console.log('PASS empty result');
  await page.route('**/api/errors?*',route=>route.fulfill({status:503,body:'{}'}));
  await page.getByRole('button',{name:'刷新',exact:true}).click();await page.waitForFunction(()=>document.getElementById('connection').textContent==='更新失败');
  assert.ok((await page.locator('#error').textContent()).includes('503'));assert.equal(await page.locator('[data-error]').count(),0);
  console.log('PASS failed query clears stale results');
  assert.deepEqual(errors,[]);console.log('PASS no browser script errors under production CSP');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
