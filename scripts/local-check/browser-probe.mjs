import { browserSession } from './browser-session.mjs'
import { writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
const evidence=resolve(process.argv[2]), result={exitCode:1,scope:'Windows headless Chromium under owned-directory deny-delete ACL; native downloads disabled'}
let browser
try {
  browser=await browserSession(evidence)
  await browser.send('Page.navigate',{url:'data:text/html,<title>Retained browser probe</title><button id="probe">Ready</button>'})
  result.version=await browser.send('Browser.getVersion')
  result.title=await browser.evaluate('document.title')
  if(result.title!=='Retained browser probe')throw Error('Page did not render')
  result.exitCode=0
}catch(error){result.error=String(error);process.exitCode=1}
finally{try{await browser?.stop()}catch(error){result.cleanupError=String(error);result.exitCode=1;process.exitCode=1}finally{result.browserMainStopped=browser?.isStopped()??true;await writeFile(resolve(evidence,'browser-result.json'),JSON.stringify(result,null,2))}}
console.log(JSON.stringify(result))
