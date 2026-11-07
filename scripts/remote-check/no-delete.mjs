import fs from 'node:fs'
import promises from 'node:fs/promises'
import { syncBuiltinESMExports } from 'node:module'
const refuse = () => { throw new Error('Filesystem deletion is prohibited for this remote check.') }
for (const name of ['rm', 'rmSync', 'unlink', 'unlinkSync', 'rmdir', 'rmdirSync']) fs[name] = refuse
for (const name of ['rm', 'unlink', 'rmdir']) promises[name] = refuse
syncBuiltinESMExports()
