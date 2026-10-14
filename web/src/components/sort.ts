export type SortKey = "name" | "modified" | "created" | "size" | "type";
export type SortDir = "asc" | "desc";

export interface SortState {
  key: SortKey;
  dir: SortDir;
  foldersFirst: boolean;
}

export const DEFAULT_SORT: SortState = { key: "name", dir: "asc", foldersFirst: true };

const LABELS: Record<SortKey, string> = {
  name: "Name",
  modified: "Last modified",
  created: "Date created",
  size: "Size",
  type: "Type",
};

export function dirLabel(key: SortKey, dir: SortDir): string {
  switch (key) {
    case "name":
    case "type":
      return dir === "asc" ? "A–Z" : "Z–A";
    case "modified":
    case "created":
      return dir === "asc" ? "Oldest first" : "Newest first";
    case "size":
      return dir === "asc" ? "Smallest first" : "Largest first";
  }
}

export function sortLabel(sort: SortState): string {
  return `${LABELS[sort.key]} · ${dirLabel(sort.key, sort.dir)}`;
}

export function isValidSort(value: unknown): value is SortState {
  if (typeof value !== "object" || value === null) return false;
  const s = value as Record<string, unknown>;
  const keys: SortKey[] = ["name", "modified", "created", "size", "type"];
  return (
    keys.includes(s.key as SortKey) &&
    (s.dir === "asc" || s.dir === "desc") &&
    typeof s.foldersFirst === "boolean"
  );
}
