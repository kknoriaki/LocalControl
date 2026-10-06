// UI state/DOM checks without a rendering browser.
// npm install --no-save linkedom; node scripts/check-model.cjs (from repo root).
const {parseHTML}=require('linkedom');
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(__dirname+'/../prototype/index.html','utf8');
const {window,document}=parseHTML(html);
const sandbox={window,document,URLSearchParams,location:{search:''},matchMedia:()=>({matches:false,addEventListener(){}}),localStorage:{getItem(){return null},setItem(){}},setTimeout,clearTimeout,setInterval,clearInterval,console};
window.scrollTo=()=>{};const context=vm.createContext(sandbox);vm.runInContext(html.split('<script>')[1].split('</script>')[0],context);
const run=code=>vm.runInContext(code,context);
const click=selector=>{const el=document.querySelector(selector);assert(el,selector);el.dispatchEvent(new window.Event('click',{bubbles:true}));};
const input=(selector,value)=>{const el=document.querySelector(selector);el.value=value;el.dispatchEvent(new window.Event('input',{bubbles:true}));};
let assertions=0;function check(condition,message){assert(condition,message);assertions++;}
check(document.querySelector('#content').textContent.includes('Ilya’s PC'),'home renders');
click('.sidebar [data-id="audio"]');input('[data-volume="discord"]','23');check(run('state.apps[0].volume')===23,'audio value');check(document.querySelector('[data-volume-label="discord"]').textContent==='23%','audio DOM');
click('[data-action="mute"][data-id="discord"]');check(run('state.apps[0].muted')===true,'mute');
click('[data-action="mic"]');check(run('state.mic')===true,'Windows mic');
click('.sidebar [data-id="apps"]');input('#app-search','Counter');check(document.querySelectorAll('#app-list .app-row').length===1,'search');input('#app-search','no-match');check(document.querySelector('#app-list').textContent.includes('не найдены'),'empty search');
click('[data-action="add-app"]');document.querySelector('#custom-name').value='<img src=x onerror=alert(1)>';click('[data-action="custom-save"]');input('#app-search','<img');check(document.querySelectorAll('#app-list img').length===0,'escaped custom label');check(document.querySelector('#app-list').textContent.includes('<img'),'label is text');
click('.sidebar [data-id="settings"]');click('[data-action="theme"][data-id="light"]');check(document.documentElement.dataset.theme==='light','light theme');
click('[data-action="pair"]');click('[data-action="pair-request"]');document.querySelector('#perm-audio').checked=true;document.querySelector('#perm-apps').checked=true;click('[data-action="pair-approve"]');check(run('state.devices.length')===2,'pair approval');click('.sidebar [data-id="devices"]');click('[data-action="revoke"]');check(run('state.devices.length')===1,'revoke');
click('.sidebar [data-id="home"]');click('[data-action="power"][data-id="shutdown"]');check(!!document.querySelector('[role="dialog"]'),'power confirmation');click('[data-action="close-modal"]');check(!document.querySelector('[role="dialog"]'),'confirmation cancel');
click('.sidebar [data-id="remote"]');click('[data-action="remote-toggle"]');document.querySelector('#remote-input').value='hello';click('[data-action="send-keys"]');check(document.querySelector('#remote-input').value==='hello','remote permission stops input');
click('.sidebar [data-id="transfers"]');document.querySelector('#clipboard').value='https://example.com';click('[data-action="clipboard"]');click('[data-action="clipboard-read"]');check(document.querySelector('#clipboard').value.includes('симуляция'),'explicit clipboard');
for(const screen of ['home','apps','audio','remote','transfers','startup','hardware','devices','settings','more']){run('navigate('+JSON.stringify(screen)+')');check(document.querySelector('#content').textContent.length>100,'screen '+screen);}
check(!/https?:\/\/(?:fonts|cdn)/.test(html),'no remote asset CDN');
console.log('PASS: '+assertions+' UI state/DOM checks. No browser layout, Windows or Safari claims.');
clearTimeout(run('toastTimer'));for(const f of run('state.queue'))clearInterval(f.timer);
