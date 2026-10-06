// Contract tests for the compiled production bundle. Fixtures live only here;
// the shipped application has no demo adapter or synthesized Windows values.
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const project=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const require=createRequire(path.join(project,'src/LocalControl.Web/package.json'));
const {chromium}=require('playwright');
const root=path.join(project,'src/LocalControl.Web/dist');
const server=http.createServer((req,res)=>{
 const p=path.resolve(root,'.'+decodeURIComponent(new URL(req.url,'http://localhost').pathname));
 if(!p.startsWith(root+path.sep)&&p!==root){res.writeHead(404).end();return;}
 const file=p===root?path.join(root,'index.html'):p;
 try{const data=fs.readFileSync(file);res.setHeader('Content-Type',({'.html':'text/html','.js':'application/javascript','.css':'text/css','.ttf':'font/ttf'})[path.extname(file)]??'application/octet-stream');res.end(data);}catch{res.writeHead(404).end();}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const url='http://127.0.0.1:'+server.address().port;
const browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH||undefined,headless:true,args:process.env.CHROMIUM_PATH?['--no-sandbox','--disable-dev-shm-usage','--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']:[]});
let count=0;
const session={csrf:'a'.repeat(64),permissions:['status.read','audio.control','apps.launch'],desktop:true,expiresAt:'9999-12-31T23:59:59Z'};
const state={revision:1,at:new Date().toISOString(),machine:{name:'QA-PC',cpuPercent:17,memoryTotalBytes:32*1024**3,memoryUsedBytes:10*1024**3,uptimeSeconds:4800,disks:[{label:'C:',totalBytes:1000*1024**3,freeBytes:600*1024**3}]},audio:[{id:'endpoint-1',label:'QA Speakers',kind:'output',volume:.7,muted:false,isDefault:true},{id:'mic-1',label:'QA Microphone',kind:'microphone',volume:.9,muted:false,isDefault:true},{id:'session-1',label:'QA App Audio',kind:'session',volume:.4,muted:false,isDefault:false}],warnings:[]};
async function fixture(page,who=session){
 const calls=[];
 await page.route('**/hubs/**',r=>r.fulfill({status:503,body:''}));
 await page.route('**/api/v1/**',async route=>{
  const request=route.request(),p=new URL(request.url()).pathname.slice('/api/v1'.length),method=request.method();
  if(method!=='GET')calls.push({path:p,method,body:request.postDataJSON(),csrf:request.headers()['x-lc-csrf']});
  const data=p==='/session'?who:p==='/state'?state:p==='/apps'?[{id:'trusted-1',label:'QA Registered App',source:'Steam'}]:p==='/desktop/network'?{url:null,addresses:['192.168.1.20'],port:41017}:p==='/desktop/devices'?[]:p==='/desktop/pairing/pending'?[]:p==='/desktop/pairing/open'?{url:url+'/#invite='+'f'.repeat(64),expiresAt:new Date(Date.now()+120000).toISOString()}:null;
  await route.fulfill({status:data?200:204,contentType:'application/json',body:data?JSON.stringify(data):undefined});
 });return calls;
}
function check(value,name){assert.ok(value,name);console.log('PASS '+name);count++;}
try{
 for(const viewport of [{width:1440,height:900},{width:393,height:852},{width:320,height:740}]){
  const page=await browser.newPage({viewport});const errors=[];page.on('pageerror',e=>errors.push(e.message));const calls=await fixture(page);
  await page.goto(url);await page.getByRole('heading',{name:'Обзор',exact:true}).waitFor();await page.getByText('QA-PC',{exact:true}).waitFor();
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'no horizontal overflow '+viewport.width);
  check(await page.getByText('17%',{exact:true}).count()===1,'snapshot CPU rendered '+viewport.width);
  await page.getByRole('button',{name:'Приложения',exact:true}).filter({visible:true}).click();
  await page.locator('.app-detail').filter({hasText:'QA Registered App'}).getByRole('button',{name:'Запустить',exact:true}).click();
  check(calls.some(x=>x.path==='/apps/trusted-1/launch'&&x.method==='POST'&&x.csrf===session.csrf),'launch uses registered id and CSRF '+viewport.width);
  await page.getByRole('button',{name:'Аудио',exact:true}).filter({visible:true}).click();
  const slider=page.getByRole('slider',{name:'Громкость QA Speakers'});await slider.focus();await slider.press('Home');await page.waitForTimeout(80);
  check(calls.some(x=>x.path==='/audio/endpoint-1'&&x.body.volume===0),'keyboard slider sends numeric volume '+viewport.width);
  await page.getByRole('button',{name:'Выключить QA Microphone',exact:true}).click();
  check(calls.some(x=>x.path==='/audio/mic-1'&&x.body.muted===true),'microphone mute calls Windows endpoint '+viewport.width);
  await page.getByRole('button',{name:'Сменить тему',exact:true}).click();
  check(await page.evaluate(()=>document.documentElement.dataset.theme)==='light','light theme '+viewport.width);
  check(errors.length===0,'no uncaught UI errors '+viewport.width);
  await page.close();
 }
 const power=await browser.newPage({viewport:{width:1440,height:900}});const powerCalls=await fixture(power);
 await power.route('**/api/v1/power/intents',async r=>{powerCalls.push({path:'/power/intents',method:r.request().method(),body:r.request().postDataJSON()});await r.fulfill({json:{id:'intent-qa'}});});
 await power.goto(url);await power.getByRole('heading',{name:'Обзор',exact:true}).waitFor();
 await power.getByRole('button',{name:'Выключить',exact:true}).click();await power.getByRole('dialog',{name:'Подтверждение питания'}).waitFor();
 check(powerCalls.some(x=>x.path==='/power/intents'&&x.body.action==='shutdown')&&!powerCalls.some(x=>x.path==='/power/confirm'),'power first click creates intent without shutting down');
 await power.getByRole('button',{name:'Отмена',exact:true}).click();check(!powerCalls.some(x=>x.path==='/power/confirm'),'cancelling power does not submit confirmation');
 await power.getByRole('button',{name:'Выключить',exact:true}).click();await power.getByRole('button',{name:'Подтвердить',exact:true}).click();
 check(powerCalls.some(x=>x.path==='/power/confirm'&&x.body.id==='intent-qa'&&x.csrf===session.csrf),'explicit power confirmation sends nonce and CSRF');await power.close();
 const restricted=await browser.newPage({viewport:{width:393,height:852}});const restrictedCalls=await fixture(restricted,{...session,desktop:false,permissions:['status.read']});
 await restricted.goto(url);await restricted.getByRole('heading',{name:'Обзор',exact:true}).waitFor();check(await restricted.getByRole('button',{name:'Выключить',exact:true}).isDisabled(),'status-only phone cannot request power');
 await restricted.getByRole('button',{name:'Экран',exact:true}).filter({visible:true}).click();check(await restricted.getByRole('button',{name:'Обновить снимок',exact:true}).isDisabled(),'status-only phone cannot capture remote screen');
 check(await restricted.getByRole('button',{name:'Enter',exact:true}).isDisabled(),'remote keyboard requires separate input grant');
 await restricted.getByRole('button',{name:'Ещё',exact:true}).click();await restricted.getByRole('button',{name:'Буфер обмена',exact:true}).click();
 check(await restricted.getByRole('button',{name:'Прочитать с ПК',exact:true}).isDisabled()&&await restricted.getByRole('button',{name:'Записать на ПК',exact:true}).isDisabled(),'clipboard read and write are disabled without grants');
 check(!restrictedCalls.some(x=>x.path.startsWith('/clipboard/')),'opening clipboard does not automatically read private text');await restricted.close();
 const offline=await browser.newPage({viewport:{width:1440,height:900}});await fixture(offline);let reads=0;
 await offline.route('**/api/v1/state',r=>++reads===1?r.fulfill({json:state}):r.fulfill({status:503,json:{message:'Связь прервана'}}));
 await offline.goto(url);await offline.getByText('QA-PC',{exact:true}).waitFor();await offline.getByText('Нет связи',{exact:true}).waitFor();
 check(!(await offline.locator('.status-dot').getAttribute('class')).includes('online'),'lost connection removes online status instead of presenting cached state as current');await offline.close();
 const limited=await browser.newPage({viewport:{width:393,height:852}});await fixture(limited,{...session,desktop:false,permissions:['status.read']});await limited.goto(url);await limited.getByRole('heading',{name:'Обзор',exact:true}).waitFor();
 check(await limited.getByRole('button',{name:'Подключение',exact:true}).count()===0,'phone has no desktop admin navigation');
 await limited.getByRole('button',{name:'Аудио',exact:true}).click();
 check(await limited.getByRole('slider',{name:'Громкость QA Speakers'}).isDisabled(),'status-only phone cannot edit audio');await limited.close();
 const pair=await browser.newPage({viewport:{width:393,height:852}});let pairRequest=null;
 await pair.route('**/api/v1/**',async r=>{const req=r.request(),p=new URL(req.url()).pathname;if(p.endsWith('/session')){await r.fulfill({status:401});return;}if(p.endsWith('/requests')){pairRequest=req.postDataJSON();await r.fulfill({json:{id:'pending-1',secret:'b'.repeat(64),code:'314159',expiresAt:new Date(Date.now()+120000).toISOString()}});return;}await r.fulfill({json:{status:'pending'}});});
 await pair.goto(url+'/#invite='+'f'.repeat(64));await pair.getByRole('button',{name:'Запросить подключение'}).click();await pair.getByText('314159',{exact:true}).waitFor();
 check(pairRequest?.invite==='f'.repeat(64)&&/^[a-f0-9]{64}$/.test(pairRequest.nonce),'pairing sends invite and random client nonce');
 check(new URL(pair.url()).hash==='','pairing removes fragment from browser history');
 check(await pair.getByText('Ожидание подтверждения',{exact:false}).count()===1,'pairing waits for real desktop approval');await pair.close();
 console.log(count+' production UI contract checks passed.');
}finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
