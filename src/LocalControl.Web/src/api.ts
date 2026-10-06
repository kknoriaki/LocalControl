let csrf = '';
export function setCsrf(value:string){csrf=value;}
export function csrfToken(){return csrf;}
export async function api<T>(path:string, method='GET', body?:unknown, signal?:AbortSignal):Promise<T>{
 const response=await fetch('/api/v1'+path,{method,credentials:'same-origin',cache:'no-store',signal,headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(method==='GET'?{}:{'X-LC-CSRF':csrf})},body:body===undefined?undefined:JSON.stringify(body)});
 if(!response.ok){
   if(response.status===401) window.dispatchEvent(new Event('lc-session-ended'));
   const data=await response.json().catch(()=>null);
   throw new Error(data?.message??({401:'Сессия завершена. Подключите телефон снова.',403:'Доступ не разрешён.',429:'Слишком много запросов. Подождите.'}[response.status]??'Не удалось связаться с ПК.'));
 }
 return response.status===204?undefined as T:response.json();
}
export function nonce(){const b=new Uint8Array(32);crypto.getRandomValues(b);return Array.from(b,x=>x.toString(16).padStart(2,'0')).join('');}
export function invitationFromFragment(){const value=new URLSearchParams(location.hash.slice(1)).get('invite');history.replaceState(null,'',location.pathname);return value&&/^[a-f0-9]{64}$/i.test(value)?value:null;}
