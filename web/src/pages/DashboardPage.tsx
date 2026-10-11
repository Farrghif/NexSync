import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "../contexts/AuthContext";
import { currentDeviceId } from "../api/auth";
import {
  getContents, createFolder, updateFolder, deleteFolder,
  uploadFile, updateFile, deleteFile,
  type FolderDto, type FileDto,
} from "../api/files";
import { api } from "../api/client";

interface Crumb {
  id: string | null;
  name: string;
}

export function DashboardPage() {
  const { user, logout } = useAuth();
  const [crumbs, setCrumbs] = useState<Crumb[]>([{ id: null, name: "Root" }]);
  const [folders, setFolders] = useState<FolderDto[]>([]);
  const [files, setFiles] = useState<FileDto[]>([]);
  const [status, setStatus] = useState("");
  const [uploadPct, setUploadPct] = useState<number | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const current = crumbs[crumbs.length - 1];

  const load = useCallback(async () => {
    try {
      const data = await getContents(current.id);
      setFolders(data.folders);
      setFiles(data.files);
    } catch (e) {
      setStatus(describeError(e));
    }
  }, [current.id]);

  useEffect(() => {
    void load();
  }, [load]);

  const openFolder = (f: FolderDto) => setCrumbs((c) => [...c, { id: f.id, name: f.name }]);
  const goCrumb = (i: number) => setCrumbs((c) => c.slice(0, i + 1));

  const onCreateFolder = async () => {
    const name = prompt("Folder name:");
    if (!name) return;
    try {
      await createFolder(name.trim(), current.id);
      setStatus("");
      await load();
    } catch (e) {
      setStatus(describeError(e));
    }
  };

  const onRenameFolder = async (f: FolderDto) => {
    const name = prompt("Rename folder:", f.name);
    if (!name || name === f.name) return;
    try {
      await updateFolder(f.id, name.trim(), f.parentFolderId);
      await load();
    } catch (e) {
      setStatus(describeError(e));
    }
  };

  const onDeleteFolder = async (f: FolderDto) => {
    if (!confirm(`Delete folder "${f.name}" and everything inside?`)) return;
    try {
      await deleteFolder(f.id);
      await load();
    } catch (e) {
      setStatus(describeError(e));
    }
  };

  const onUpload = async (picked: FileList | null) => {
    if (!picked || picked.length === 0) return;
    try {
      for (const file of Array.from(picked)) {
        setUploadPct(0);
        await uploadFile(file, current.id, setUploadPct);
      }
      setUploadPct(null);
      setStatus("");
      await load();
    } catch (e) {
      setUploadPct(null);
      setStatus(describeError(e));
    }
  };

  const onRenameFile = async (f: FileDto) => {
    const name = prompt("Rename file:", f.name);
    if (!name || name === f.name) return;
    try {
      await updateFile(f.id, name.trim(), f.folderId);
      await load();
    } catch (e) {
      setStatus(describeError(e));
    }
  };

  const onDeleteFile = async (f: FileDto) => {
    if (!confirm(`Delete file "${f.name}"?`)) return;
    try {
      await deleteFile(f.id);
      await load();
    } catch (e) {
      setStatus(describeError(e));
    }
  };

  const onDownload = async (f: FileDto) => {
    try {
      const res = await api.get(`/files/${f.id}/download`, { responseType: "blob" });
      const url = URL.createObjectURL(res.data);
      const a = document.createElement("a");
      a.href = url;
      a.download = f.name;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setStatus(describeError(e));
    }
  };

  return (
    <div className="dash">
      <header>
        <strong>NexSync</strong>
        <span className="muted">{user?.email} · device {currentDeviceId()?.slice(0, 8)}…</span>
        <button onClick={() => void logout()}>Sign out</button>
      </header>
      <nav className="crumbs">
        {crumbs.map((c, i) => (
          <span key={c.id ?? "root"}>
            {i > 0 && " / "}
            <button className="link" onClick={() => goCrumb(i)}>{c.name}</button>
          </span>
        ))}
      </nav>
      <div className="toolbar">
        <button onClick={onCreateFolder}>New folder</button>
        <button onClick={() => fileInput.current?.click()}>Upload</button>
        <input ref={fileInput} type="file" multiple hidden onChange={(e) => void onUpload(e.target.files)} />
        {uploadPct !== null && <span>Uploading… {uploadPct}%</span>}
      </div>
      {status && <p className="error">{status}</p>}
      <section>
        <h3>Folders</h3>
        {folders.length === 0 && <p className="muted">No folders.</p>}
        <ul>
          {folders.map((f) => (
            <li key={f.id}>
              <button className="link" onClick={() => openFolder(f)}>📁 {f.name}</button>
              {" "}
              <button onClick={() => void onRenameFolder(f)}>Rename</button>
              {" "}
              <button onClick={() => void onDeleteFolder(f)}>Delete</button>
            </li>
          ))}
        </ul>
      </section>
      <section>
        <h3>Files</h3>
        {files.length === 0 && <p className="muted">No files.</p>}
        <ul>
          {files.map((f) => (
            <li key={f.id}>
              📄 {f.name} <span className="muted">({formatSize(f.size)})</span>
              {" "}
              <button onClick={() => void onDownload(f)}>Download</button>
              {" "}
              <button onClick={() => void onRenameFile(f)}>Rename</button>
              {" "}
              <button onClick={() => void onDeleteFile(f)}>Delete</button>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function describeError(e: unknown): string {
  if (typeof e === "object" && e !== null && "response" in e) {
    const resp = (e as { response?: { status?: number; data?: { detail?: string; message?: string; code?: string } } }).response;
    if (resp?.status === 403) return "Access denied — this device may have been revoked. Please sign in again.";
    return resp?.data?.detail ?? resp?.data?.message ?? `Request failed (${resp?.status}).`;
  }
  return "Request failed.";
}
