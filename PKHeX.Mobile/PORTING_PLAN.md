# PKHeX Android Porting Plan

## Stack
- **.NET MAUI** — cross-platform UI (Android, iOS, Windows, macOS from one codebase)
- **SkiaSharp** — replaces `System.Drawing` for 2D graphics (Phase 2)
- **PKHeX.Core** — reused entirely unchanged (1,763 files, zero platform dependencies)

## Key Facts
- ~315 WinForms files need replacing (~15% of codebase)
- ~85 files use `System.Drawing` (in WinForms + Drawing projects)
- 2 P/Invoke calls (`BitmapCursor.cs`, `SummaryPreviewer.cs`) — UI-layer only, not carried forward
- Plugin system via reflection — deferred to later version
- Settings (JSON), embedded resources, and Core test suite are all fully portable

---

## Phase 1 — Foundation ✅
**Goal:** PKHeX.Core running in Android context with a shell app.

- [x] Create `PKHeX.Mobile` MAUI project (`net10.0-android`)
- [x] Reference `PKHeX.Core` directly — no changes to Core
- [x] `IFileService` abstraction for platform file I/O (Android SAF via MAUI `FilePicker`)
- [x] Minimal save file loader: pick file → `SaveUtil.TryGetSaveFile()` → display trainer info
- [x] GitHub Actions CI: builds release APK, publishes to GitHub Releases
- [x] Validated on device: save file loads, trainer name/ID/playtime/storage display correctly

---

## Phase 2 — Drawing Abstraction
**Goal:** Replace `System.Drawing` with SkiaSharp. No changes to PKHeX.Core.

- [ ] Add `PKHeX.Drawing.Mobile` project with `SkiaSharp` + `SkiaSharp.Views.Maui` dependencies
- [ ] Port `ImageUtil.cs` and `ColorUtil.cs` (2 files) — map `Bitmap`→`SKBitmap`, `Graphics`→`SKCanvas`, `Color`→`SKColor`
- [ ] Port sprite loading (`PKHeX.Drawing.PokeSprite`) — embedded resources stay the same, decode with `SKBitmap.Decode()` instead of `ResourceManager`
- [ ] Port QR code rendering (`PKHeX.Drawing.Misc`) — `QRCoder` outputs raw pixels, render via SkiaSharp
- [ ] Update settings color serialization — store as hex strings instead of `System.Drawing.Color`

---

## Phase 3 — Core UI (Editor Forms)
**Goal:** Core editing experience. Bulk of the work.

Priority order:

- [ ] **PKM Editor** — `ContentPage` with tabs per property group (Stats, Moves, Met Data, OT/Misc)
  - `ComboBox` → `Picker`
  - `NumericUpDown` → `Entry` (numeric keyboard)
  - `PictureBox` (sprites) → `SKCanvasView`
  - `CheckBox`, `TextBox` → direct MAUI equivalents
- [ ] **Box/Party Slot Display** — `CollectionView` grid with `SKCanvasView` per slot; touch-based long-press drag
- [ ] **Save File Overview** — trainer card, Pokédex, items as additional `ContentPage`s
- [ ] **File export** — share sheet via `IFileService.ExportFileAsync()`

---

## Phase 4 — Secondary Features
- [ ] **Database/Search** (`SAV_Database`) — `CollectionView` with search/filter
- [ ] **Mystery Gift DB** (`SAV_MysteryGiftDB`) — same pattern as database
- [ ] **QR Code export** — `SKCanvasView` with rendered QR bitmap
- [ ] **Settings UI** — `ContentPage` backed by existing JSON settings model

---

## Phase 5 — Polish & Platform UX
- [ ] Touch UX — replace hover previews with tap-and-hold; context menus → bottom sheets
- [ ] Responsive layout — tablet shows PKM editor alongside box view simultaneously
- [ ] Drag and drop in box editor — `DragGestureRecognizer` / `DropGestureRecognizer`
- [ ] Dark mode — MAUI respects `AppTheme` automatically
- [ ] Plugin support — deferred; Android APK model incompatible with loose `.dll` loading

---

## Risks
| Risk | Mitigation |
|---|---|
| MAUI rendering performance for 960-slot box view | Lazy sprite rendering, `CollectionView` virtualization |
| Android file access (SAF) differs from Windows paths | All file I/O behind `IFileService` from day one |
| No direct `DataGridView` equivalent | `CollectionView` with custom `DataTemplate` |
| Plugin system incompatible with APK model | Defer to v2 |
