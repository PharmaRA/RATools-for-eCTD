import '../remote-check/no-delete.mjs'
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { randomBytes, createHash } from 'node:crypto'
import { readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { browserSession, until, delay } from './browser-session.mjs'

const root=fileURLToPath(new URL('../../',import.meta.url)), evidence=resolve(process.argv[2]), backend=resolve(evidence,'backend')
const workspace=resolve(evidence,'input-workspace'), fixture=JSON.parse(await readFile(resolve(evidence,'browser-fixture.json'),'utf8'))
const sdk=resolve(root,'.artifacts/tools/dotnet-8.0.423'), token=randomBytes(32).toString('hex')
const env={...process.env,DOTNET_ROOT:sdk,DOTNET_NOLOGO:'1',DOTNET_CLI_TELEMETRY_OPTOUT:'1',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:'http://127.0.0.1:0',
  ASPNETCORE_HOSTINGSTARTUPASSEMBLIES:'InputApiStartup',Persistence__Provider:'InMemory',Deployment__Mode:'LocalOnly',Security__ApiKey:token,
  Security__AllowedWorkspaceRoots__0:workspace,FileSecrets__ApiKeyPath:'',FileSecrets__PostgreSqlPasswordPath:'',FileStorage__RootPath:resolve(workspace,'uploads'),
  BackboneOutput__RootPath:resolve(workspace,'publish'),BackboneOutput__RetainJobRuns:'0',PackageValidation__StorageRoot:resolve(workspace,'validation'),
  ASPNETCORE_TEMP:resolve(workspace,'framework-temp-must-not-exist')}
const result={exitCode:1,scope:'Windows actual Chromium/current API/InMemory external ZIP UI; Blob bytes captured, native download manager disabled',passed:[],facts:{},unrun:['native download completion','PostgreSQL restart','Formal/external qualification']}
let browser,api,apiLog='',base
const stopApi=async()=>{if(api&&api.exitCode===null&&api.signalCode===null){api.kill();await until(()=>api.exitCode!==null||api.signalCode!==null,'API stopped',10000)}}
const request=async path=>{const response=await fetch(base+path,{headers:{'X-RA-Tools-Api-Key':token},signal:AbortSignal.timeout(10000)});assert.equal(response.status,200,path);return response.json()}
const startApi=async(disableWorker=false)=>{
  let startupLog=''
  api=spawn(resolve(sdk,'dotnet.exe'),[resolve(backend,'RATools.Api.dll'),'--contentRoot',backend,'--webroot',resolve(evidence,'webroot')],{cwd:backend,env:{...env,PackageValidation__Enabled:disableWorker?'false':'true'},windowsHide:true,stdio:['ignore','pipe','pipe']})
  api.stdout.on('data',data=>{apiLog+=data;startupLog+=data});api.stderr.on('data',data=>{apiLog+=data;startupLog+=data})
  await new Promise((resolve,reject)=>{api.once('spawn',resolve);api.once('error',reject)})
  await until(()=>{if(api.exitCode!==null)throw Error('API startup failed');base=/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/.exec(startupLog)?.[1];return base},'API address')
}
try{
  await startApi()
  browser=await browserSession(evidence)
  const {send,evaluate}=browser
  result.facts.browser=await send('Browser.getVersion')
  const wait=expression=>until(()=>evaluate(expression),expression)
  const mouseClick=async expression=>{const box=await evaluate(`(()=>{const e=${expression};e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`);await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...box});await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...box})}
  const click=async label=>{await wait(`[...document.querySelectorAll('button')].some(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.disabled)`);await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(label)}&&!e.disabled).click()`)}
  const choose=async(selector,label)=>{
    const input=`(()=>{const e=document.querySelector(${JSON.stringify(selector)});return e.matches('input')?e:e.querySelector('input[role="combobox"]')})()`
    await mouseClick(input)
    const dropdown=`document.getElementById((${input}).getAttribute('aria-controls'))?.closest('.ant-select-dropdown')`
    await wait(`(${input}).getAttribute('aria-expanded')==='true'&&[...((${dropdown})?.querySelectorAll('.ant-select-item-option')||[])].some(e=>e.textContent.trim()===${JSON.stringify(label)})`)
    await evaluate(`[...(${dropdown}).querySelectorAll('.ant-select-item-option')].find(e=>e.textContent.trim()===${JSON.stringify(label)}).click()`)
    await wait(`document.querySelector(${JSON.stringify(selector)}).closest('.ant-select').textContent.includes(${JSON.stringify(label)})`)
  }
  await send('Page.addScriptToEvaluateOnNewDocument',{source:`window.__qaDownloads=[];const old=URL.createObjectURL;URL.createObjectURL=function(blob){window.__qaDownloads.push(blob);return old.call(this,blob)};`})
  await send('Page.navigate',{url:base+'/package-validations'})
  await wait(`!!document.querySelector('#validation-application')`)
  const application=await evaluate(`document.querySelector('#validation-application').value`)
  result.facts.applicationId=application
  const register=async(role,sequence)=>{
    await click(role)
    await wait(`!!document.querySelector('#validation-zip')`)
    const document=await send('DOM.getDocument')
    const node=await send('DOM.querySelector',{nodeId:document.root.nodeId,selector:'#validation-zip'})
    await send('DOM.setFileInputFiles',{nodeId:node.nodeId,files:[fixture.archive]})
    await click('登记并选择')
    await wait(`!!document.querySelector('#validation-root')`)
    await choose('#validation-root',sequence+' / bundle/'+sequence)
    await click('登记并选择')
    await wait(`!document.querySelector('#validation-zip')`)
  }
  await register('选择目标输入','0001')
  result.passed.push('real browser uploads external ZIP, receives root ambiguity and selects target0001')
  await register('添加历史序列','0000')
  const profile='ich-3.2.2-development-v1'
  await choose('#validation-profile',profile)
  await choose('[aria-label="历史 0000 规则范围"]',profile)
  await click('开始独立校验')
  await wait(`document.body.innerText.includes('运行完成（不等于通过）')&&document.body.innerText.includes('共 ')`)
  const runId=await evaluate(`new URL(location.href).searchParams.get('run')`)
  const run=await request('/api/package-validations/'+runId), binding=await request('/api/package-validations/'+runId+'/evidence')
  assert.equal(run.status,'Completed');assert.equal(binding.history.length,1);assert.equal(binding.historyBaseline.entries[0].trustStatus,'Unverified')
  assert.equal(binding.history[0].profileSnapshotId,profile);assert.equal(run.binding.profileSnapshotId,profile)
  assert.deepEqual(await request('/api/applications'),[])
  result.facts.run=run;result.facts.history=binding.history
  result.passed.push('UI selects explicit history/profile and completes actual worker without creating a business application')
  const first=await request('/api/package-validations/'+runId+'/findings?offset=0&limit=20')
  assert.ok(first.totalCount>20,'Fixture must exercise actual findings pagination')
  const verifyPage=async(page,data)=>{
    const expected=data.items.map(item=>({code:item.code,path:item.location.logicalPath||item.location.nodePath||'规则级问题'}))
    await wait(`(()=>{const table=[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位'));if(!table||table.querySelector('.ant-spin-spinning')||table.querySelector('.ant-pagination-item-active')?.textContent!==${JSON.stringify(String(page))})return false;const rows=[...table.querySelectorAll('tbody tr.ant-table-row')];const expected=${JSON.stringify(expected)};return rows.length===expected.length&&expected.every((item,index)=>rows[index].innerText.includes(item.code)&&rows[index].innerText.includes(item.path))})()`)
  }
  await verifyPage(1,first)
  await evaluate(`[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位')).querySelector('.ant-pagination-next button').click()`)
  const second=await request('/api/package-validations/'+runId+'/findings?offset=20&limit=20')
  await verifyPage(2,second)
  result.facts.findings=first.totalCount
  result.passed.push('findings UI traverses a real second page of archived findings')
  // Select a known delivered text file by its actual finding row, across pages.
  let found=false
  for(let page=1;page<=Math.ceil(first.totalCount/20);page++){
    await evaluate(`{const table=[...document.querySelectorAll('.ant-table-wrapper')].find(e=>e.innerText.includes('查看定位'));table.querySelector('.ant-pagination-item-${page}')?.click()}`)
    const pageData=page===1?first:page===2?second:await request('/api/package-validations/'+runId+`/findings?offset=${(page-1)*20}&limit=20`)
    await verifyPage(page,pageData)
    found=await evaluate(`[...document.querySelectorAll('tr')].some(e=>e.innerText.includes(${JSON.stringify(fixture.sourcePath)})&&e.innerText.includes('查看定位'))`)
    if(found)break
  }
  assert.ok(found,'Known source finding must be visible')
  await evaluate(`[...document.querySelectorAll('tr')].find(e=>e.innerText.includes(${JSON.stringify(fixture.sourcePath)})&&e.innerText.includes('查看定位')).querySelector('button').click()`)
  await wait(`document.querySelector('[data-testid="validation-source-text"]')?.textContent.includes('Independent browser fixture 0')`)
  await click('下载定位文件原始字节')
  await wait(`window.__qaDownloads.length===1`)
  assert.equal(await evaluate(`window.__qaDownloads[0].text()`),fixture.sourceText)
  await evaluate(`document.querySelector('.ant-drawer-close').click()`)
  result.passed.push('source finding opens exact delivered text and download action returns original Blob bytes')
  await click('下载完整报告 JSON')
  await wait(`window.__qaDownloads.length===2`)
  const reportText=await evaluate(`window.__qaDownloads[1].text()`),reportBytes=Buffer.from(reportText)
  const raw=await fetch(base+'/api/package-validations/'+runId+'/report',{headers:{'X-RA-Tools-Api-Key':token}})
  assert.deepEqual(reportBytes,Buffer.from(await raw.arrayBuffer()))
  assert.equal(JSON.parse(reportText).isReadyForFinalization,false)
  await writeFile(resolve(evidence,'browser-report.json'),reportBytes)
  result.facts.report={bytes:reportBytes.length,sha256:createHash('sha256').update(reportBytes).digest('hex')}
  result.passed.push('report download UI yields exact archived JSON Blob; Development remains not ready')
  await stopApi()
  await startApi(true)
  await send('Page.navigate',{url:base+'/package-validations'})
  await wait(`!!document.querySelector('#validation-application')`)
  await register('选择目标输入','0001')
  await choose('#validation-profile',profile)
  await click('开始独立校验')
  await wait(`[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='请求取消')`)
  const cancelId=await evaluate(`new URL(location.href).searchParams.get('run')`)
  const beforeCancel=await request('/api/package-validations/'+cancelId)
  assert.equal(beforeCancel.status,'Pending');assert.equal(beforeCancel.attemptCount,0)
  await click('请求取消')
  await wait(`document.body.innerText.includes('已取消')&&![...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='请求取消')`)
  const cancelled=await request('/api/package-validations/'+cancelId)
  assert.equal(cancelled.status,'Cancelled');assert.equal(cancelled.reportAvailable,false);assert.equal(cancelled.attemptCount,0)
  assert.equal(await evaluate(`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='下载完整报告 JSON').disabled`),true)
  result.facts.cancelled=cancelled
  result.passed.push('UI cancels Pending task in independent worker-disabled API; report remains unavailable')
  const errors=browser.events.filter(event=>event.method==='Runtime.exceptionThrown').map(event=>event.params.exceptionDetails.text)
  assert.deepEqual(errors,[])
  result.exitCode=0
}catch(error){result.error=String(error).replaceAll(token,'[task-credential-redacted]');process.exitCode=1
  try{if(browser)await writeFile(resolve(evidence,'browser-page.txt'),await browser.evaluate('document.body.innerText'))}catch{}
}finally{
  const cleanup=[]
  try{await browser?.stop()}catch(error){cleanup.push('browser: '+String(error))}
  try{await stopApi()}catch(error){cleanup.push('api: '+String(error))}
  result.browserMainStopped=browser?.isStopped()??true;result.apiStopped=!api||api.exitCode!==null||api.signalCode!==null
  if(cleanup.length){result.cleanupErrors=cleanup;result.exitCode=1;process.exitCode=1}
  try{await writeFile(resolve(evidence,'browser-api.log'),apiLog.replaceAll(token,'[task-credential-redacted]'))}
  finally{await writeFile(resolve(evidence,'browser-result.json'),JSON.stringify(result,null,2))}
}
console.log(JSON.stringify({exitCode:result.exitCode,passed:result.passed,browserMainStopped:result.browserMainStopped,apiStopped:result.apiStopped,error:result.error}))
