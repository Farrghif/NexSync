import { api, newOperationId } from "./client";

export interface FolderDto {
  id: string;
  name: string;
  parentFolderId: string | null;
  createdAt: string;
  updatedAt: string;
  originDeviceId: string | null;
}

export interface FileDto {
  id: string;
  name: string;
  folderId: string | null;
  hash: string;
  size: number;
  contentType: string;
  createdAt: string;
  updatedAt: string;
  originDeviceId: string | null;
}

export interface FolderContents {
  folders: FolderDto[];
  files: FileDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

function opHeaders() {
  return { "X-Operation-Id": newOperationId() };
}

export async function getContents(parentId: string | null, page = 1, pageSize = 50): Promise<FolderContents> {
  const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
  if (parentId) params.parentId = parentId;
  const res = await api.get<FolderContents>("/folders", { params });
  return res.data;
}

export async function createFolder(name: string, parentFolderId: string | null): Promise<FolderDto> {
  const res = await api.post<FolderDto>("/folders", { name, parentFolderId }, { headers: opHeaders() });
  return res.data;
}

export async function updateFolder(id: string, name: string, parentFolderId: string | null): Promise<FolderDto> {
  const res = await api.put<FolderDto>(`/folders/${id}`, { name, parentFolderId }, { headers: opHeaders() });
  return res.data;
}

export async function deleteFolder(id: string): Promise<void> {
  await api.delete(`/folders/${id}`, { headers: opHeaders() });
}

export async function uploadFile(file: File, folderId: string | null, onProgress?: (pct: number) => void): Promise<FileDto> {
  const form = new FormData();
  form.append("file", file);
  const url = folderId ? `/files/upload?folderId=${folderId}` : "/files/upload";
  const res = await api.post<FileDto>(url, form, {
    headers: { "X-Operation-Id": newOperationId() },
    onUploadProgress: (e) => {
      if (e.total && onProgress) onProgress(Math.round((e.loaded / e.total) * 100));
    },
  });
  return res.data;
}

export async function updateFile(id: string, name: string, folderId: string | null): Promise<FileDto> {
  const res = await api.put<FileDto>(`/files/${id}`, { name, folderId }, { headers: opHeaders() });
  return res.data;
}

export async function deleteFile(id: string): Promise<void> {
  await api.delete(`/files/${id}`, { headers: opHeaders() });
}

export function downloadUrl(id: string): string {
  return `/files/${id}/download`;
}
