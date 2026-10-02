// Run against an isolated local API and frontend. See reference/publisher/NODE-EDITOR.md.
import assert from 'node:assert/strict'
import { randomInt } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const { chromium } = await import(process.env.RATOOLS_PLAYWRIGHT_MODULE || 'playwright')
const origin = process.env.RATOOLS_BROWSER_ORIGIN || 'http://127.0.0.1:3000'
const api = process.env.RATOOLS_BROWSER_API || 'http://127.0.0.1:5000'
const workspace = process.env.RATOOLS_BROWSER_WORKSPACE_ROOT
assert.ok(workspace, 'Set RATOOLS_BROWSER_WORKSPACE_ROOT to an allowed disposable workspace parent.')
for (const value of [origin, api]) assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(new URL(value).hostname), 'Use an isolated local server.')
const output = resolve(process.env.RATOOLS_BROWSER_OUTPUT || '.artifacts/node-editor-browser')
await mkdir(output, { recursive: true })
const browser = await chromium.launch({ headless: true })
const page = await browser.newPage({ viewport: { width: 1600, height: 1100 }, extraHTTPHeaders:
  process.env.RATOOLS_BROWSER_API_KEY ? { 'X-RA-Tools-Api-Key': process.env.RATOOLS_BROWSER_API_KEY } : {} })
const errors = []
page.on('pageerror', error => errors.push(error.message))

async function json(response) {
  assert.ok(response.ok(), `${response.status()}: ${await response.text()}`)
  return response.json()
}
async function choose(selector) {
  const item = typeof selector === 'string' ? page.locator(`[data-tree-key="${selector}"]`)
    : page.locator('.ectd-tree-node[role="treeitem"]').filter({ hasText: selector })
  const holder = page.locator('.ant-tree-list-holder')
  await holder.evaluate(element => { element.scrollTop = 0 })
  await page.waitForTimeout(100)
  for (let index = 0; index < 40; index++) {
    if (await item.count()) { await item.first().click(); return }
    await holder.evaluate(element => { element.scrollTop += 220 })
    await page.waitForTimeout(75)
  }
  throw new Error(`Missing tree item: ${selector}`)
}
async function create(label, fields = {}) {
  await choose(new RegExp(`＋ .*${label}`))
  for (const [name, value] of Object.entries(fields)) await page.getByLabel(name, { exact: true }).fill(value)
  const [response] = await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/nodes') && response.request().method() === 'POST'),
    page.getByRole('button', { name: '创建实例', exact: true }).click(),
  ])
  await json(response)
  await page.getByRole('button', { name: '保存节点', exact: true }).waitFor()
  await page.waitForTimeout(250)
}
async function upload(sequence, manufacturer) {
  const path = resolve(`tests/RATools.Tests/Fixtures/Publisher/sequences/${sequence}/m3/32-body-data/32s-drug-sub/drug-a-${manufacturer}/32s4-contr-drug-sub/32s41-spec/specification.pdf`)
  const [response] = await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/api/document-placements') && response.request().method() === 'POST'),
    page.getByTestId('section-file-input').setInputFiles(path),
  ])
  await json(response)
  await page.waitForTimeout(400)
}

try {
  // Only application/sequence scaffolding uses direct writes. Every node, file and
  // lifecycle edit below is performed through the visible operator interface.
  const app = await json(await page.request.post(`${api}/api/applications`, { data: {
    applicationNumber: String(randomInt(900000, 999999)), sponsorName: 'Synthetic node editor verification',
    ectdTemplateKey: 'us-fda-ectd-3.2.2', workingDirectoryParentPath: workspace,
  } }))
  const base = `${api}/api/applications/${app.id}/sequences`
  for (const sequenceNumber of ['0000', '0001']) await json(await page.request.post(base, { data: {
    sequenceNumber, submissionType: 'original-application', description: 'Synthetic browser verification',
  } }))
  const snapshot = async sequence => json(await page.request.get(`${base}/${sequence}/workspace`))
  const navigate = async sequence => {
    await page.goto(`${origin}/applications/${app.id}/sequences/${sequence}/workspace`)
    await page.getByRole('button', { name: '刷新工作区' }).waitFor()
    await page.waitForTimeout(500)
  }
  await navigate('0000')
  await create('Module 3 Quality')
  await create('Body Of Data')
  await create('Drug Substance', { '原料药（业务身份）': 'Drug A', '生产商（业务身份）': 'Alpha', '目录标识': 'drug-a-alpha' })
  await create('Control Of Drug Substance')
  await create('Specification')
  await upload('0000', 'alpha')
  await choose(/Alpha.*Drug A/)
  await page.getByRole('button', { name: '复制结构', exact: true }).click()
  await page.getByLabel('生产商（业务身份）', { exact: true }).fill('Beta')
  await page.getByLabel('目录标识', { exact: true }).fill('drug-a-beta')
  await page.getByRole('button', { name: '预览复制', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.waitFor()
  assert.match(await dialog.innerText(), /drug-a-beta/)
  assert.equal((await snapshot('0000')).nodeTree.nodes.length, 5, 'Preview must not write nodes.')
  await page.getByRole('button', { name: '创建新业务分组', exact: true }).click()
  await dialog.waitFor({ state: 'hidden' })
  await page.waitForTimeout(400)
  const nodes = (await snapshot('0000')).nodeTree.nodes
  const spec = manufacturer => {
    const group = nodes.find(node => node.attributes.manufacturer === manufacturer)
    const control = nodes.find(node => node.parentInstanceId === group.nodeInstanceId)
    return nodes.find(node => node.parentInstanceId === control.nodeInstanceId)
  }
  await choose(`node:${spec('Beta').nodeInstanceId}`)
  await upload('0000', 'beta')
  const initial = await snapshot('0000')
  assert.equal(initial.placements.length, 2)
  assert.notEqual(initial.documents[0].storagePath, initial.documents[1].storagePath)
  for (const manufacturer of ['Alpha', 'Beta']) {
    const leaf = initial.placements.find(leaf => leaf.nodeInstanceId === spec(manufacturer).nodeInstanceId)
    const document = initial.documents.find(document => document.id === leaf.documentId)
    assert.equal(document.fileName, 'specification.pdf')
    const fixture = `tests/RATools.Tests/Fixtures/Publisher/sequences/0000/m3/32-body-data/32s-drug-sub/drug-a-${manufacturer.toLowerCase()}/32s4-contr-drug-sub/32s41-spec/specification.pdf`
    assert.deepEqual(await readFile(document.storagePath), await readFile(fixture))
  }
  await page.screenshot({ path: resolve(output, 'two-manufacturers.png'), fullPage: true })
  await navigate('0001')
  const [inherited] = await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/nodes/inherit')),
    page.getByRole('button', { name: '沿用历史结构' }).click(),
  ])
  await json(inherited)
  await page.waitForTimeout(500)
  await choose(`node:${spec('Alpha').nodeInstanceId}`)
  await upload('0001', 'alpha')
  const current = await snapshot('0001')
  const leaf = current.placements.find(leaf => leaf.sequenceNumber === '0001')
  await choose(`placement:${leaf.id}`)
  await page.getByLabel('操作类型', { exact: true }).click()
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option-content').getByText('替换', { exact: true }).click()
  await page.waitForTimeout(400)
  await page.getByLabel('生命周期目标', { exact: true }).click()
  await page.waitForTimeout(150)
  const options = await page.locator('.ant-select-dropdown:visible .ant-select-item-option-content').allTextContents()
  assert.equal(options.length, 1)
  assert.match(options[0], /Alpha/)
  assert.doesNotMatch(options[0], /Beta/)
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').first().click()
  const [saved] = await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/metadata') && response.request().method() === 'PUT'),
    page.getByRole('button', { name: '保存叶节点元数据', exact: true }).click(),
  ])
  await json(saved)
  const final = await snapshot('0001')
  const replacement = final.placements.find(item => item.id === leaf.id)
  assert.equal(replacement.operation, 'Replace')
  assert.equal(replacement.lifecycleTargetPlacementId, initial.placements.find(item => item.nodeInstanceId === spec('Alpha').nodeInstanceId).id)
  assert.deepEqual(errors, [])
  await page.screenshot({ path: resolve(output, 'alpha-replacement.png'), fullPage: true })
  await writeFile(resolve(output, 'evidence.json'), JSON.stringify({ applicationId: app.id, initial, final, candidates: options }, null, 2))
  console.log('Node editor browser verification passed: create, preview, clone, same-name PDFs, inherit, Alpha-only replacement.')
} catch (error) {
  await page.screenshot({ path: resolve(output, 'failure.png'), fullPage: true })
  throw error
} finally {
  await browser.close()
}
