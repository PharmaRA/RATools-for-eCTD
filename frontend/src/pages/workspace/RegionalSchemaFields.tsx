import type { RegionalFieldDescriptor, RegionalMetadataProvenance } from '../../api/generated'
import { regionalFieldPath, regionalValueAt, type RegionalValuePath } from './regionalMetadataValues'

export type RegionalSelection = { id: string; label: string; nodeInstanceId?: string | null; ctdSection?: string }
type Props = { fields: readonly RegionalFieldDescriptor[]; value: Record<string, unknown>; disabled: boolean;
  nodes: readonly RegionalSelection[]; placements: readonly RegionalSelection[];
  scope?: string; provenance?: readonly RegionalMetadataProvenance[]; editedPaths?: ReadonlySet<string>;
  onChange: (path: RegionalValuePath, value: unknown) => void }
const object = (value: unknown): Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
  ? value as Record<string, unknown> : {}
const initial = (fields: readonly RegionalFieldDescriptor[]): Record<string, unknown> => Object.fromEntries(fields.map(field =>
  [field.key, field.kind === 'collection' ? [] : field.kind === 'object' ? initial(field.fields) : null]))

export function RegionalSchemaFields({ fields, value, disabled, nodes, placements, scope = 'regional', provenance = [], editedPaths = new Set(), onChange }: Props) {
  const renderFields = (descriptors: readonly RegionalFieldDescriptor[], parent: RegionalValuePath = []) => descriptors.map(field => {
    const path = [...parent, field.key], name = regionalFieldPath(path), current = regionalValueAt(value, path)
    const id = `${scope}-${name}`
    const conditions = field.conditions ?? []
    const matches = (condition: NonNullable<RegionalFieldDescriptor['conditions']>[number]) => {
      const indices = path.filter(key => typeof key === 'number')
      let index = 0
      const conditionPath = condition.fieldPath.replace(/\[(?:\*|)\]/g, () => `[${indices[index++] ?? 0}]`)
      const keys: (string | number)[] = Array.from(conditionPath.matchAll(/([^.[\]]+)|\[(\d+)\]/g), match => match[2] === undefined ? match[1] : Number(match[2]))
      const target = keys.length === 1 ? [...parent, ...keys] : keys
      const entry = object(regionalValueAt(value, parent))
      const external = condition.fieldPath === 'ctdSection' ? (parent[0] === 'documentContexts'
        ? placements.find(option => option.id === entry.placementId)?.ctdSection
        : nodes.find(option => option.id === entry.nodeInstanceId)?.ctdSection) : undefined
      return condition.values.includes(String(external ?? regionalValueAt(value, target) ?? ''))
    }
    const applicable = conditions.filter(condition => condition.effect === 'visible').every(matches)
    const required = field.required || conditions.some(condition => condition.effect === 'required' && matches(condition))
    const changed = [...editedPaths].some(edited => name === edited || name.startsWith(edited + '.') || name.startsWith(edited + '['))
    const origin = provenance.find(item => item.fieldPath === name)
    const source = field.source
    if (!applicable) return <div key={field.key} id={id} tabIndex={-1} data-field-path={name} className="my-2 text-sm">
      <strong>{field.label}</strong>：当前条件不适用；值已保留：{current === undefined || current === null ? '空' : typeof current === 'object' ? JSON.stringify(current) : String(current)}
      <span className="block text-gray-500">{conditions.map(item => item.description).join('；')}</span>
    </div>
    if (field.kind === 'object') return <fieldset key={field.key} className="my-3 border border-gray-200 p-3">
      <legend>{field.label}</legend>{renderFields(field.fields, path)}</fieldset>
    if (field.kind === 'collection') {
      const entries: unknown[] = Array.isArray(current) ? current : []
      const identity = field.identityField || 'id'
      const identityOptions = identity === 'nodeInstanceId' ? nodes : identity === 'placementId' ? placements : undefined
      const move = (index: number, delta: number) => {
        const next = [...entries]; [next[index], next[index + delta]] = [next[index + delta], next[index]]
        onChange(path, next)
      }
      return <fieldset key={field.key} id={id} tabIndex={-1} data-field-path={name} className="my-3 border border-gray-200 p-3">
        <legend>{field.label}{required ? ' *' : ''}</legend>
        {entries.map((item, index) => {
          const entry = object(item), entryPath = [...path, index], entryName = regionalFieldPath(entryPath)
          const identityValue = String(entry[identity] ?? '')
          return <div key={identityValue || `new-${index}`} className="my-2 border border-gray-100 p-2">
            <div className="flex gap-2 items-center mb-2"><strong>{field.label} {index + 1}</strong>
              <button type="button" disabled={disabled || index === 0} aria-label={`上移 ${entryName}`} onClick={() => move(index, -1)}>上移</button>
              <button type="button" disabled={disabled || index === entries.length - 1} aria-label={`下移 ${entryName}`} onClick={() => move(index, 1)}>下移</button>
              <button type="button" disabled={disabled} aria-label={`移除 ${entryName}`} onClick={() => onChange(path, entries.filter((_, at) => at !== index))}>移除条目</button>
            </div>
            {identityOptions && <label className="block mb-2">{identity === 'placementId' ? '文档放置项' : '业务节点'}
              <select disabled={disabled} className="block w-full border p-1" aria-label={`${entryName}.${identity}`} value={identityValue}
                onChange={event => {
                  const selected = identityOptions.find(option => option.id === event.target.value)
                  onChange(entryPath, { ...entry, [identity]: event.target.value,
                    ...(identity === 'placementId' ? { nodeInstanceId: selected?.nodeInstanceId ?? null } : {}) })
                }}>
                <option value="">请选择</option>
                {identityValue && !identityOptions.some(option => option.id === identityValue) && <option value={identityValue}>{identityValue}（当前工作区中不可用）</option>}
                {identityOptions.map(option => <option key={option.id} value={option.id}
                  disabled={entries.some((other, at) => at !== index && object(other)[identity] === option.id)}>{option.label}</option>)}
              </select>
            </label>}
            {renderFields(field.fields.filter(child => child.key !== identity && !(identity === 'placementId' && child.key === 'nodeInstanceId')), entryPath)}
          </div>
        })}
        <button type="button" disabled={disabled || entries.length >= 1000} aria-label={`新增 ${name}`} onClick={() => onChange(path,
          [...entries, { ...initial(field.fields), [identity]: identityOptions ? '' : crypto.randomUUID() }])}>新增 {field.label}</button>
      </fieldset>
    }
    return <div key={field.key} className="my-2" data-field-path={name}>
      <label htmlFor={id} className="block text-sm">{field.label}{required ? ' *' : ''}</label>
      <input id={id} aria-label={name} aria-required={required} disabled={disabled} className="block w-full border border-gray-300 p-1 rounded"
        value={typeof current === 'string' || typeof current === 'number' ? current : ''} list={field.kind === 'enum' ? `${id}-options` : undefined}
        onChange={event => onChange(path, event.target.value === '' ? null : event.target.value)} />
      {field.kind === 'enum' && <datalist id={`${id}-options`}>{field.options.map(option => <option key={option} value={option} />)}</datalist>}
      <small className="block text-gray-500">{changed ? '人工修改，保存后记录来源' : origin ? `来源：${origin.origin} · ${origin.status}${origin.sourceRevisionId ? ` · ${origin.sourceRevisionId}` : ''}` : '待填写 / 尚无来源记录'}</small>
      {source && <small className="block text-gray-500" title={`${source.sha256} · ${source.sourceUri}`}>规范来源：{source.resourceKey} · {source.locator}</small>}
      {field.businessEvidence && <small className="block text-gray-500">{field.businessEvidence}</small>}
      {conditions.length > 0 && <small className="block text-gray-500">适用条件：{conditions.map(item => item.description).join('；')}</small>}
    </div>
  })
  return <div>{renderFields(fields)}</div>
}
