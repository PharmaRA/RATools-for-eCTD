import '../remote-check/no-delete.mjs'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFile, writeFile } from 'node:fs/promises'
import { resolve, relative, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { build } from '../../frontend/node_modules/vite/dist/node/index.js'
import ts from '../../frontend/node_modules/typescript/lib/typescript.js'

const root=fileURLToPath(new URL('../../',import.meta.url)), frontend=resolve(root,'frontend'), evidence=resolve(process.argv[2])
assert.ok(evidence.startsWith(resolve(root,'.artifacts/local-check')+sep))
const hashes={}
const fingerprint=async path=>{hashes[relative(root,path).replaceAll('\\','/')]=createHash('sha256').update(await readFile(path)).digest('hex')}
for(const file of ['package.json','package-lock.json','vite.config.ts','postcss.config.js','tailwind.config.js','index.html','bundle-budget.json'])await fingerprint(resolve(frontend,file))
const configSource=await readFile(resolve(frontend,'vite.config.ts'),'utf8')
const javascript=ts.transpileModule(configSource,{compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText
  .replace("from 'vite'",`from '${pathToFileURL(resolve(frontend,'node_modules/vite/dist/node/index.js')).href}'`)
  .replace("from '@vitejs/plugin-react'",`from '${pathToFileURL(resolve(frontend,'node_modules/@vitejs/plugin-react/dist/index.js')).href}'`)
const configFile=resolve(evidence,'vite-config.mjs');await writeFile(configFile,javascript)
const config=(await import(pathToFileURL(configFile).href)).default
process.chdir(frontend)
await build({...config,root:frontend,configFile:false,envFile:false,build:{...config.build,outDir:resolve(evidence,'webroot'),emptyOutDir:false},
  plugins:[...config.plugins,{name:'qa-browser-input-manifest',async transform(_code,id){if(id.includes('\0'))return;const path=resolve(id.split('?')[0]);if(path.startsWith(frontend+sep)&&!path.includes(`${sep}node_modules${sep}`))await fingerprint(path)}}]})
assert.ok(Object.keys(hashes).some(path=>path==='frontend/src/pages/PackageValidationPage.tsx'),'Frontend source manifest is incomplete')
await writeFile(resolve(evidence,'frontend-inputs.json'),JSON.stringify(hashes,null,2))
