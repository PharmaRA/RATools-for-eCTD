import { spawn } from 'node:child_process'

export const startProcess = (command, args, options) => {
  const process = spawn(command, args, options)
  const ready = new Promise((resolve, reject) => {
    process.once('spawn', resolve)
    process.on('error', reject)
  })
  return { process, ready }
}
export const fetchDeadline = (url, options = {}, timeout = 10000) => fetch(url,
  { ...options, signal: options.signal ? AbortSignal.any([options.signal, AbortSignal.timeout(timeout)]) : AbortSignal.timeout(timeout) })
