// Run: npm install --no-save playwright && npx playwright install chromium
// Start: python -m http.server 8765 from the directory containing LocalControl.
// Then: node LocalControl/scripts/check-prototype.cjs
const {chromium} = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
(async()=>{
 const browser = await chromium.launch({headless:true});
 const page = await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
 const errors=[]; page.on('pageerror',e=>errors.push(e.message));
 const root='http://127.0.0.1:8765/LocalControl/prototype/index.html';
 const out=path.resolve('LocalControl/design');fs.mkdirSync(out,{recursive:true});
 const shots=[['desktop-dashboard','home',1440,900,'dark'],['mobile-home','home',393,852,'dark'],['mobile-audio','audio',393,852,'dark'],['mobile-applications','apps',393,852,'dark'],['mobile-remote','remote',393,852,'dark'],['desktop-applications','apps',1440,900,'dark'],['desktop-audio','audio',1440,900,'dark'],['desktop-settings','settings',1440,900,'light']];
 for(const [name,screen,width,height,theme] of shots){
  await page.setViewportSize({width,height}); await page.goto(root+'?screen='+screen+'&theme='+theme);await page.evaluate(()=>document.fonts.ready);
  await page.screenshot({path:path.join(out,name+'.png'),fullPage:false});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,name+' horizontal overflow');
  assert(await page.evaluate(()=>document.fonts.check('14px Manrope')&&document.fonts.check('20px "Space Grotesk"')),name+' fonts');
 }
 await page.setViewportSize({width:1440,height:900}); await page.goto(root+'?screen=audio&theme=dark');
 await page.locator('[data-volume="discord"]').fill('23');
 assert.equal(await page.locator('[data-volume-label="discord"]').textContent(),'23%');
 await page.locator('[data-action="mute"][data-id="discord"]').click();assert.equal(await page.locator('[data-action="mute"][data-id="discord"]').getAttribute('aria-pressed'),'true');
 await page.locator('[data-action="mic"]').click(); assert(await page.locator('.mic-button').textContent().then(t=>t.includes('выключен')));
 await page.locator('.sidebar [data-id="apps"]').click(); await page.locator('#app-search').fill('Counter');assert.equal(await page.locator('#app-list .app-row').count(),1);
 await page.locator('#app-search').fill('невозможное');assert(await page.locator('#app-list').textContent().then(t=>t.includes('не найдены')));
 await page.locator('[data-action="add-app"]').click();await page.locator('#custom-name').fill('<img src=x onerror=alert(1)>');await page.locator('[data-action="custom-save"]').click();await page.locator('#app-search').fill('<img');assert.equal(await page.locator('#app-list img').count(),0);assert(await page.locator('#app-list').textContent().then(t=>t.includes('<img')));
 await page.locator('.sidebar [data-id="settings"]').click();await page.locator('[data-action="theme"][data-id="light"]').click();assert.equal(await page.locator('html').getAttribute('data-theme'),'light');
 await page.locator('[data-action="pair"]').click();await page.locator('[data-action="pair-request"]').click();await page.locator('[data-action="pair-approve"]').click();await page.locator('.sidebar [data-id="devices"]').click();assert.equal(await page.locator('[data-action="revoke"]').count(),2);await page.locator('[data-action="revoke"]').last().click();assert.equal(await page.locator('[data-action="revoke"]').count(),1);
 await page.locator('.sidebar [data-id="home"]').click();await page.locator('[data-action="power"][data-id="shutdown"]').click();assert.equal(await page.locator('[role="dialog"]').count(),1);await page.keyboard.press('Escape');assert.equal(await page.locator('[role="dialog"]').count(),0);
 await page.locator('.sidebar [data-id="remote"]').click();await page.locator('[data-action="remote-toggle"]').click();await page.locator('#remote-input').fill('hello');await page.locator('[data-action="send-keys"]').click();assert.equal(await page.locator('#remote-input').inputValue(),'hello');
 await page.locator('.sidebar [data-id="transfers"]').click();await page.locator('#file-input').setInputFiles({name:'demo.txt',mimeType:'text/plain',buffer:Buffer.from('private demo')});assert.equal(await page.locator('#transfer-list .app-row').count(),1);await page.locator('[data-action="cancel-file"]').click();assert(await page.locator('#transfer-list').textContent().then(t=>t.includes('Отменено')));await page.locator('[data-action="retry-file"]').click();await page.locator('#transfer-list').filter({hasText:'Передано'}).waitFor({timeout:7000});
 for(const width of [320,393,720,768,1080,1440]){await page.setViewportSize({width,height:900});for(const screen of ['home','apps','audio','remote','settings','transfers','startup','hardware','devices','more']){await page.goto(root+'?screen='+screen);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,screen+' @ '+width);}}
 assert.deepEqual(errors,[],'browser runtime errors');
 fs.writeFileSync(path.join(out,'qa-result.json'),JSON.stringify({status:'passed',screenshots:shots.length,responsiveChecks:60,checked:['fonts','sliders','mute/mic','search','custom-name escaping','themes','pairing/revoke','power confirmation','remote permission','transfer cancel/retry'],browser:'Chromium headless',limitations:['Windows integrations not implemented','Physical Safari/WebView2 not tested']},null,2));
 console.log('PASS: 8 screenshots, 60 responsive checks, interactive smoke, no runtime errors');await browser.close();
})().catch(error=>{console.error(error);process.exit(1)});
