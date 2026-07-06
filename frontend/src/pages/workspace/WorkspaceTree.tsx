import { Alert, Card, Spin, Tag, Tree } from 'antd'
import { CheckCircle, FileText, FolderOpen } from 'lucide-react'

import { ectdAllowedExtensionsHint } from '../../ectdFileTypes'
import {
  buildWorkspaceTreeNodeClassName,
  findWorkspaceTreeNode,
  getWorkspaceTreeNodeDropCapabilities,
  getWorkspaceTreeNodeTitleParts,
  type WorkspaceTreeNode,
} from '../../workspaceTree'
import { addSectionExpansionKeys } from '../appShared'
import type { UseWorkspaceDragDropResult } from './useWorkspaceDragDrop'

type WorkspaceTreeProps = {
  treeData: WorkspaceTreeNode[]
  expandedKeys: string[]
  selectedTreeKey: string | null
  loading: boolean
  treeLoading: boolean
  treeError: string | null
  setExpandedKeys: (keys: string[] | ((current: string[]) => string[])) => void
  onSelectNode: (node: WorkspaceTreeNode) => void
  dragDrop: UseWorkspaceDragDropResult
}

export const WorkspaceTree = ({
  treeData,
  expandedKeys,
  selectedTreeKey,
  loading,
  treeLoading,
  treeError,
  setExpandedKeys,
  onSelectNode,
  dragDrop,
}: WorkspaceTreeProps) => (
  <Card title="eCTD Structure (Drag & Drop files here)" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
    {treeError && <Alert type="error" showIcon className="mb-3" title="Failed to load eCTD structure" description={treeError} />}
    <p className="mb-2 text-xs text-gray-500">Tip: Drag a mapped file node to a section node to move it. Allowed extensions: {ectdAllowedExtensionsHint}</p>
    <Spin spinning={loading || treeLoading}>
      <Tree
        className="ectd-tree"
        treeData={treeData}
        expandedKeys={expandedKeys}
        selectedKeys={selectedTreeKey ? [selectedTreeKey] : []}
        blockNode
        height={520}
        virtual
        motion={null}
        onExpand={(keys) => setExpandedKeys(keys.map((key) => String(key)))}
        onSelect={(keys) => {
          const selectedKey = keys.length > 0 ? String(keys[0]) : null
          if (!selectedKey) {
            return
          }

          const resolvedSelectedNode = findWorkspaceTreeNode(treeData, selectedKey)
          if (!resolvedSelectedNode) {
            return
          }

          onSelectNode(resolvedSelectedNode)
          setExpandedKeys((current) => addSectionExpansionKeys(current, resolvedSelectedNode.sectionPath))
        }}
        titleRender={(nodeData: WorkspaceTreeNode) => {
          const isSelected = selectedTreeKey === nodeData.key
          const isHovered = dragDrop.dragOverNode === nodeData.key
          const {
            isSection,
            acceptsPlacementDrop,
            acceptsFileDrop,
            canDrop,
          } = getWorkspaceTreeNodeDropCapabilities(nodeData, dragDrop.draggingPlacementId)
          const isBusy = loading || treeLoading
          const { text: titleText, prefix: titlePrefix, label: titleLabel } = getWorkspaceTreeNodeTitleParts(nodeData)

          return (
            <div
              role="treeitem"
              tabIndex={isSection || !isBusy ? 0 : -1}
              aria-label={titleText}
              aria-grabbed={nodeData.nodeType === 'document' ? dragDrop.draggingPlacementId === nodeData.placementId : undefined}
              draggable={!isSection && !isBusy}
              onDragStart={(e) => dragDrop.handleDragStart(nodeData, e.dataTransfer)}
              onDragEnd={dragDrop.handleDragEnd}
              onDragOver={(e) => dragDrop.handleDragOver(e, nodeData.key, acceptsPlacementDrop, acceptsFileDrop)}
              onDragLeave={(e) => dragDrop.handleDragLeave(e, nodeData.key)}
              onDrop={(e) => dragDrop.handleNodeDrop({
                event: e,
                nodeData,
                acceptsPlacementDrop,
                acceptsFileDrop,
              })}
              onKeyDown={(e) => {
                void dragDrop.handleNodeKeyDown(e, nodeData, acceptsPlacementDrop)
              }}
              className={buildWorkspaceTreeNodeClassName({
                nodeType: nodeData.nodeType,
                canDrop,
                isHovered,
                isSelected,
                isDragging: nodeData.nodeType === 'document' && dragDrop.draggingPlacementId === nodeData.placementId,
              })}
            >
              <div className="ectd-tree-node__main">
                <span className="ectd-tree-node__icon">
                  {isSection ? <FolderOpen size={16} /> : <FileText size={16} />}
                </span>
                <div className="ectd-tree-node__text">
                  <div className="ectd-tree-node__labelRow">
                    {titlePrefix && <span className="ectd-tree-node__prefix">{titlePrefix}</span>}
                    <span className="ectd-tree-node__label">{titleLabel}</span>
                    {nodeData.nodeType === 'document' && <Tag className="ectd-tree-node__tag" color="blue">{nodeData.operation}</Tag>}
                  </div>
                </div>
              </div>
              {nodeData.nodeType === 'section' && nodeData.hasPlacement && <CheckCircle size={14} className="ectd-tree-node__status" />}
            </div>
          )
        }}
      />
    </Spin>
  </Card>
)
