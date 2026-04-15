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
import { apiFetch } from './apiClient';
import { performDelete, type DeleteMode } from './deleteActions';

// ==========================================
// Types & Interfaces
// ==========================================
interface Application {
  id: string;
  applicationNumber: string;
  region: string;
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
  const [treeLoading, setTreeLoading] = useState(false);
  const [treeError, setTreeError] = useState<string | null>(null);
  const [ectdRoots, setEctdRoots] = useState<EctdStructureNode[]>([]);
  const [expandedKeys, setExpandedKeys] = useState<string[]>([]);
  const [selectedTreeKey, setSelectedTreeKey] = useState<string | null>(null);
  const [selectedSectionPath, setSelectedSectionPath] = useState<string | null>(null);

  const treeData = useMemo(() => {
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById);
  }, [documentsById, ectdRoots, placements]);
  
  const [form] = Form.useForm();

  useEffect(() => {
    form.setFieldsValue({ ctdSection: selectedSectionPath ?? undefined });
  }, [form, selectedSectionPath]);

  useEffect(() => {
    if (!selectedTreeKey) {
      return;
    }

    const selectedNode = findWorkspaceTreeNode(treeData, selectedTreeKey);
    if (!selectedNode) {
      setSelectedTreeKey(null);
      return;
    }

    if (selectedSectionPath !== selectedNode.sectionPath) {
      setSelectedSectionPath(selectedNode.sectionPath);
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
      const response = await apiFetch(`/api/ectd-structure?region=${encodeURIComponent(app.region || 'US')}`) as EctdStructureResponse;
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
      await Promise.all([fetchPlacements(), fetchDocuments()]);
    } catch (err: any) {
      message.error({ content: `Failed: ${err.message}`, key: 'uploading' });
    } finally {
      setLoading(false);
    }
  };

  const triggerPublish = async () => {
    setPublishing(true);
    try {
      const jobRes = await apiFetch('/api/publish-jobs', {
        method: 'POST', 
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ 
          applicationId: appId, 
          sequenceNumber: String(seqNumber).trim(),
          validationProfile: 'US-FDA-v3.3'
        })
      });
      
      const targetJobId = jobRes.id || jobRes.publishJobId;
      if (!targetJobId) throw new Error("Job created but no valid Job ID returned from the server.");

      await apiFetch('/api/publish-jobs/execute', {
        method: 'POST', 
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ publishJobId: targetJobId })
      });
      
      message.success('Publish job initiated successfully! Check History tab for results.');
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
        <Button type="primary" icon={<PlayCircle size={16} className="mr-1"/>} loading={publishing} onClick={triggerPublish}>
          Publish Sequence
        </Button>
      </div>

      <Row gutter={16}>
        <Col span={12}>
          <Card title="eCTD Structure (Drag & Drop PDFs here)" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {treeError && <Alert type="error" showIcon className="mb-3" message="Failed to load eCTD structure" description={treeError} />}
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
                  const canDrop = isSection && nodeData.canDrop;
                  const titleText = String(nodeData.title ?? '');
                  const titleMatch = isSection ? /^([0-9]+(?:\.[0-9A-Z]+)*)\s+(.+)$/.exec(titleText) : null;
                  const titlePrefix = titleMatch ? titleMatch[1] : null;
                  const titleLabel = titleMatch ? titleMatch[2] : titleText;

                  return (
                     <div
                       onDragOver={(e) => {
                         e.preventDefault();
                         e.stopPropagation();
                         e.dataTransfer.dropEffect = canDrop ? 'copy' : 'none';

                         if (canDrop) {
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
                         const files = e.dataTransfer.files;
                         if (!files || files.length === 0) {
                           return;
                         }

                         if (!canDrop) {
                           message.warning(nodeData.nodeType === 'document'
                             ? 'Drop files on a section, not a document.'
                             : 'Only leaf sections accept dropped files.');
                           return;
                         }

                         const file = files[0];
                         if (!file.name.toLowerCase().endsWith('.pdf')) { message.error('Only PDF allowed.'); return; }
                         await handleDirectDrop(file, nodeData.sectionPath);
                       }}
                       className={`ectd-tree-node ${isSection ? 'ectd-tree-node--section' : 'ectd-tree-node--document'} ${canDrop ? 'ectd-tree-node--droppable' : ''} ${isHovered ? 'ectd-tree-node--hover' : ''} ${isSelected ? 'ectd-tree-node--selected' : ''}`}
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
          <Card title="Current Mapped Documents" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {placements.length === 0 ? (
              <div className="text-center text-gray-400 mt-20">
                <FolderOpen size={48} className="mx-auto mb-4 opacity-50"/>
                <p>No documents mapped yet.</p>
                <p>Select a node on the left and drop a file.</p>
              </div>
            ) : (
              <Table 
                dataSource={placements} 
                rowKey="id" 
                size="small" 
                pagination={false}
                columns={[
                  { title: 'eCTD Node', dataIndex: 'ctdSection', key: 'node', render: (t) => <Tag>{t}</Tag> },
                  { title: 'Operation', dataIndex: 'operation', key: 'op', render: (t) => <Tag color="blue">{t}</Tag> }
                ]}
              />
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

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-4 bg-white p-4 rounded shadow-sm border border-gray-200">
        <Button icon={<ArrowLeft size={16} />} onClick={onBack}>Back to Applications</Button>
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
              <Button type="primary" icon={<Plus size={16} className="mr-1"/>} onClick={() => setSeqModalVisible(true)}>
                New Sequence
              </Button>
            </div>
            <Table 
              loading={loading}
              dataSource={appData?.sequences || []} 
              rowKey="sequenceNumber" 
              size="middle"
              columns={[
                { title: 'Sequence', dataIndex: 'sequenceNumber', render: (t) => <b>{t}</b> },
                { title: 'Submission Type', dataIndex: 'submissionType' },
                { title: 'Description', dataIndex: 'description' },
                { title: 'Actions', key: 'actions', render: (_: any, r: any) => (
                  <Space>
                    <Button type="link" size="small" onClick={() => onOpenWorkspace(r.sequenceNumber)}>
                      Enter Workspace
                    </Button>
                    <Button
                      danger
                      type="text"
                      size="small"
                      icon={<Trash2 size={14} />}
                      title="Delete Sequence"
                      loading={deletingSequenceNumbers.has(r.sequenceNumber)}
                      disabled={deletingSequenceNumbers.has(r.sequenceNumber)}
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
  const [appDeleteDialog, setAppDeleteDialog] = useState<{ open: boolean; appId: string | null; mode: DeleteMode }>({
    open: false,
    appId: null,
    mode: 'databaseOnly',
  });
  const [form] = Form.useForm();

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

  const handleCreateApp = async () => {
    try {
      const values = await form.validateFields();
      await apiFetch('/api/applications', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          applicationNumber: values.applicationNumber,
          region: values.region,
          sponsorName: values.sponsorName,
          // 提交后端的最新字段名：父工作路径
          workingDirectoryParentPath: values.workingDirectoryParentPath
        })
      });
      message.success('Application created with Workspace!');
      setAppModalVisible(false);
      form.resetFields();
      fetchApps();
    } catch (e: any) { message.error('Failed to create application: ' + e.message); }
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

  const columns = [
    { title: 'App Number', dataIndex: 'applicationNumber', render: (t: string) => <b>{t}</b> },
    { title: 'Region', dataIndex: 'region', render: (t: string) => <Tag>{t}</Tag> },
    { title: 'Sponsor', dataIndex: 'sponsorName' },
    { title: 'Created', dataIndex: 'createdUtc', render: formatDate },
    { title: 'Sequences', key: 'sequences', render: (_: any, r: Application) => r.sequences?.length || 0 },
    { title: 'Action', key: 'action', render: (_: any, r: Application) => (
        <Space>
          <Button type="primary" size="small" onClick={() => onSelectApp(r.id, `${r.applicationNumber} (${r.sponsorName})`)}>
            Manage App
          </Button>
          <Button
            danger
            size="small"
            icon={<Trash2 size={14} />}
            title="Delete App"
            loading={deletingAppIds.has(r.id)}
            disabled={deletingAppIds.has(r.id)}
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
          <Button type="primary" icon={<Plus size={16} className="mr-1"/>} onClick={() => setAppModalVisible(true)}>New Application</Button>
          <Button onClick={fetchApps} loading={loading}>Refresh</Button>
        </Space>
      </div>
      <Table loading={loading} columns={columns} dataSource={apps} rowKey="id" pagination={{ pageSize: 15 }} />

      <Modal title="Create New Application" open={appModalVisible} onOk={handleCreateApp} onCancel={() => setAppModalVisible(false)} destroyOnClose width={600}>
        <Form form={form} layout="vertical">
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="applicationNumber" label="Application Number" rules={[{ required: true }]}>
                <Input placeholder="e.g. NDA123456" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="region" label="Region" initialValue="US">
                <Select options={[{ value: 'US', label: 'US FDA' }, { value: 'EU', label: 'EMA' }]} />
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
