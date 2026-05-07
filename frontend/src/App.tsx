import { useState, useEffect, useMemo } from 'react';
import { 
  Table, Button, Tag, Space, Drawer, Descriptions, 
  Tabs, Form, Input, Select, Card, 
  Statistic, Row, Col, Alert, Spin, message, Badge, Tree, Modal, Tooltip, Radio
} from 'antd';
import {
  Download, Activity, ArrowLeft, 
  CheckCircle, XCircle, FolderOpen, FileText, Plus, PlayCircle, Save, Trash2, HardDrive
} from 'lucide-react';
import {
  attachDocumentNodes,
  findWorkspaceTreeNode,
  mapSectionTreeData,
  resolveUploadSection,
  type DocumentPlacementRecord,
  type DocumentRecord,
  type EctdStructureNode,
  type WorkspaceTreeNode,
} from './workspaceTree';
import { ApiRequestError, apiFetch } from './apiClient';
import {
  performDelete,
  performBatchDelete,
  type BatchDeleteSummary,
  type DeleteMode,
} from './deleteActions';
import {
  mapImportErrorToMessage,
  type ImportApplicationResult,
} from './importActions';
import { createAndExecutePublishJob } from './publishActions';
import {
  createApplication,
  getDefaultEctdTemplateKey,
  importApplicationWithTemplate,
  loadEctdTemplates,
  type EctdTemplateOption,
} from './ectdTemplateActions';
import {
  deletePlacementWithDocument,
  movePlacementToSection,
  PlacementDeletePartialFailureError,
  revisePlacementMetadata,
  serializePlacementDragPayload,
  tryParsePlacementDragPayload,
  WORKSPACE_PLACEMENT_DRAG_MIME,
} from './workspaceActions';
import { ectdAllowedExtensionsHint, isAllowedEctdFileName, splitFileName } from './ectdFileTypes';

// ==========================================
// Types & Interfaces
// ==========================================
interface Application {
  id: string;
  applicationNumber: string;
  region: string;
  ectdTemplateKey?: string;
  ectdTemplateDisplayName?: string;
  sponsorName: string;
  workingDirectoryPath?: string; // [新增] 物理工作区路径
  createdUtc: string;
  sequences: any[];
}

interface RouteState {
  view: 'applications' | 'app_details' | 'workspace';
  applicationId?: string;
  appTitle?: string;
  sequenceNumber?: string;
}

interface EctdStructureResponse {
  profileName: string;
  region: string;
  roots: EctdStructureNode[];
}

// ==========================================
// Helpers
// ==========================================
const formatDate = (dateStr?: string) => {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString();
};

const formatBytes = (bytes: number) => {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

const getStatusColor = (status: string) => {
  switch (status.toLowerCase()) {
    case 'completed': return 'success';
    case 'failed': return 'error';
    case 'running': return 'processing';
    case 'pending': return 'default';
    default: return 'default';
  }
};

const getSectionAncestorKeys = (sectionPath: string) => {
  const segments = sectionPath.split('.').filter(Boolean);
  const keys: string[] = [];

  for (let index = 0; index < segments.length; index += 1) {
    keys.push(segments.slice(0, index + 1).join('.'));
  }

  return keys;
};

// ==========================================
// Drawers (Artifacts & Reports)
// ==========================================
const ArtifactsPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false);
  const [artifacts, setArtifacts] = useState<any[]>([]);

  useEffect(() => {
    if (!jobId) return;
    setLoading(true);
    apiFetch(`/api/publish-jobs/${jobId}/artifacts`)
      .then(data => setArtifacts(data.artifacts || []))
      .catch(err => message.error('Failed to load artifacts: ' + err.message))
      .finally(() => setLoading(false));
  }, [jobId]);

  const columns = [
    { title: 'Name', dataIndex: 'name', key: 'name', render: (t: string) => <b>{t}</b> },
    { title: 'Status', dataIndex: 'exists', key: 'exists', render: (exists: boolean) => exists ? <Tag color="green">Exists</Tag> : <Tag color="red">Missing</Tag> },
    { title: 'Size', dataIndex: 'sizeBytes', key: 'size', render: (s: number) => formatBytes(s) },
    { title: 'Type', dataIndex: 'contentType', key: 'type' },
    { title: 'Action', key: 'action', render: (_: any, record: any) => (
      record.exists ? (
        <Button type="link" icon={<Download size={14} className="mr-1" />} href={`/api/publish-jobs/${jobId}/artifacts/${record.name}/download`} target="_blank" download>
          Download
        </Button>
      ) : <span className="text-gray-400">Unavailable</span>
    )}
  ];

  return (
    <Drawer title="Publish Artifacts" placement="right" width={600} onClose={onClose} open={!!jobId}>
      {loading ? <Spin className="w-full mt-10 flex justify-center" /> : <Table dataSource={artifacts} columns={columns} rowKey="name" pagination={false} size="small" />}
    </Drawer>
  );
};

const ReportPanel = ({ jobId, onClose }: { jobId: string | null, onClose: () => void }) => {
  const [loading, setLoading] = useState(false);
  const [errorState, setErrorState] = useState<{ status: number, message: string } | null>(null);
  const [report, setReport] = useState<any>(null);

  useEffect(() => {
    if (!jobId) return;
    setLoading(true); setErrorState(null); setReport(null);
    apiFetch(`/api/publish-jobs/${jobId}/report`)
      .then(data => setReport(data))
      .catch(err => setErrorState(err))
      .finally(() => setLoading(false));
  }, [jobId]);

  const renderError = () => {
    if (!errorState) return null;
    let title = "无法加载报告";
    let type: 'error' | 'warning' | 'info' = 'error';
    if (errorState.status === 404) { title = "报告不存在 (404)"; type = "warning"; }
    if (errorState.status === 409) { title = "任务未完成 (409)"; type = "info"; }
    if (errorState.status === 410) { title = "报告文件已缺失 (410)"; type = "warning"; }
    if (errorState.status === 422) { title = "报告已损坏 (422)"; type = "error"; }
    return <Alert message={title} description={errorState.message} type={type} showIcon className="mt-4" />;
  };

  return (
    <Drawer title="Publish Report Details" placement="right" width={800} onClose={onClose} open={!!jobId}>
      {loading && <Spin className="w-full mt-10 flex justify-center" />}
      {renderError()}
      {report && (
        <div className="flex flex-col gap-4">
          <div className="flex justify-between items-center bg-gray-50 p-4 rounded">
            <div>
              <h2 className="text-lg font-bold flex items-center gap-2 m-0">
                {report.succeeded ? <CheckCircle className="text-green-500" /> : <XCircle className="text-red-500" />}
                {report.succeeded ? 'Publish Succeeded' : 'Publish Failed'}
              </h2>
              <p className="text-gray-500 m-0 text-sm mt-1">{report.message}</p>
            </div>
            <Button type="primary" icon={<Download size={16} className="mr-1"/>} href={`/api/publish-jobs/${jobId}/artifacts/PublishReport/download`} target="_blank">
              Download JSON
            </Button>
          </div>
          <Descriptions bordered size="small" column={2}>
            <Descriptions.Item label="Profile">{report.validationProfile}</Descriptions.Item>
            <Descriptions.Item label="Duration">{report.durationMs} ms</Descriptions.Item>
            <Descriptions.Item label="Errors">{report.errorCount}</Descriptions.Item>
            <Descriptions.Item label="Warnings">{report.warningCount}</Descriptions.Item>
          </Descriptions>
          <Tabs defaultActiveKey="issues">
            <Tabs.TabPane tab={`Validation Issues (${report.validationReport?.issues?.length || 0})`} key="issues">
              <Table dataSource={report.validationReport?.issues || []} rowKey={(_, i) => i + ''} pagination={{ pageSize: 10 }} size="small"
                columns={[
                  { title: 'Severity', dataIndex: 'severity', render: (s: string) => <Tag color={s==='Error'?'red':'orange'}>{s}</Tag>, width: 100 },
                  { title: 'Code', dataIndex: 'code', width: 200 },
                  { title: 'Message', dataIndex: 'message' }
                ]}
              />
            </Tabs.TabPane>
          </Tabs>
        </div>
      )}
    </Drawer>
  );
};

// ==========================================
// Component: Publish History Tab
// ==========================================
const PublishHistoryTab = ({ appId }: { appId: string }) => {
  const [loading, setLoading] = useState(false);
  const [data, setData] = useState<any>(null);
  const [selectedReportJobId, setSelectedReportJobId] = useState<string | null>(null);
  const [selectedArtifactsJobId, setSelectedArtifactsJobId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  const fetchHistory = () => {
    setLoading(true);
    const values = form.getFieldsValue();
    const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() });
    if (values.sequenceNumber) params.append('sequenceNumber', values.sequenceNumber);
    if (values.status) params.append('status', values.status);
    
    apiFetch(`/api/applications/${appId}/publish-history?${params.toString()}`)
      .then(res => setData(res))
      .catch(err => message.error('Failed to load history: ' + err.message))
      .finally(() => setLoading(false));
  };

  useEffect(() => { fetchHistory(); }, [appId, page, pageSize]);

  const columns = [
    { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'seq' },
    { title: 'Status', dataIndex: 'status', key: 'status', render: (s: string) => <Badge status={getStatusColor(s) as any} text={s} /> },
    { title: 'Profile', dataIndex: 'validationProfile', key: 'profile' },
    { title: 'Created', dataIndex: 'createdUtc', key: 'created', render: formatDate },
    {
      title: 'Actions', key: 'actions', fixed: 'right' as const, width: 200,
      render: (_: any, r: any) => (
        <Space>
          <Button size="small" onClick={() => setSelectedReportJobId(r.publishJobId)}>Report</Button>
          <Button size="small" type="primary" ghost onClick={() => setSelectedArtifactsJobId(r.publishJobId)}>Artifacts</Button>
        </Space>
      )
    }
  ];

  return (
    <div className="flex flex-col gap-4">
      {data?.statusSummary && (
        <Row gutter={16}>
          <Col span={8}><Card size="small" bordered className="shadow-sm"><Statistic title="Completed Jobs" value={data.statusSummary.completedCount} valueStyle={{ color: '#3f8600' }} /></Card></Col>
          <Col span={8}><Card size="small" bordered className="shadow-sm"><Statistic title="Failed Jobs" value={data.statusSummary.failedCount} valueStyle={{ color: '#cf1322' }} /></Card></Col>
          <Col span={8}><Card size="small" bordered className="shadow-sm"><Statistic title="Running Jobs" value={data.statusSummary.runningCount} valueStyle={{ color: '#1677ff' }} /></Card></Col>
        </Row>
      )}
      <div className="bg-white p-4 rounded border border-gray-200">
        <Form form={form} layout="inline" onFinish={() => setPage(1)} className="mb-4">
          <Form.Item name="sequenceNumber" label="Sequence"><Input placeholder="e.g. 0000" allowClear className="w-32" /></Form.Item>
          <Form.Item name="status" label="Status">
            <Select placeholder="All" allowClear className="w-32">
              <Select.Option value="Completed">Completed</Select.Option>
              <Select.Option value="Failed">Failed</Select.Option>
              <Select.Option value="Running">Running</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item><Button type="primary" htmlType="submit">Filter</Button><Button className="ml-2" onClick={() => { form.resetFields(); setPage(1); }}>Reset</Button></Form.Item>
        </Form>
        <Table loading={loading} dataSource={data?.entries || []} columns={columns} rowKey="publishJobId" size="small"
          pagination={{ current: page, pageSize, total: data?.totalCount || 0, showSizeChanger: true, onChange: (p, ps) => { setPage(p); setPageSize(ps); } }}
        />
      </div>
      <ReportPanel jobId={selectedReportJobId} onClose={() => setSelectedReportJobId(null)} />
      <ArtifactsPanel jobId={selectedArtifactsJobId} onClose={() => setSelectedArtifactsJobId(null)} />
    </div>
  );
};

// ==========================================
// Component: Sequence Workspace (D&D)
// ==========================================
const SequenceWorkspace = ({ appId, seqNumber, onBack }: { appId: string, seqNumber: string, onBack: () => void }) => {
  const [placements, setPlacements] = useState<DocumentPlacementRecord[]>([]);
  const [documentsById, setDocumentsById] = useState<Record<string, DocumentRecord>>({});
  const [loading, setLoading] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [dragOverNode, setDragOverNode] = useState<string | null>(null);
  const [draggingPlacementId, setDraggingPlacementId] = useState<string | null>(null);
  const [treeLoading, setTreeLoading] = useState(false);
  const [treeError, setTreeError] = useState<string | null>(null);
  const [ectdRoots, setEctdRoots] = useState<EctdStructureNode[]>([]);
  const [expandedKeys, setExpandedKeys] = useState<string[]>([]);
  const [selectedTreeKey, setSelectedTreeKey] = useState<string | null>(null);
  const [selectedSectionPath, setSelectedSectionPath] = useState<string | null>(null);
  const [deletingPlacementIds, setDeletingPlacementIds] = useState<Set<string>>(new Set());
  const [movingPlacementIds, setMovingPlacementIds] = useState<Set<string>>(new Set());
  const [savingRevisionPlacementId, setSavingRevisionPlacementId] = useState<string | null>(null);
  const [isPublishModalOpen, setIsPublishModalOpen] = useState(false);

  const treeData = useMemo(() => {
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById);
  }, [documentsById, ectdRoots, placements]);

  const [metadataForm] = Form.useForm();
  const [publishForm] = Form.useForm();
  const revisedPrefix = Form.useWatch('fileNamePrefix', metadataForm);

  const selectedNode = useMemo(
    () => (selectedTreeKey ? findWorkspaceTreeNode(treeData, selectedTreeKey) : undefined),
    [selectedTreeKey, treeData],
  );

  const selectedPlacement = useMemo(() => {
    if (!selectedNode || selectedNode.nodeType !== 'document') {
      return undefined;
    }

    return placements.find((placement) => placement.id === selectedNode.placementId);
  }, [placements, selectedNode]);

  const selectedDocument = useMemo(() => {
    if (!selectedPlacement) {
      return undefined;
    }

    return documentsById[selectedPlacement.documentId];
  }, [documentsById, selectedPlacement]);

  const selectedSectionChildrenCount = useMemo(() => {
    if (!selectedNode || selectedNode.nodeType !== 'section') {
      return 0;
    }

    return selectedNode.children.filter((child) => child.nodeType === 'document').length;
  }, [selectedNode]);

  const selectedDocumentNameParts = useMemo(() => {
    if (!selectedDocument) {
      return { prefix: '', extension: '' };
    }

    return splitFileName(selectedDocument.fileName);
  }, [selectedDocument]);

  useEffect(() => {
    if (!selectedNode || selectedNode.nodeType !== 'document' || !selectedPlacement || !selectedDocument) {
      metadataForm.resetFields();
      return;
    }

    metadataForm.setFieldsValue({
      title: selectedPlacement.title || '',
      fileNamePrefix: selectedDocumentNameParts.prefix,
    });
  }, [metadataForm, selectedDocumentNameParts.prefix, selectedNode, selectedPlacement]);

  useEffect(() => {
    if (!selectedTreeKey) {
      return;
    }

    const resolvedSelectedNode = findWorkspaceTreeNode(treeData, selectedTreeKey);
    if (!resolvedSelectedNode) {
      setSelectedTreeKey(null);
      return;
    }

    if (selectedSectionPath !== resolvedSelectedNode.sectionPath) {
      setSelectedSectionPath(resolvedSelectedNode.sectionPath);
    }
  }, [selectedSectionPath, selectedTreeKey, treeData]);

  const fetchPlacements = async () => {
    try {
      const res = await apiFetch(`/api/document-placements`);
      const list = Array.isArray(res) ? res : (res.items || []);
      const mapped = list.filter((p: DocumentPlacementRecord) => p.applicationId === appId && p.sequenceNumber === seqNumber);
      setPlacements(mapped);
    } catch (e) {
      console.warn('Could not fetch existing placements', e);
    }
  };

  const fetchDocuments = async () => {
    try {
      const docs = await apiFetch('/api/documents') as DocumentRecord[];
      const mapped = Object.fromEntries((docs || []).map((doc) => [doc.id, doc]));
      setDocumentsById(mapped);
    } catch (e) {
      console.warn('Could not fetch documents', e);
    }
  };

  const fetchEctdStructure = async () => {
    setTreeLoading(true);
    setTreeError(null);
    try {
      const app = await apiFetch(`/api/applications/${appId}`) as Application;
      const response = await apiFetch(`/api/applications/${appId}/ectd-structure`) as EctdStructureResponse;
      const roots = response.roots || [];
      setEctdRoots(roots);
      setExpandedKeys(roots.map((node) => node.sectionPath));
    } catch (e: any) {
      setTreeError(e.message || 'Failed to load eCTD structure');
      setEctdRoots([]);
      setExpandedKeys([]);
    } finally {
      setTreeLoading(false);
    }
  };

  useEffect(() => {
    fetchPlacements();
    fetchDocuments();
  }, [appId, seqNumber]);
  useEffect(() => { fetchEctdStructure(); }, [appId]);

  const refreshWorkspaceData = async () => {
    await Promise.all([fetchPlacements(), fetchDocuments()]);
  };

  const getPlacementPayloadFromDataTransfer = (dataTransfer: DataTransfer) => {
    const preferred = tryParsePlacementDragPayload(dataTransfer.getData(WORKSPACE_PLACEMENT_DRAG_MIME));
    if (preferred) {
      return preferred;
    }

    return tryParsePlacementDragPayload(dataTransfer.getData('text/plain'));
  };

  const handleMovePlacement = async (placementId: string, fromSection: string, toSection: string) => {
    setMovingPlacementIds((current) => new Set(current).add(placementId));
    setLoading(true);
    try {
      const moved = await movePlacementToSection({ placementId, fromSection, toSection });

      if (!moved) {
        message.info('Document is already mapped to this section.');
        return;
      }

      setExpandedKeys((current) => Array.from(new Set([...current, ...getSectionAncestorKeys(toSection)])));
      setSelectedTreeKey(toSection);
      setSelectedSectionPath(toSection);
      await refreshWorkspaceData();
      message.success('Document moved to target section.');
    } catch (error: any) {
      message.error(`Failed to move document: ${error?.message || 'Unknown error'}`);
    } finally {
      setMovingPlacementIds((current) => {
        const next = new Set(current);
        next.delete(placementId);
        return next;
      });
      setLoading(false);
    }
  };

  const handleDeletePlacementWithFile = async (placementId: string, documentId: string) => {
    setDeletingPlacementIds((current) => new Set(current).add(placementId));
    setLoading(true);
    try {
      await deletePlacementWithDocument({ placementId, documentId });
      await refreshWorkspaceData();
      message.success('Document mapping and physical file deleted.');
    } catch (error: any) {
      if (error instanceof PlacementDeletePartialFailureError) {
        message.error(`Mapping deleted, but document/file delete failed: ${error.message}`);
      } else {
        message.error(`Failed to delete mapped document: ${error?.message || 'Unknown error'}`);
      }
    } finally {
      setDeletingPlacementIds((current) => {
        const next = new Set(current);
        next.delete(placementId);
        return next;
      });
      setLoading(false);
    }
  };

  const confirmDeletePlacement = (placementId: string, documentId: string) => {
    Modal.confirm({
      title: 'Delete mapped document',
      content: 'This will remove mapping and delete the physical file from workspace. Continue?',
      okText: 'Delete',
      okButtonProps: { danger: true },
      cancelText: 'Cancel',
      onOk: async () => {
        await handleDeletePlacementWithFile(placementId, documentId);
      },
    });
  };

  const handleSaveRevision = async () => {
    if (!selectedPlacement || !selectedDocument) {
      return;
    }

    const values = await metadataForm.validateFields();
    const normalizedPrefix = String(values.fileNamePrefix || '').trim();

    setSavingRevisionPlacementId(selectedPlacement.id);
    setLoading(true);
    try {
      await revisePlacementMetadata({
        placementId: selectedPlacement.id,
        title: String(values.title || '').trim() || undefined,
        fileNamePrefix: normalizedPrefix,
      });
      await refreshWorkspaceData();
      message.success('File metadata revision saved.');
    } catch (error: any) {
      message.error(`Failed to save metadata revision: ${error?.message || 'Unknown error'}`);
    } finally {
      setSavingRevisionPlacementId(null);
      setLoading(false);
    }
  };

  const handleDirectDrop = async (file: File, targetNodeKey: string) => {
    setLoading(true);
    message.loading({ content: `Processing ${file.name}...`, key: 'uploading' });
    
    try {
      const targetSection = resolveUploadSection(targetNodeKey, selectedSectionPath);
      setExpandedKeys((current) => Array.from(new Set([...current, ...getSectionAncestorKeys(targetSection)])));
      setSelectedTreeKey(targetSection);
      setSelectedSectionPath(targetSection);

      const formData = new FormData();
      formData.append('file', file);
      formData.append('CtdSection', targetSection);
      const docRes = await apiFetch(`/api/applications/${appId}/sequences/${seqNumber}/documents/upload`, { method: 'POST', body: formData });

      await apiFetch('/api/document-placements', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          applicationId: appId,
          sequenceNumber: seqNumber,
          documentId: docRes.id,
          ctdSection: targetSection,
          operation: 'New'
        })
      });

      message.success({ content: `${file.name} mapped to ${targetSection} and saved!`, key: 'uploading' });
      await refreshWorkspaceData();
    } catch (err: any) {
      message.error({ content: `Failed: ${err.message}`, key: 'uploading' });
    } finally {
      setLoading(false);
    }
  };

  const openPublishModal = () => {
    publishForm.setFieldsValue({
      outputDirectoryPath: '',
    });
    setIsPublishModalOpen(true);
  };

  const handlePublishModalCancel = () => {
    setIsPublishModalOpen(false);
    publishForm.resetFields();
  };

  const triggerPublish = async () => {
    const values = await publishForm.validateFields();
    setPublishing(true);
    try {
      await createAndExecutePublishJob({
        applicationId: appId,
        sequenceNumber: String(seqNumber).trim(),
        outputDirectoryPath: String(values.outputDirectoryPath || '').trim(),
      });
      
      message.success('Publish job initiated successfully! Check History tab for results.');
      setIsPublishModalOpen(false);
      publishForm.resetFields();
      onBack();
    } catch (err: any) {
      message.error('Publish failed: ' + err.message);
    } finally {
      setPublishing(false);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-between items-center bg-white p-4 rounded shadow-sm border border-gray-200">
        <div className="flex items-center gap-4">
          <Button icon={<ArrowLeft size={16} />} onClick={onBack}>Back to Details</Button>
          <div>
            <h2 className="m-0 text-xl font-bold">Sequence Workspace: <Tag color="blue">{seqNumber}</Tag></h2>
            <p className="m-0 text-gray-500 text-sm flex items-center gap-1">
              <Save size={14}/> Changes are saved automatically upon document drop.
            </p>
          </div>
        </div>
        <Button type="primary" icon={<PlayCircle size={16} className="mr-1"/>} loading={publishing} onClick={openPublishModal}>
          Publish Sequence
        </Button>
      </div>

      <Modal
        title="Publish Sequence"
        open={isPublishModalOpen}
        onCancel={handlePublishModalCancel}
        onOk={triggerPublish}
        confirmLoading={publishing}
        destroyOnClose
      >
        <Form form={publishForm} layout="vertical" requiredMark={false}>
          <Form.Item
            name="outputDirectoryPath"
            label="Export Directory"
            rules={[{ required: true, message: 'Export directory is required.' }]}
          >
            <Input placeholder="E:\exports\submission-a" />
          </Form.Item>
        </Form>
      </Modal>

      <Row gutter={16}>
        <Col span={12}>
          <Card title="eCTD Structure (Drag & Drop files here)" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {treeError && <Alert type="error" showIcon className="mb-3" message="Failed to load eCTD structure" description={treeError} />}
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
                  const selectedKey = keys.length > 0 ? String(keys[0]) : null;
                  if (!selectedKey) {
                    setSelectedTreeKey(null);
                    return;
                  }

                  const selectedNode = findWorkspaceTreeNode(treeData, selectedKey);
                  if (!selectedNode) {
                    return;
                  }

                  setSelectedTreeKey(selectedNode.key);
                  setSelectedSectionPath(selectedNode.sectionPath);
                  setExpandedKeys((current) => Array.from(new Set([...current, ...getSectionAncestorKeys(selectedNode.sectionPath)])));
                }}
                titleRender={(nodeData: WorkspaceTreeNode) => {
                  const isSelected = selectedTreeKey === nodeData.key;
                  const isHovered = dragOverNode === nodeData.key;
                  const isSection = nodeData.nodeType === 'section';
                  const acceptsPlacementDrop = isSection;
                  const acceptsFileDrop = isSection && nodeData.canDrop;
                  const canDrop = acceptsFileDrop || (isSection && draggingPlacementId !== null);
                  const isBusy = loading || treeLoading;
                  const titleText = String(nodeData.title ?? '');
                  const titleMatch = isSection ? /^([0-9]+(?:\.[0-9A-Z]+)*)\s+(.+)$/.exec(titleText) : null;
                  const titlePrefix = titleMatch ? titleMatch[1] : null;
                  const titleLabel = titleMatch ? titleMatch[2] : titleText;

                  return (
                      <div
                        draggable={!isSection && !isBusy}
                        onDragStart={(e) => {
                          if (isSection || nodeData.nodeType !== 'document') {
                            return;
                          }

                          setDraggingPlacementId(nodeData.placementId);
                          e.dataTransfer.effectAllowed = 'move';
                          const payload = serializePlacementDragPayload({
                            placementId: nodeData.placementId,
                            documentId: nodeData.documentId,
                            sectionPath: nodeData.sectionPath,
                          });
                          e.dataTransfer.setData(
                            WORKSPACE_PLACEMENT_DRAG_MIME,
                            payload,
                          );
                          e.dataTransfer.setData('text/plain', payload);
                        }}
                        onDragEnd={() => setDraggingPlacementId(null)}
                        onDragOver={(e) => {
                          e.preventDefault();
                          e.stopPropagation();

                          const internalPayload = getPlacementPayloadFromDataTransfer(e.dataTransfer);
                          const internalDragActive = draggingPlacementId !== null || internalPayload !== null;
                          const allowDrop = internalDragActive ? acceptsPlacementDrop : acceptsFileDrop;

                          e.dataTransfer.dropEffect = allowDrop
                            ? (internalDragActive ? 'move' : 'copy')
                            : 'none';

                          if (allowDrop) {
                            setDragOverNode(nodeData.key);
                          } else if (dragOverNode === nodeData.key) {
                            setDragOverNode(null);
                          }
                        }}
                       onDragLeave={(e) => {
                         e.preventDefault();
                         e.stopPropagation();
                         if (dragOverNode === nodeData.key) setDragOverNode(null);
                       }}
                        onDrop={async (e) => {
                          e.preventDefault(); e.stopPropagation();
                          setDragOverNode(null);

                          const internalPayload = getPlacementPayloadFromDataTransfer(e.dataTransfer)
                            ?? (() => {
                              if (!draggingPlacementId) {
                                return null;
                              }

                              const placement = placements.find((item) => item.id === draggingPlacementId);
                              if (!placement) {
                                return null;
                              }

                              return {
                                placementId: placement.id,
                                documentId: placement.documentId,
                                sectionPath: placement.ctdSection,
                              };
                            })();

                          if (internalPayload) {
                            if (!acceptsPlacementDrop) {
                              message.warning('Move documents onto a section node.');
                              return;
                            }

                            await handleMovePlacement(internalPayload.placementId, internalPayload.sectionPath, nodeData.sectionPath);
                            setDraggingPlacementId(null);
                            return;
                          }

                          const files = e.dataTransfer.files;
                          if (!files || files.length === 0) {
                            return;
                          }

                          if (!acceptsFileDrop) {
                            message.warning(nodeData.nodeType === 'document'
                              ? 'Drop files on a section, not a document.'
                              : 'Only leaf sections accept dropped files.');
                            return;
                          }

                         const file = files[0];
                         if (!isAllowedEctdFileName(file.name)) {
                           message.error(`Unsupported file extension. Allowed: ${ectdAllowedExtensionsHint}`);
                           return;
                         }
                         await handleDirectDrop(file, nodeData.sectionPath);
                        }}
                        className={`ectd-tree-node ${isSection ? 'ectd-tree-node--section' : 'ectd-tree-node--document'} ${canDrop ? 'ectd-tree-node--droppable' : ''} ${isHovered ? 'ectd-tree-node--hover' : ''} ${isSelected ? 'ectd-tree-node--selected' : ''} ${nodeData.nodeType === 'document' && draggingPlacementId === nodeData.placementId ? 'ectd-tree-node--dragging' : ''}`}
                      >
                       <div className="ectd-tree-node__main">
                         <span className="ectd-tree-node__icon">
                           {isSection ? <FolderOpen size={16} /> : <FileText size={16} />}
                         </span>
                         <div className="ectd-tree-node__text">
                           <div className="ectd-tree-node__labelRow">
                             {titlePrefix && <span className="ectd-tree-node__prefix">{titlePrefix}</span>}
                             <span className="ectd-tree-node__label">{titleLabel}</span>
                             {!isSection && <Tag className="ectd-tree-node__tag" color="blue">{nodeData.operation}</Tag>}
                           </div>
                         </div>
                       </div>
                       {isSection && nodeData.hasPlacement && <CheckCircle size={14} className="ectd-tree-node__status" />}
                     </div>
                   );
                }}
              />
            </Spin>
          </Card>
        </Col>

        <Col span={12}>
          <Card title="Selection Details" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {!selectedNode && (
              <div className="text-center text-gray-400 mt-20">
                <FolderOpen size={48} className="mx-auto mb-4 opacity-50"/>
                <p>Select a section or mapped file from the left tree.</p>
              </div>
            )}

            {selectedNode?.nodeType === 'section' && (
              <div className="flex flex-col gap-4">
                <Descriptions size="small" bordered column={1} className="selection-details-descriptions">
                  <Descriptions.Item label="Section">{selectedNode.sectionPath}</Descriptions.Item>
                  <Descriptions.Item label="Display">{selectedNode.title}</Descriptions.Item>
                  <Descriptions.Item label="Leaf Node">{selectedNode.canDrop ? 'Yes' : 'No'}</Descriptions.Item>
                  <Descriptions.Item label="Mapped Files">{selectedSectionChildrenCount}</Descriptions.Item>
                </Descriptions>

                <Alert
                  type="info"
                  showIcon
                  message="Leaf Element Data Entry (Reserved)"
                  description="This section is reserved for future leaf element data entry fields."
                />

                <p className="text-xs text-gray-500">Tip: Drop files on leaf sections. Drag file nodes between sections to move them.</p>
              </div>
            )}

            {selectedNode?.nodeType === 'document' && selectedPlacement && selectedDocument && (
              <div className="flex flex-col gap-4">
                <Descriptions size="small" bordered column={1} className="selection-details-descriptions">
                  <Descriptions.Item label="Placement ID">{selectedPlacement.id}</Descriptions.Item>
                  <Descriptions.Item label="eCTD Section"><Tag>{selectedPlacement.ctdSection}</Tag></Descriptions.Item>
                  <Descriptions.Item label="Operation"><Tag color="blue">{selectedPlacement.operation}</Tag></Descriptions.Item>
                  <Descriptions.Item label="Storage Path"><span className="text-xs break-all">{selectedDocument.storagePath}</span></Descriptions.Item>
                </Descriptions>

                <Form form={metadataForm} layout="vertical" requiredMark={false}>
                  <Form.Item name="title" label="Backbone Title (index.xml title)">
                    <Input maxLength={255} placeholder="Optional title" />
                  </Form.Item>
                  <Form.Item
                    name="fileNamePrefix"
                    label="File Prefix"
                    rules={[
                      { required: true, message: 'File prefix is required.' },
                      {
                        validator: (_, value) => (
                          String(value || '').trim().length > 0
                            ? Promise.resolve()
                            : Promise.reject(new Error('File prefix cannot be empty.'))
                        ),
                      },
                    ]}
                  >
                    <Input maxLength={255} placeholder="example-file-name" />
                  </Form.Item>
                  <Form.Item label="Extension">
                    <Input value={selectedDocumentNameParts.extension || '(no extension)'} readOnly />
                  </Form.Item>
                  <Form.Item label="Resulting File Name">
                    <Input
                      value={`${String(revisedPrefix || '').trim()}${selectedDocumentNameParts.extension}`}
                      readOnly
                    />
                  </Form.Item>
                </Form>

                <Space>
                  <Button
                    type="primary"
                    loading={savingRevisionPlacementId === selectedPlacement.id}
                    disabled={loading || deletingPlacementIds.has(selectedPlacement.id) || movingPlacementIds.has(selectedPlacement.id)}
                    onClick={handleSaveRevision}
                  >
                    Save Revision
                  </Button>
                  <Button
                    danger
                    icon={<Trash2 size={14} />}
                    loading={deletingPlacementIds.has(selectedPlacement.id)}
                    disabled={loading || deletingPlacementIds.has(selectedPlacement.id) || movingPlacementIds.has(selectedPlacement.id)}
                    onClick={() => confirmDeletePlacement(selectedPlacement.id, selectedPlacement.documentId)}
                  >
                    Delete
                  </Button>
                </Space>

                <p className="text-xs text-gray-500">Delete removes mapping and physical file. Editing revision only changes the file prefix; extension remains unchanged.</p>
              </div>
            )}
          </Card>
        </Col>
      </Row>
    </div>
  );
};

// ==========================================
// Component: Application Details View
// ==========================================
const ApplicationDetailsView = ({ appId, appTitle, onBack, onOpenWorkspace }: { appId: string, appTitle: string, onBack: () => void, onOpenWorkspace: (seq: string) => void }) => {
  const [appData, setAppData] = useState<Application | null>(null);
  const [loading, setLoading] = useState(false);
  const [deletingSequenceNumbers, setDeletingSequenceNumbers] = useState<Set<string>>(new Set());
  const [seqModalVisible, setSeqModalVisible] = useState(false);
  const [sequenceDeleteDialog, setSequenceDeleteDialog] = useState<{ open: boolean; sequenceNumber: string | null; mode: DeleteMode }>({
    open: false,
    sequenceNumber: null,
    mode: 'databaseOnly',
  });
  const [selectedSequenceKeys, setSelectedSequenceKeys] = useState<string[]>([]);
  const [sequenceBatchDeleteDialog, setSequenceBatchDeleteDialog] = useState<{ open: boolean; mode: DeleteMode; running: boolean }>({
    open: false,
    mode: 'databaseOnly',
    running: false,
  });
  const [sequenceBatchSummary, setSequenceBatchSummary] = useState<BatchDeleteSummary | null>(null);
  const [sequenceBatchSummaryOpen, setSequenceBatchSummaryOpen] = useState(false);
  const [form] = Form.useForm();

  const fetchApp = async () => {
    setLoading(true);
    try {
      const data = await apiFetch('/api/applications');
      const target = data.find((a: any) => a.id === appId);
      setAppData(target || null);
    } catch (e: any) { message.error('Failed to load application details.'); }
    finally { setLoading(false); }
  };

  useEffect(() => { fetchApp(); }, [appId]);

  useEffect(() => {
    setSelectedSequenceKeys([]);
  }, [appId]);

  useEffect(() => {
    const validSequenceKeys = new Set((appData?.sequences || []).map((sequence: any) => String(sequence.sequenceNumber)));
    setSelectedSequenceKeys((current) => {
      const next = current.filter((key) => validSequenceKeys.has(key));
      return next.length === current.length ? current : next;
    });
  }, [appData?.sequences]);

  const handleCreateSequence = async () => {
    try {
      const values = await form.validateFields();
      await apiFetch(`/api/applications/${appId}/sequences`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(values)
      });
      message.success('Sequence created successfully!');
      setSeqModalVisible(false);
      form.resetFields();
      fetchApp();
    } catch (e: any) { message.error('Failed to create sequence: ' + e.message); }
  };

  const handleDeleteSequence = async (seqNumber: string, mode: DeleteMode) => {
    setDeletingSequenceNumbers((current) => new Set(current).add(seqNumber));

    try {
      const outcome = await performDelete('sequence', `/api/applications/${appId}/sequences/${seqNumber}`, mode);

      if (outcome.kind === 'success') {
        message.success(outcome.message);
      } else {
        message.error(outcome.message);
      }

      if (outcome.shouldRefresh) {
        await fetchApp();
      }
    } finally {
      setDeletingSequenceNumbers((current) => {
        const next = new Set(current);
        next.delete(seqNumber);
        return next;
      });
    }
  };

  const openDeleteSequenceDialog = (sequenceNumber: string) => {
    setSequenceDeleteDialog({
      open: true,
      sequenceNumber,
      mode: 'databaseOnly',
    });
  };

  const confirmDeleteSequence = async () => {
    const sequenceNumber = sequenceDeleteDialog.sequenceNumber;
    if (!sequenceNumber) {
      return;
    }

    const mode = sequenceDeleteDialog.mode;
    setSequenceDeleteDialog((current) => ({ ...current, open: false }));
    await handleDeleteSequence(sequenceNumber, mode);
  };

  const confirmBatchDeleteSequences = async () => {
    if (selectedSequenceKeys.length === 0 || deletingSequenceNumbers.size > 0) {
      if (deletingSequenceNumbers.size > 0) {
        message.warning('存在进行中的单条删除，请稍后再试批量删除。');
      }
      return;
    }

    setSequenceBatchDeleteDialog((current) => ({ ...current, running: true }));

    try {
      const mode = sequenceBatchDeleteDialog.mode;
      const items = selectedSequenceKeys.map((sequenceNumber) => ({
        key: sequenceNumber,
        label: sequenceNumber,
        url: `/api/applications/${appId}/sequences/${sequenceNumber}`,
      }));
      const summary = await performBatchDelete('sequence', mode, items);

      setSequenceBatchSummary(summary);
      setSequenceBatchSummaryOpen(true);
      setSequenceBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false });
    } catch (error: any) {
      message.error('批量删除失败: ' + (error?.message || '未知错误'));
      setSequenceBatchDeleteDialog((current) => ({ ...current, running: false }));
    }
  };

  const closeSequenceBatchSummary = async () => {
    setSequenceBatchSummaryOpen(false);
    setSequenceBatchSummary(null);
    setSelectedSequenceKeys([]);
    await fetchApp();
  };

  const failedSequenceBatchResults = (sequenceBatchSummary?.results || []).filter((result) => result.outcome.kind === 'error');
  const hasSingleSequenceDeleteRunning = deletingSequenceNumbers.size > 0;
  const canStartBatchDelete = selectedSequenceKeys.length > 0 && !sequenceBatchDeleteDialog.running && !hasSingleSequenceDeleteRunning;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-4 bg-white p-4 rounded shadow-sm border border-gray-200">
        <Button icon={<ArrowLeft size={16} />} onClick={onBack} disabled={sequenceBatchDeleteDialog.running}>Back to Applications</Button>
        <div className="flex-1">
          <div className="flex justify-between items-start">
            <h2 className="m-0 text-xl font-bold">{appTitle}</h2>
          </div>
          <Space className="mt-2 flex-wrap">
            <Tag color="purple">{appData?.region || 'Unknown Region'}</Tag>
            <span className="text-gray-500 text-sm border-r pr-2">Created: {formatDate(appData?.createdUtc)}</span>
            {appData?.workingDirectoryPath && (
              <Tooltip title="Physical Working Directory Path">
                <span className="text-gray-600 text-sm flex items-center gap-1 bg-gray-100 px-2 py-0.5 rounded font-mono">
                  <HardDrive size={14} className="text-blue-500"/> 
                  {appData.workingDirectoryPath}
                </span>
              </Tooltip>
            )}
          </Space>
        </div>
      </div>

      <div className="bg-white p-4 rounded shadow-sm border border-gray-200">
        <Tabs defaultActiveKey="sequences">
          <Tabs.TabPane tab="Sequences" key="sequences">
            <div className="mb-4 flex justify-end">
              <Space>
                <Button
                  danger
                  icon={<Trash2 size={14} className="mr-1" />}
                  disabled={!canStartBatchDelete}
                  loading={sequenceBatchDeleteDialog.running}
                  onClick={() => {
                    if (hasSingleSequenceDeleteRunning) {
                      return;
                    }
                    setSequenceBatchDeleteDialog({ open: true, mode: 'databaseOnly', running: false });
                  }}
                >
                  Batch Delete
                </Button>
              <Button type="primary" icon={<Plus size={16} className="mr-1"/>} onClick={() => setSeqModalVisible(true)}>
                New Sequence
              </Button>
              </Space>
            </div>
            <Table 
              loading={loading}
              dataSource={appData?.sequences || []} 
              rowKey="sequenceNumber" 
              size="middle"
              rowSelection={{
                selectedRowKeys: selectedSequenceKeys,
                onChange: (nextSelectedRowKeys) => setSelectedSequenceKeys(nextSelectedRowKeys.map((key) => String(key))),
                getCheckboxProps: (record: any) => ({
                  disabled: sequenceBatchDeleteDialog.running || deletingSequenceNumbers.has(String(record.sequenceNumber)),
                }),
              }}
              pagination={{
                onChange: () => setSelectedSequenceKeys([]),
              }}
              columns={[
                { title: 'Sequence', dataIndex: 'sequenceNumber', render: (t) => <b>{t}</b> },
                { title: 'Submission Type', dataIndex: 'submissionType' },
                { title: 'Description', dataIndex: 'description' },
                { title: 'Actions', key: 'actions', render: (_: any, r: any) => (
                  <Space>
                    <Button type="link" size="small" disabled={sequenceBatchDeleteDialog.running} onClick={() => onOpenWorkspace(r.sequenceNumber)}>
                      Enter Workspace
                    </Button>
                    <Button
                      danger
                      type="text"
                      size="small"
                      icon={<Trash2 size={14} />}
                      title="Delete Sequence"
                      loading={deletingSequenceNumbers.has(r.sequenceNumber)}
                      disabled={deletingSequenceNumbers.has(r.sequenceNumber) || sequenceBatchDeleteDialog.running}
                      onClick={() => openDeleteSequenceDialog(r.sequenceNumber)}
                    />
                  </Space>
                )}
              ]}
            />
          </Tabs.TabPane>
          <Tabs.TabPane tab="Publish History" key="history">
            <PublishHistoryTab appId={appId} />
          </Tabs.TabPane>
        </Tabs>
      </div>

      <Modal title="Create New Sequence" open={seqModalVisible} onOk={handleCreateSequence} onCancel={() => setSeqModalVisible(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="sequenceNumber" label="Sequence Number" initialValue="0000" rules={[{ required: true }]}>
            <Input placeholder="0000" />
          </Form.Item>
          <Form.Item name="submissionType" label="Submission Type" initialValue="Original Application" rules={[{ required: true }]}>
            <Select options={[{ value: 'Original Application', label: 'Original Application' }, { value: 'Supplemental Application', label: 'Supplemental Application' }, { value: 'Amendment', label: 'Amendment' }]} />
          </Form.Item>
          <Form.Item name="submissionSubType" label="Submission Sub-Type" initialValue="Presubmission">
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Description" rules={[{ required: true }, { min: 2, max: 512 }]}>
            <Input.TextArea placeholder="e.g. Initial eCTD Submission" rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="删除 Sequence"
        open={sequenceDeleteDialog.open}
        okText="确认删除"
        cancelText="取消"
        onOk={confirmDeleteSequence}
        onCancel={() => setSequenceDeleteDialog({ open: false, sequenceNumber: null, mode: 'databaseOnly' })}
        confirmLoading={
          sequenceDeleteDialog.sequenceNumber !== null
          && deletingSequenceNumbers.has(sequenceDeleteDialog.sequenceNumber)
        }
      >
        <div className="flex flex-col gap-3">
          <div>
            即将删除 Sequence: <Tag>{sequenceDeleteDialog.sequenceNumber ?? '-'}</Tag>
          </div>
          <Radio.Group
            value={sequenceDeleteDialog.mode}
            onChange={(event) => setSequenceDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {sequenceDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              message="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除 Sequence"
        open={sequenceBatchDeleteDialog.open}
        okText="确认批量删除"
        cancelText="取消"
        onOk={confirmBatchDeleteSequences}
        onCancel={() => setSequenceBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })}
        confirmLoading={sequenceBatchDeleteDialog.running}
        okButtonProps={{ disabled: !canStartBatchDelete }}
        cancelButtonProps={{ disabled: sequenceBatchDeleteDialog.running }}
      >
        <div className="flex flex-col gap-3">
          <div>
            已选择 <Tag>{selectedSequenceKeys.length}</Tag> 个 Sequence。
          </div>
          <Radio.Group
            value={sequenceBatchDeleteDialog.mode}
            onChange={(event) => setSequenceBatchDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {sequenceBatchDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              message="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除结果"
        open={sequenceBatchSummaryOpen}
        okText="关闭"
        cancelButtonProps={{ style: { display: 'none' } }}
        onOk={() => { void closeSequenceBatchSummary(); }}
        onCancel={() => { void closeSequenceBatchSummary(); }}
      >
        <div className="flex flex-col gap-3">
          <div>成功: <Tag color="green">{sequenceBatchSummary?.successCount ?? 0}</Tag></div>
          <div>失败: <Tag color="red">{sequenceBatchSummary?.failureCount ?? 0}</Tag></div>
          {failedSequenceBatchResults.length > 0 && (
            <div className="flex flex-col gap-2">
              {failedSequenceBatchResults.map((result) => (
                <Alert
                  key={result.key}
                  type="error"
                  showIcon
                  message={`${result.label}: ${result.outcome.message}`}
                />
              ))}
            </div>
          )}
        </div>
      </Modal>
    </div>
  );
};

// ==========================================
// Component: Application List View
// ==========================================
const ApplicationListView = ({ onSelectApp }: { onSelectApp: (id: string, title: string) => void }) => {
  const [loading, setLoading] = useState(false);
  const [deletingAppIds, setDeletingAppIds] = useState<Set<string>>(new Set());
  const [apps, setApps] = useState<Application[]>([]);
  const [appModalVisible, setAppModalVisible] = useState(false);
  const [importModalVisible, setImportModalVisible] = useState(false);
  const [importingApplication, setImportingApplication] = useState(false);
  const [importResult, setImportResult] = useState<ImportApplicationResult | null>(null);
  const [importResultVisible, setImportResultVisible] = useState(false);
  const [appDeleteDialog, setAppDeleteDialog] = useState<{ open: boolean; appId: string | null; mode: DeleteMode }>({
    open: false,
    appId: null,
    mode: 'databaseOnly',
  });
  const [selectedAppKeys, setSelectedAppKeys] = useState<string[]>([]);
  const [appBatchDeleteDialog, setAppBatchDeleteDialog] = useState<{ open: boolean; mode: DeleteMode; running: boolean }>({
    open: false,
    mode: 'databaseOnly',
    running: false,
  });
  const [appBatchSummary, setAppBatchSummary] = useState<BatchDeleteSummary | null>(null);
  const [appBatchSummaryOpen, setAppBatchSummaryOpen] = useState(false);
  const [form] = Form.useForm();
  const [importForm] = Form.useForm();
  const [ectdTemplates, setEctdTemplates] = useState<EctdTemplateOption[]>([]);
  const [templatesLoading, setTemplatesLoading] = useState(false);

  const fetchApps = async () => {
    setLoading(true);
    try {
      const data = await apiFetch('/api/applications');
      setApps(data);
    } catch (err: any) {
      message.error('Failed to load apps: ' + err.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchApps(); }, []);

  useEffect(() => {
    const fetchTemplates = async () => {
      setTemplatesLoading(true);
      try {
        const templates = await loadEctdTemplates();
        setEctdTemplates(templates);
      } catch (err: any) {
        message.error('Failed to load eCTD templates: ' + err.message);
      } finally {
        setTemplatesLoading(false);
      }
    };

    void fetchTemplates();
  }, []);

  useEffect(() => {
    const validAppIds = new Set(apps.map((app) => app.id));
    setSelectedAppKeys((current) => {
      const next = current.filter((key) => validAppIds.has(key));
      return next.length === current.length ? current : next;
    });
  }, [apps]);

  const defaultTemplateKey = getDefaultEctdTemplateKey(ectdTemplates);
  const ectdTemplateOptions = ectdTemplates.map((template) => ({
    value: template.key,
    label: template.displayName,
  }));

  const handleCreateApp = async () => {
    try {
      const values = await form.validateFields();
      await createApplication({
        applicationNumber: values.applicationNumber,
        ectdTemplateKey: values.ectdTemplateKey,
        sponsorName: values.sponsorName,
        workingDirectoryParentPath: values.workingDirectoryParentPath,
      });
      message.success('Application created with Workspace!');
      setAppModalVisible(false);
      form.resetFields();
      fetchApps();
    } catch (e: any) { message.error('Failed to create application: ' + e.message); }
  };

  const handleImportApplication = async () => {
    try {
      const values = await importForm.validateFields();
      setImportingApplication(true);

      const result = await importApplicationWithTemplate({
        workingDirectoryPath: values.workingDirectoryPath,
        ectdTemplateKey: values.ectdTemplateKey,
        sponsorName: values.sponsorName,
      });

      setImportResult(result);
      setImportResultVisible(true);
      setImportModalVisible(false);
      importForm.resetFields();
      await fetchApps();
      message.success(`Application ${result.applicationNumber} imported.`);
    } catch (error) {
      if (error instanceof ApiRequestError || error instanceof Error) {
        message.error(mapImportErrorToMessage(error));
      }
    } finally {
      setImportingApplication(false);
    }
  };

  const handleDeleteApp = async (id: string, mode: DeleteMode) => {
    setDeletingAppIds((current) => new Set(current).add(id));

    try {
      const outcome = await performDelete('application', `/api/applications/${id}`, mode);

      if (outcome.kind === 'success') {
        message.success(outcome.message);
      } else {
        message.error(outcome.message);
      }

      if (outcome.shouldRefresh) {
        await fetchApps();
      }
    } finally {
      setDeletingAppIds((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });
    }
  };

  const openDeleteAppDialog = (id: string) => {
    setAppDeleteDialog({
      open: true,
      appId: id,
      mode: 'databaseOnly',
    });
  };

  const confirmDeleteApp = async () => {
    const appId = appDeleteDialog.appId;
    if (!appId) {
      return;
    }

    const mode = appDeleteDialog.mode;
    setAppDeleteDialog((current) => ({ ...current, open: false }));
    await handleDeleteApp(appId, mode);
  };

  const confirmBatchDeleteApps = async () => {
    if (selectedAppKeys.length === 0 || deletingAppIds.size > 0) {
      if (deletingAppIds.size > 0) {
        message.warning('存在进行中的单条删除，请稍后再试批量删除。');
      }
      return;
    }

    setAppBatchDeleteDialog((current) => ({ ...current, running: true }));

    try {
      const mode = appBatchDeleteDialog.mode;
      const items = selectedAppKeys.map((appId) => ({
        key: appId,
        label: appId,
        url: `/api/applications/${appId}`,
      }));
      const summary = await performBatchDelete('application', mode, items);

      setAppBatchSummary(summary);
      setAppBatchSummaryOpen(true);
      setAppBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false });
    } catch (error: any) {
      message.error('批量删除失败: ' + (error?.message || '未知错误'));
      setAppBatchDeleteDialog((current) => ({ ...current, running: false }));
    }
  };

  const closeAppBatchSummary = async () => {
    setAppBatchSummaryOpen(false);
    setAppBatchSummary(null);
    setSelectedAppKeys([]);
    await fetchApps();
  };

  const failedAppBatchResults = (appBatchSummary?.results || []).filter((result) => result.outcome.kind === 'error');
  const hasSingleAppDeleteRunning = deletingAppIds.size > 0;
  const canStartAppBatchDelete = selectedAppKeys.length > 0 && !appBatchDeleteDialog.running && !hasSingleAppDeleteRunning;

  const columns = [
    { title: 'App Number', dataIndex: 'applicationNumber', render: (t: string) => <b>{t}</b> },
    { title: 'Region', dataIndex: 'region', render: (t: string) => <Tag>{t}</Tag> },
    { title: 'Sponsor', dataIndex: 'sponsorName' },
    { title: 'Created', dataIndex: 'createdUtc', render: formatDate },
    { title: 'Sequences', key: 'sequences', render: (_: any, r: Application) => r.sequences?.length || 0 },
    { title: 'Action', key: 'action', render: (_: any, r: Application) => (
        <Space>
          <Button
            type="primary"
            size="small"
            disabled={appBatchDeleteDialog.running}
            onClick={() => onSelectApp(r.id, `${r.applicationNumber} (${r.sponsorName})`)}
          >
            Manage App
          </Button>
          <Button
            danger
            size="small"
            icon={<Trash2 size={14} />}
            title="Delete App"
            loading={deletingAppIds.has(r.id)}
            disabled={deletingAppIds.has(r.id) || appBatchDeleteDialog.running}
            onClick={() => openDeleteAppDialog(r.id)}
          />
        </Space>
      )
    },
  ];

  return (
    <div className="bg-white p-6 rounded shadow-sm border border-gray-200">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-xl font-bold m-0 text-gray-800">Applications</h2>
        <Space>
          <Button
            danger
            icon={<Trash2 size={14} className="mr-1" />}
            disabled={!canStartAppBatchDelete}
            loading={appBatchDeleteDialog.running}
            onClick={() => {
              if (hasSingleAppDeleteRunning) {
                return;
              }
              setAppBatchDeleteDialog({ open: true, mode: 'databaseOnly', running: false });
            }}
          >
            Batch Delete
          </Button>
          <Button
            type="primary"
            icon={<Plus size={16} className="mr-1"/>}
            onClick={() => {
              form.setFieldsValue({ ectdTemplateKey: defaultTemplateKey });
              setAppModalVisible(true);
            }}
          >
            New Application
          </Button>
          <Button
            icon={<HardDrive size={16} className="mr-1"/>}
            onClick={() => {
              importForm.setFieldsValue({ ectdTemplateKey: defaultTemplateKey });
              setImportModalVisible(true);
            }}
          >
            Import Application
          </Button>
          <Button onClick={fetchApps} loading={loading}>Refresh</Button>
        </Space>
      </div>
      <Table
        loading={loading}
        columns={columns}
        dataSource={apps}
        rowKey="id"
        rowSelection={{
          selectedRowKeys: selectedAppKeys,
          onChange: (nextSelectedRowKeys) => setSelectedAppKeys(nextSelectedRowKeys.map((key) => String(key))),
          getCheckboxProps: (record: any) => ({
            disabled: appBatchDeleteDialog.running || deletingAppIds.has(String(record.id)),
          }),
        }}
        pagination={{
          pageSize: 15,
          onChange: () => setSelectedAppKeys([]),
        }}
      />

      <Modal title="Create New Application" open={appModalVisible} onOk={handleCreateApp} onCancel={() => setAppModalVisible(false)} destroyOnClose width={600}>
        <Form form={form} layout="vertical" initialValues={{ ectdTemplateKey: defaultTemplateKey }}>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="applicationNumber" label="Application Number" rules={[{ required: true }]}>
                <Input placeholder="e.g. NDA123456" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="ectdTemplateKey"
                label="eCTD Template"
                initialValue={defaultTemplateKey}
                rules={[{ required: true, message: 'Please select an eCTD template.' }]}
              >
                <Select loading={templatesLoading} options={ectdTemplateOptions} placeholder="Select an eCTD template" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="sponsorName" label="Sponsor Name" rules={[{ required: true }]}>
            <Input placeholder="e.g. Acme Pharma Ltd." />
          </Form.Item>
          <Form.Item 
            name="workingDirectoryParentPath" 
            label={
              <span className="flex items-center gap-1">
                Workspace Parent Directory
                <Tooltip title="The physical folder path on the server where this application's folder will be assembled.">
                  <Activity size={14} className="text-gray-400 cursor-help" />
                </Tooltip>
              </span>
            } 
            rules={[{ required: true, message: 'Please specify the working directory parent path.' }]}
          >
            <Input prefix={<FolderOpen size={16} className="text-gray-400" />} placeholder="e.g. C:\eCTD_Submissions" />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="Import Application"
        open={importModalVisible}
        onOk={() => { void handleImportApplication(); }}
        onCancel={() => setImportModalVisible(false)}
        okText="Import"
        cancelText="Cancel"
        confirmLoading={importingApplication}
        destroyOnClose
        width={680}
      >
        <Form form={importForm} layout="vertical" initialValues={{ ectdTemplateKey: defaultTemplateKey }}>
          <Form.Item
            name="workingDirectoryPath"
            label="Working Directory Path"
            rules={[{ required: true, message: 'Please input working directory path.' }]}
          >
            <Input prefix={<FolderOpen size={16} className="text-gray-400" />} placeholder="e.g. D:\eCTD\IND-IMPORT-1" />
          </Form.Item>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="ectdTemplateKey" label="eCTD Template" rules={[{ required: true, message: 'Please select an eCTD template.' }]}> 
                <Select loading={templatesLoading} options={ectdTemplateOptions} placeholder="Select an eCTD template" />
              </Form.Item>
            </Col>
            <Col span={16}>
              <Form.Item name="sponsorName" label="Sponsor Name" rules={[{ required: true, message: 'Please input sponsor name.' }]}>
                <Input placeholder="e.g. Demo Sponsor" />
              </Form.Item>
            </Col>
          </Row>
          <Alert
            type="info"
            showIcon
            message="The import reads sequences from the application workspace directory and parses each sequence index.xml."
          />
        </Form>
      </Modal>

      <Modal
        title="Import Result"
        open={importResultVisible}
        okText="Close"
        cancelButtonProps={{ style: { display: 'none' } }}
        onOk={() => {
          setImportResultVisible(false);
          setImportResult(null);
        }}
        onCancel={() => {
          setImportResultVisible(false);
          setImportResult(null);
        }}
        width={860}
      >
        {importResult && (
          <div className="flex flex-col gap-4">
            <Row gutter={12}>
              <Col span={8}><Card size="small"><Statistic title="Imported Sequences" value={importResult.importedSequenceCount} /></Card></Col>
              <Col span={8}><Card size="small"><Statistic title="Imported Documents" value={importResult.importedDocumentCount} /></Card></Col>
              <Col span={8}><Card size="small"><Statistic title="Imported Placements" value={importResult.importedPlacementCount} /></Card></Col>
            </Row>
            <Row gutter={12}>
              <Col span={12}><Card size="small"><Statistic title="Skipped Sequences" value={importResult.skippedSequenceCount} /></Card></Col>
              <Col span={12}><Card size="small"><Statistic title="Failed Sequences" value={importResult.failedSequenceCount} /></Card></Col>
            </Row>

            {(importResult.issues || []).length === 0 ? (
              <Alert type="success" showIcon message="Import finished without warnings or errors." />
            ) : (
              <Table
                size="small"
                pagination={{ pageSize: 8 }}
                rowKey={(_, index) => `issue-${index}`}
                dataSource={importResult.issues}
                columns={[
                  {
                    title: 'Severity',
                    dataIndex: 'severity',
                    key: 'severity',
                    width: 110,
                    render: (value: string) => <Tag color={String(value).toLowerCase() === 'error' ? 'red' : 'gold'}>{value}</Tag>,
                  },
                  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
                  { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'sequenceNumber', width: 130, render: (value?: string | null) => value || '-' },
                  { title: 'Message', dataIndex: 'message', key: 'message' },
                ]}
              />
            )}
          </div>
        )}
      </Modal>

      <Modal
        title="删除 Application"
        open={appDeleteDialog.open}
        okText="确认删除"
        cancelText="取消"
        onOk={confirmDeleteApp}
        onCancel={() => setAppDeleteDialog({ open: false, appId: null, mode: 'databaseOnly' })}
        confirmLoading={appDeleteDialog.appId !== null && deletingAppIds.has(appDeleteDialog.appId)}
      >
        <div className="flex flex-col gap-3">
          <div>
            即将删除 Application: <Tag>{appDeleteDialog.appId ?? '-'}</Tag>
          </div>
          <Radio.Group
            value={appDeleteDialog.mode}
            onChange={(event) => setAppDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {appDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              message="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除 Application"
        open={appBatchDeleteDialog.open}
        okText="确认批量删除"
        cancelText="取消"
        onOk={confirmBatchDeleteApps}
        onCancel={() => setAppBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })}
        confirmLoading={appBatchDeleteDialog.running}
        okButtonProps={{ disabled: !canStartAppBatchDelete }}
        cancelButtonProps={{ disabled: appBatchDeleteDialog.running }}
      >
        <div className="flex flex-col gap-3">
          <div>
            已选择 <Tag>{selectedAppKeys.length}</Tag> 个 Application。
          </div>
          <Radio.Group
            value={appBatchDeleteDialog.mode}
            onChange={(event) => setAppBatchDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {appBatchDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              message="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除结果"
        open={appBatchSummaryOpen}
        okText="关闭"
        cancelButtonProps={{ style: { display: 'none' } }}
        onOk={() => { void closeAppBatchSummary(); }}
        onCancel={() => { void closeAppBatchSummary(); }}
      >
        <div className="flex flex-col gap-3">
          <div>成功: <Tag color="green">{appBatchSummary?.successCount ?? 0}</Tag></div>
          <div>失败: <Tag color="red">{appBatchSummary?.failureCount ?? 0}</Tag></div>
          {failedAppBatchResults.length > 0 && (
            <div className="flex flex-col gap-2">
              {failedAppBatchResults.map((result) => (
                <Alert
                  key={result.key}
                  type="error"
                  showIcon
                  message={`${result.label}: ${result.outcome.message}`}
                />
              ))}
            </div>
          )}
        </div>
      </Modal>
    </div>
  );
};

// ==========================================
// Component: Main App Root
// ==========================================
export default function App() {
  const [route, setRoute] = useState<RouteState>({ view: 'applications' });
  const [health, setHealth] = useState<'ok' | 'error' | 'loading'>('loading');

  useEffect(() => {
    fetch('/health')
      .then(res => res.json())
      .then(data => setHealth(data.status === 'ok' ? 'ok' : 'error'))
      .catch(() => setHealth('error'));
  }, []);

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      <header className="bg-slate-900 text-white p-4 shadow-md flex justify-between items-center z-10">
        <div className="flex items-center gap-2 cursor-pointer select-none" onClick={() => setRoute({ view: 'applications' })}>
          <Activity className="text-blue-400" />
          <h1 className="text-xl font-bold m-0 tracking-wide">RATools Admin</h1>
        </div>
        <div className="flex items-center gap-2 text-sm">
          <span className="text-gray-400">API Health:</span>
          {health === 'loading' ? <Spin size="small" /> : (
            health === 'ok' ? <Tag color="success" className="m-0 border-0">Online</Tag> : <Tag color="error" className="m-0 border-0">Offline</Tag>
          )}
        </div>
      </header>

      <main className="flex-1 p-6 overflow-auto max-w-7xl w-full mx-auto">
        {route.view === 'applications' && (
          <ApplicationListView 
            onSelectApp={(id, title) => setRoute({ view: 'app_details', applicationId: id, appTitle: title })} 
          />
        )}
        {route.view === 'app_details' && route.applicationId && (
          <ApplicationDetailsView 
            appId={route.applicationId} 
            appTitle={route.appTitle!}
            onBack={() => setRoute({ view: 'applications' })}
            onOpenWorkspace={(seq) => setRoute({ ...route, view: 'workspace', sequenceNumber: seq })}
          />
        )}
        {route.view === 'workspace' && route.applicationId && route.sequenceNumber && (
          <SequenceWorkspace 
            appId={route.applicationId} 
            seqNumber={route.sequenceNumber} 
            onBack={() => setRoute({ view: 'app_details', applicationId: route.applicationId, appTitle: route.appTitle })}
          />
        )}
      </main>
    </div>
  );
}
