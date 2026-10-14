import { useEffect, useRef, useState, type ReactNode } from "react";
import {
  ArrowDownIcon, ArrowUpIcon, CalendarIcon, CheckIcon, ChevronDownIcon,
  ClockIcon, FolderFirstIcon, LayersIcon, SortIcon, TagIcon, TextIcon,
} from "./icons";
import { dirLabel, sortLabel, type SortDir, type SortKey, type SortState } from "./sort";

interface Option {
  key: SortKey;
  label: string;
  icon: ReactNode;
  defaultDir: SortDir;
}

const OPTIONS: Option[] = [
  { key: "name", label: "Name", icon: <TextIcon size={16} />, defaultDir: "asc" },
  { key: "modified", label: "Last modified", icon: <ClockIcon size={16} />, defaultDir: "desc" },
  { key: "created", label: "Date created", icon: <CalendarIcon size={16} />, defaultDir: "desc" },
  { key: "size", label: "Size", icon: <LayersIcon size={16} />, defaultDir: "desc" },
  { key: "type", label: "Type", icon: <TagIcon size={16} />, defaultDir: "asc" },
];

interface SortMenuProps {
  sort: SortState;
  onChange: (next: SortState) => void;
}

export function SortMenu({ sort, onChange }: SortMenuProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const pick = (option: Option) => {
    if (sort.key === option.key) {
      onChange({ ...sort, dir: sort.dir === "asc" ? "desc" : "asc" });
    } else {
      onChange({ ...sort, key: option.key, dir: option.defaultDir });
    }
  };

  return (
    <div className="sort" ref={ref}>
      <button
        type="button"
        className="btn btn-outline btn-sm sort-trigger"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <SortIcon size={16} />
        <span className="sort-label">{sortLabel(sort)}</span>
        <ChevronDownIcon size={15} className={`sort-caret${open ? " rot" : ""}`} />
      </button>
      {open && (
        <div className="menu" role="menu" aria-label="Sort options">
          <div className="menu-title">Sort by</div>
          {OPTIONS.map((option) => {
            const active = sort.key === option.key;
            return (
              <button
                key={option.key}
                type="button"
                className={`menu-row${active ? " active" : ""}`}
                role="menuitemradio"
                aria-checked={active}
                onClick={() => pick(option)}
              >
                <span className="menu-ico">{option.icon}</span>
                <span className="menu-text">{option.label}</span>
                {active && (
                  <span className="menu-dir">
                    {sort.dir === "asc" ? <ArrowUpIcon size={15} /> : <ArrowDownIcon size={15} />}
                    <span className="menu-dir-label">{dirLabel(sort.key, sort.dir)}</span>
                  </span>
                )}
              </button>
            );
          })}
          <div className="menu-sep" />
          <button
            type="button"
            className={`menu-row${sort.foldersFirst ? " active" : ""}`}
            role="menuitemcheckbox"
            aria-checked={sort.foldersFirst}
            onClick={() => onChange({ ...sort, foldersFirst: !sort.foldersFirst })}
          >
            <span className="menu-ico">
              <FolderFirstIcon size={16} />
            </span>
            <span className="menu-text">Folders first</span>
            <span className="menu-check" data-on={sort.foldersFirst}>
              <CheckIcon size={15} />
            </span>
          </button>
        </div>
      )}
    </div>
  );
}
