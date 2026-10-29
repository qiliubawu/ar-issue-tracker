import { useCallback, useEffect, useState } from 'react';

export interface Position {
  x: number;
  y: number;
  z: number;
}

export type Priority = 'low' | 'medium' | 'high';
export type Status = 'open' | 'in_progress' | 'resolved';

export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: Priority;
  status: Status;
  position: Position;
  createdAt: string;
  updatedAt: string;
}

/** 后端地址：可通过 .env 里的 VITE_API_BASE 覆盖 */
const API_BASE = (import.meta.env.VITE_API_BASE as string | undefined) ?? 'http://localhost:8080';

const PRIORITY_LABEL: Record<Priority, string> = {
  high: '高',
  medium: '中',
  low: '低',
};

const STATUS_LABEL: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
};

const STATUS_ORDER: Status[] = ['open', 'in_progress', 'resolved'];

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

export default function App() {
  const [issues, setIssues] = useState<Issue[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [lastSync, setLastSync] = useState<string>('');

  const fetchIssues = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE}/api/issues`, { cache: 'no-store' });
      if (!res.ok) {
        throw new Error(`HTTP ${res.status} ${res.statusText}`);
      }
      const data: unknown = await res.json();
      setIssues(Array.isArray(data) ? (data as Issue[]) : []);
      setLastSync(new Date().toLocaleTimeString());
    } catch (err) {
      // 后端不可用时给出明确提示，页面不会白屏
      setError(`服务连接异常：后端服务不可用（${API_BASE}）。请确认 Go 后端已启动。详情：${errorText(err)}`);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void fetchIssues();
  }, [fetchIssues]);

  const handleStatusChange = async (id: string, next: Status) => {
    setError(null);
    setUpdatingId(id);
    try {
      const res = await fetch(`${API_BASE}/api/issues/${id}/status`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: next }),
      });
      if (!res.ok) {
        const payload = (await res.json().catch(() => ({}))) as { error?: string };
        throw new Error(payload.error ?? `状态更新失败（HTTP ${res.status}）`);
      }
      const updated = (await res.json()) as Issue;
      setIssues((prev) => prev.map((it) => (it.id === id ? updated : it)));
      setLastSync(new Date().toLocaleTimeString());
    } catch (err) {
      setError(`状态更新失败：${errorText(err)}`);
    } finally {
      setUpdatingId(null);
    }
  };

  const counts = {
    total: issues.length,
    open: issues.filter((i) => i.status === 'open').length,
    in_progress: issues.filter((i) => i.status === 'in_progress').length,
    resolved: issues.filter((i) => i.status === 'resolved').length,
  };

  return (
    <div className="page">
      <header className="topbar">
        <div>
          <h1>园区 AR 巡检问题管理端</h1>
          <p className="subtitle">
            Unity AR 现场标记上报 · Go 后端持久化 · React 查看与流转
            {lastSync && <span className="sync">最近同步 {lastSync}</span>}
          </p>
        </div>
        <button className="btn-primary" onClick={() => void fetchIssues()} disabled={loading}>
          {loading ? '刷新中…' : '刷新列表'}
        </button>
      </header>

      {error && (
        <div className="alert" role="alert">
          <div>
            <strong>错误提示：</strong>
            {error}
          </div>
          <button className="btn-ghost" onClick={() => void fetchIssues()}>
            重试
          </button>
        </div>
      )}

      <section className="stats">
        <div className="stat">
          <span className="stat-num">{counts.total}</span>
          <span className="stat-label">问题总数</span>
        </div>
        <div className="stat">
          <span className="stat-num status-open">{counts.open}</span>
          <span className="stat-label">待处理</span>
        </div>
        <div className="stat">
          <span className="stat-num status-in_progress">{counts.in_progress}</span>
          <span className="stat-label">处理中</span>
        </div>
        <div className="stat">
          <span className="stat-num status-resolved">{counts.resolved}</span>
          <span className="stat-label">已解决</span>
        </div>
      </section>

      <section className="card">
        {loading && issues.length === 0 ? (
          <p className="hint">正在加载数据…</p>
        ) : issues.length === 0 ? (
          <div className="empty">
            <p className="hint">暂无上报问题</p>
            <p className="hint small">请在手机 AR 端对准水平面放置标记并提交问题，然后点“刷新列表”。</p>
          </div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>标题</th>
                  <th>描述</th>
                  <th>优先级</th>
                  <th>AR 坐标 (x, y, z)</th>
                  <th>状态</th>
                  <th>更新时间</th>
                </tr>
              </thead>
              <tbody>
                {issues.map((issue) => (
                  <tr key={issue.id}>
                    <td className="title-cell">{issue.title}</td>
                    <td className="desc-cell">{issue.description || '—'}</td>
                    <td>
                      <span className={`pill pri-${issue.priority}`}>{PRIORITY_LABEL[issue.priority]}</span>
                    </td>
                    <td className="coord">
                      ({issue.position.x.toFixed(2)}, {issue.position.y.toFixed(2)},{' '}
                      {issue.position.z.toFixed(2)})
                    </td>
                    <td>
                      <div className="status-cell">
                        <select
                          className={`status-select status-${issue.status}`}
                          value={issue.status}
                          disabled={updatingId === issue.id}
                          onChange={(e) => void handleStatusChange(issue.id, e.target.value as Status)}
                        >
                          {STATUS_ORDER.map((s) => (
                            <option key={s} value={s}>
                              {STATUS_LABEL[s]}（{s}）
                            </option>
                          ))}
                        </select>
                        {updatingId === issue.id && <span className="saving">保存中…</span>}
                      </div>
                    </td>
                    <td className="time">{new Date(issue.updatedAt).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <footer className="foot">
        接口地址 <code>{API_BASE}</code> · 数据来源 Go 后端 <code>/api/issues</code>
      </footer>
    </div>
  );
}
