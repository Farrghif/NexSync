import type { ReactNode, SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement> & { size?: number };

function base({ size = 18, ...props }: IconProps, children: ReactNode) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.9}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...props}
    >
      {children}
    </svg>
  );
}

export const FolderIcon = (p: IconProps) =>
  base(p, <path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z" />);

export const FileIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M14 3v5h5" />
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8l-5-5Z" />
    </>,
  );

export const SearchIcon = (p: IconProps) =>
  base(
    p,
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="m20 20-3.2-3.2" />
    </>,
  );

export const PlusIcon = (p: IconProps) => base(p, <path d="M12 5v14M5 12h14" />);

export const UploadIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M12 16V4" />
      <path d="m7 9 5-5 5 5" />
      <path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
    </>,
  );

export const DownloadIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M12 4v12" />
      <path d="m7 11 5 5 5-5" />
      <path d="M4 19v0a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v0" />
    </>,
  );

export const PencilIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
    </>,
  );

export const TrashIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M3 6h18" />
      <path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" />
      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" />
      <path d="M10 11v6M14 11v6" />
    </>,
  );

export const GridIcon = (p: IconProps) =>
  base(
    p,
    <>
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </>,
  );

export const ListIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M8 6h13M8 12h13M8 18h13" />
      <path d="M3.5 6h.01M3.5 12h.01M3.5 18h.01" />
    </>,
  );

export const ChevronRight = (p: IconProps) => base(p, <path d="m9 6 6 6-6 6" />);

export const LogoutIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M9 21H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3" />
      <path d="M16 17l5-5-5-5" />
      <path d="M21 12H9" />
    </>,
  );

export const DeviceIcon = (p: IconProps) =>
  base(
    p,
    <>
      <rect x="3" y="4" width="18" height="12" rx="2" />
      <path d="M8 20h8M12 16v4" />
    </>,
  );

export const CheckIcon = (p: IconProps) => base(p, <path d="m4 12.5 5 5L20 6.5" />);

export const AlertIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M10.3 4.3 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z" />
      <path d="M12 9v4M12 17h.01" />
    </>,
  );

export const XIcon = (p: IconProps) =>
  base(p, <path d="M18 6 6 18M6 6l12 12" />);

export const CloudIcon = (p: IconProps) =>
  base(
    p,
    <path d="M17.5 19a4.5 4.5 0 0 0 .5-8.97A6 6 0 0 0 6.2 10.4 4 4 0 0 0 7 19h10.5Z" />,
  );

export const ShieldIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M12 3 5 6v6c0 4.5 3 7.6 7 9 4-1.4 7-4.5 7-9V6l-7-3Z" />
      <path d="m9.5 12 1.8 1.8L15 10" />
    </>,
  );

export const SunIcon = (p: IconProps) =>
  base(
    p,
    <>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </>,
  );

export const MoonIcon = (p: IconProps) =>
  base(p, <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" />);

export const FolderPlusIcon = (p: IconProps) =>
  base(
    p,
    <>
      <path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v3" />
      <path d="M3 7v10a2 2 0 0 0 2 2h6" />
      <path d="M17 14v6M14 17h6" />
    </>,
  );
