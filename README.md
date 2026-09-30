# PLrename

Download **PLrename.exe** from the [latest release](https://github.com/pommaibokrokna/PLrename/releases/latest) and run it on Windows. No installation or administrator access is needed. Uses the Windows .NET Framework and native Windows Forms controls.

1. Add files, add a folder, or drag files/folders onto the drop box. The box highlights when a drop is accepted; click it to browse. Dropping onto other parts of the window also works. Folders add only their direct files.
2. Choose a numbering mode and review the current/new names.
3. Click **Rename files** and confirm the preview.

The progress bar tracks preparation and renaming (or undo). The window remains responsive; file selection and settings are disabled until the batch finishes. Closing the window during a batch is blocked so the operation can finish safely.

## Assign counting sequence

Type `0101` in Start to generate `0101`, `0102`, `0103`… Type `009` to generate `009`, `010`, `011`… Leading zeros in Start set the minimum width. Numbers grow naturally if the width is exceeded (`999` → `1000`). Set Step to control the increment. Optional prefix and suffix surround the number; extensions are preserved. Example: prefix `IMG_`, start `009` produces `IMG_009.jpg`, `IMG_010.jpg`.

Choose **Sort by** to use Windows natural name order within each folder (`2` before `10`), or **Modified date (oldest first / newest first)** across all added files. Equal dates use natural name order within each folder. The preview displays each file's modified date in local time. One sequence runs across the entire list in the order shown. Remove selected rows to exclude files before renaming. Adding more files keeps the selected sort order.

## Shift existing last number

Adds Offset to the last group of digits before the extension, preserving the rest of the name and the original minimum width. Offset `1`: `0101.png` → `0102.png`, `photo_009.jpg` → `photo_010.jpg`. Negative offsets work provided results remain nonnegative. Every selected file must contain a number.

## Recovery and undo

Existing unrelated files are never overwritten. Conflicts, invalid names, missing files, and unsupported symbolic links block the batch. Renames use temporary names to handle overlapping sequences safely. If an operation fails, the app attempts to restore original names and reports any failures.

**Undo last batch** restores the latest batch during the current app session. It stops if a file is missing or its size/modification time changed. This metadata check cannot detect every possible outside modification. A second successful batch replaces the undo history.

Each operation records original, target, temporary, and current paths in `%LOCALAPPDATA%\BatchNumberRenamer\*.tsv`. Closing PLrename normally automatically deletes this session's logs and temporary journal files, including logs from undo and failed attempts. Logs from a crash or forced termination remain available for manual recovery. Earlier logs and unrelated TSV files are left alone. Temporary paths are recorded before files move; the current column can lag a move if the process is interrupted. No file contents are modified. This is not a multi-file atomic filesystem operation.

Supports ordinary Windows paths below 260 characters; filenames up to 255 characters; nonnegative counters fitting a signed 64-bit integer. Close other applications that may hold files open before renaming.

## Build from source

Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` in this folder. The included source uses the Windows .NET Framework compiler, with no downloads or external packages.

The release executable includes the PL Studio logo in the window header and works independently of the source PNG and ICO files. The logo PNG is not distributed in this repository. Source builds work without it; optionally place your own `PLstu_small.png` in this folder to embed a header logo. App.ico supplies the executable, window, and taskbar icon.
