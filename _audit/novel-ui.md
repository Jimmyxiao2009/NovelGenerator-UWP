# Novel Generator — UI Shell Audit (unit: novel-ui)

Scope: `Novel Generator/Pages`, `App.xaml(.cs)`, `Common`, `Themes`, `Package.appxmanifest`.
Focus: navigation bugs, missing loading/error states during generation, settings that do nothing, binding errors.

Findings ordered most-impactful first.

---

## [HIGH][BUG] System/hardware Back button is never handled — OS back exits the app instead of navigating

Files: `App.xaml.cs` (whole file); every page only wires an in-page `OnBackClick` `Button` (e.g. `Pages/Generation/ChapterGenerationPage.xaml.cs:239`, `Pages/Skeleton/SkeletonPage.xaml.cs:181`).

Evidence: A repo-wide search for `SystemNavigationManager`, `SystemNavigationManager.BackRequested`, and `HardwareButtons.BackPressed` returns **no matches**. This is a multi-page `Frame`-based UWP app (min target 10.0.10240, both Mobile and Desktop families per `Package.appxmanifest:11-12`). With no `BackRequested`/`BackPressed` subscription, pressing the phone hardware Back button or the desktop title-bar back chevron does not call `Frame.GoBack()` — on Mobile it suspends/closes the app, on Desktop the back chevron never even appears. Users mid-generation (e.g. on `BatchGenerationPage`) who press hardware Back are kicked out of the app and lose their place.

Proposed fix: In `App.OnLaunched`, after creating `rootFrame`, register a single global handler:
```csharp
var nav = SystemNavigationManager.GetForCurrentView();
rootFrame.Navigated += (s, a) =>
    nav.AppViewBackButtonVisibility = rootFrame.CanGoBack
        ? AppViewBackButtonVisibility.Visible
        : AppViewBackButtonVisibility.Collapsed;
nav.BackRequested += (s, a) =>
{
    if (rootFrame.CanGoBack && !a.Handled) { a.Handled = true; rootFrame.GoBack(); }
};
```
(Mobile hardware-button support is automatically routed to `BackRequested` on 10240+, so this one handler covers both families.) Existing in-page back Buttons can stay.
Confidence: high
Effort: S

---

## [MEDIUM][FEATURE] Dead settings: ChapterReviewMode, DefaultTargetWords, IsEnglish/S(), OobeCompleted are written/defined but never consumed

Files: `Core/AppSettings.cs:25-26` (`S`, `IsEnglish`), `:41-45` (`ChapterReviewMode`), `:47-51` (`DefaultTargetWords`), `:72-76` (`OobeCompleted`).

Evidence: Grepping the entire project (excluding `bin`/`obj`) shows these members are referenced **only inside `AppSettings.cs` itself** — no page reads or writes them:
- `ChapterReviewMode` (逐章 vs 批量审查) — never read; the per-chapter vs batch path is chosen by which page navigates, not by this setting.
- `DefaultTargetWords` — never read; `NewNovelPage` uses its own `TargetWordsBox` default `2500` (`Pages/Wizard/NewNovelPage.xaml:68`) instead.
- `IsEnglish` + `S(zh,en)` localization helper — never called; all UI strings are hardcoded Chinese, so the EN/locale switch is inert.
- `OobeCompleted` — never read; there is no OOBE/first-run flow that gates on it.

Proposed fix: Either wire them up or delete them. Minimal honest fix: remove `ChapterReviewMode`, `DefaultTargetWords`, `IsEnglish`, `S`, and `OobeCompleted` from `AppSettings` (and the stored keys) so the API surface reflects reality. If localization is intended later, keep `IsEnglish`/`S` but file a follow-up; otherwise its presence implies a feature that does not exist. Lowest-risk subset: at minimum drop `DefaultTargetWords` and `ChapterReviewMode` (the two that look like user-tunable generation knobs but silently do nothing).
Confidence: high
Effort: S

---

## [MEDIUM][BUG] BatchGenerationPage: after a generation failure the action button stays labelled "取消生成" with dark style, leaving no clear recovery

Files: `Pages/Generation/BatchGenerationPage.xaml.cs:264-277` (catch/finally of `StartBatch`), `:376-391` (`OnCancelClick`).

Evidence: On the success path `StartBatch` repurposes the bottom button: sets `CancelBtn.Content = "返回目录 →"` and an accent background, then auto-navigates (`:254-262`). On the `catch (Exception ex)` path (`:268-271`) it only sets `StatusText.Text = "✗ " + ex.Message`; the button is left reading "取消生成" with the dark `#BB333333` background. `_isRunning` is now false, so a subsequent tap on `OnCancelClick` skips the cancel branch and just does `Frame.GoBack()` — the label "取消生成" no longer matches its behavior, and there is no "重试" affordance. The user sees an error but the only button still says "Cancel generation".

Proposed fix: In the `catch (Exception)` and `catch (OperationCanceledException)` blocks of `StartBatch`, update the button to a recovery state, e.g. `CancelBtn.Content = "返回目录 →"` (or "重试") and restore an accent background, mirroring the success path. If offering retry, point `OnCancelClick`/a retry handler at re-invoking `StartBatch()` when not `_isRunning`. At minimum relabel to "返回" on failure so the button text matches the GoBack behavior.
Confidence: high
Effort: S

---

## [MEDIUM][UX] ComposePage shows no empty/guidance state and no parameter-resume; blank page if entered without an active pipeline

Files: `Pages/Generation/ComposePage.xaml.cs:22-28`, `Pages/Generation/ComposePage.xaml` (no empty hint).

Evidence: `ComposePage.OnNavigatedTo` only handles `PipelineHost.Current`; if null it silently `Frame.GoBack()` (`:26`). Unlike `DirectoryPage`/`WorldSettingPage`/`SkeletonEditPage`/`WorldEditPage`, it does **not** fall back to `e.Parameter is string nid` → `PipelineHost.ResumeAsync(nid)`, even though navigations into it pass an id is not done (it's reached only with `Current` set today). More user-visible: before the user taps 开始合并, `ResultPanel` is empty and `StatusText` is blank — there is no "点『开始合并』生成简介/目录" prompt, so a fresh page looks broken/empty.

Proposed fix: (1) Add an initial hint in `OnNavigatedTo` when `Composition == null`, e.g. `StatusText.Text = "点『开始合并』生成简介与目录。";`. (2) For consistency/robustness add the same parameter-resume fallback the sibling pages use:
```csharp
_pipeline = PipelineHost.Current;
if (_pipeline == null && e.Parameter is string nid) { var _ = ResumeAndRender(nid); return; }
```
Confidence: medium
Effort: S

---

## [MEDIUM][BUG] Inconsistent pipeline-resume across pages: SkeletonPage / ReviewGate1Page / ChapterGenerationPage / BatchGenerationPage / ExportPage / ComposePage bounce out if PipelineHost.Current is null

Files: `Pages/Skeleton/SkeletonPage.xaml.cs:27-31`, `Pages/ReviewGate1Page.xaml.cs:33-38`, `Pages/Generation/ChapterGenerationPage.xaml.cs:33-34`, `Pages/Generation/BatchGenerationPage.xaml.cs:399-400`, `Pages/Generation/ExportPage.xaml.cs:26-27`, `Pages/Generation/ComposePage.xaml.cs:26`. Compare with the pages that DO resume: `Pages/Generation/DirectoryPage.xaml.cs:36-40`, `Pages/WorldView/WorldSettingPage.xaml.cs:27-33`, the two Edit pages.

Evidence: `PipelineHost.Current` is an in-memory static (`Pipeline/PipelineHost.cs:13`) that is null after a cold start / process restart. Several inter-page navigations pass `_pipeline.Novel.Id` as a string parameter (e.g. `ReviewGate1Page.OnAcceptClick` → `BatchGenerationPage` with id, `Pages/ReviewGate1Page.xaml.cs:166`; `SkeletonPage.OnNextClick` → `ReviewGate1Page` with **no** id, `Pages/Skeleton/SkeletonPage.xaml.cs:188`), but the listed target pages ignore `e.Parameter` and just `Frame.GoBack()` when `Current` is null. Today the library-entry path (`MainPage.NavigateToNovelAsync`) calls `ResumeAsync` before navigating, so `Current` is usually set — but any path that reaches these pages without `Current` (future deep-link/tile activation, or back-stack replay after the static is cleared) dead-ends to GoBack. The codebase is internally inconsistent: half the pages resume-from-parameter, half don't.

Proposed fix: Give the six listed pages the same fallback the sibling pages already use (resume from `e.Parameter as string` via `PipelineHost.ResumeAsync` before falling back to GoBack). Make `SkeletonPage.OnNextClick` and any other navigations that omit the id pass `_pipeline.Novel.Id` so the target can resume. This makes navigation robust and uniform.
Confidence: medium
Effort: M

---

## [MEDIUM][FEATURE] SuspensionManager session-restore is a non-functional stub — it persists only BackStackDepth and never restores the navigation stack

Files: `Common/SuspensionManager.cs:57-60` (`SaveFrameState` stores only `frame.BackStackDepth`), `:42-55` (`RestoreAsync` reloads the dict but never applies it), `App.xaml.cs:38-45` (registers frame + calls `RestoreAsync` on `Terminated`).

Evidence: `App.OnLaunched` does `await SuspensionManager.RestoreAsync()` when `e.PreviousExecutionState == ApplicationExecutionState.Terminated` (`App.xaml.cs:44-45`), implying session restore. But `SaveFrameState` only writes an integer `frame.BackStackDepth` into `SessionState[key]`, and `RestoreAsync` merely deserializes the dictionary back — nothing ever reads that depth or rehydrates `frame.SetNavigationState(...)`. So after a terminate-and-relaunch the app always lands on `MainPage` regardless of where the user was; the whole suspend/restore machinery is dead code that looks implemented.

Proposed fix: Implement real frame state capture using `Frame.GetNavigationState()`/`SetNavigationState(string)`:
- In `SaveFrameState`: `SessionState[key] = frame.GetNavigationState();`
- In a new `RestoreFrameStateAsync`/inside `RegisterFrame` after restore: if `SessionState.TryGetValue(key, out var s) && s is string str` then `frame.SetNavigationState(str)`.
Note: page Types must be serializable by the navigation-state mechanism and parameters must be primitives — pages currently pass `Novel.Id` (string), which is fine. Alternatively, if session restore is not wanted, delete the registration + `RestoreAsync` call so the dead machinery doesn't mislead.
Confidence: high
Effort: M

---

## [LOW][BUG] ChapterGenerationPage.OnGenerateClick does not disable the button at start; a fast double-tap before _cts is assigned can start two generations

Files: `Pages/Generation/ChapterGenerationPage.xaml.cs:140-185`.

Evidence: The re-entry guard is `if (_cts != null) { _cts.Cancel(); return; }` (`:143`). `_cts` is only assigned on `:144` after that check. On the very first click `_cts` is null; `GenerateBtn.IsEnabled` is never set false (only `Label` changes, `:145`). Two clicks dispatched before line 144 runs both pass the `_cts == null` check and both call `_pipeline.GenerateAndPolishChapterAsync`. The other action handlers (`OnAcceptClick` uses `_isSubmitting`, `:195`) guard against this; `OnGenerateClick` does not.

Proposed fix: Add a reentrancy flag set synchronously at the top, e.g.:
```csharp
if (_cts != null) { _cts.Cancel(); return; }
_cts = new CancellationTokenSource();   // assign BEFORE any await
```
Assign `_cts` (or a `bool _generating`) as the first statement so the second tap sees it. Optionally also `GenerateBtn.IsEnabled = false` until the UI reflects the "取消" state.
Confidence: medium
Effort: S

---

## [LOW][BUG] OneWay x:Bind bindings on Novel/ApiProfile never update — model types have no INotifyPropertyChanged

Files: `Pages/Library/MainPage.xaml:38-41,80-81,119-120` (`{x:Bind Title, Mode=OneWay}`, `StateLabel`, `Genre`, `UserPrompt`), model `Models/Novel.cs:9-84` (plain `[DataContract]` class, no `INotifyPropertyChanged`; `StateLabel` is a computed getter).

Evidence: `Novel` exposes plain auto-properties and a computed `StateLabel` with no change notification. The MainPage item templates request `Mode=OneWay`, which only does work if the source raises `PropertyChanged`. Since it never does, the OneWay mode is inert overhead and, more concretely, any in-place mutation of a `Novel` already shown in the list will not refresh its card. (It happens to look correct only because `MainPage.OnNavigatedTo` calls `Refresh()` which rebuilds the `ObservableCollection`s on return.)

Proposed fix: Pick one: (a) drop `Mode=OneWay` and use the default `OneTime` to make the intent honest and slightly cheaper (safe, since the list is rebuilt on navigation); or (b) implement `INotifyPropertyChanged` on `Novel` and raise it from `State`/`Title`/etc. setters plus `StateLabel` if you want live updates without a full `Refresh()`. Given the page already rebuilds on return, (a) is the low-risk fix.
Confidence: medium
Effort: S

---

## [LOW][UX] ApiProfilesPage list gives no indication of which profile is the default, despite a "设为默认" action

Files: `Pages/Settings/ApiProfilesPage.xaml:31-52` (item template), `Pages/Settings/ApiProfileEditPage.xaml.cs:199-210` (`OnSetDefaultClick` persists `DefaultApiProfileId`).

Evidence: Users can mark a profile as default in the edit page, and `AppSettings.DefaultApiProfileId` / `DataManager.Data.DefaultApiProfileId` are stored, but the profile list template shows only Name/Model/Url with a chevron — no "默认" badge. There is no way to see the current default from the list, making the setting's effect invisible.

Proposed fix: In `ApiProfilesPage.Refresh()` (or the template), show a "默认" badge for the profile whose `Id == DataManager.Data.DefaultApiProfileId`. Simplest: add a `TextBlock` bound via a helper/x:Bind to a computed `IsDefault` flag, or append " · 默认" to the displayed name when it matches.
Confidence: high
Effort: S

---

## [LOW][BUG] ApiProfilesPage / ApiProfileEditPage in-page Back button has no fallback and editing mutates the live list instance pre-save

Files: `Pages/Settings/ApiProfilesPage.xaml.cs:30-33` (`OnBackClick` = bare `Frame.GoBack()`), `Pages/Settings/ApiProfileEditPage.xaml.cs:131-158,199-210` (`OnTestClick`/`OnSetDefaultClick` call `SyncFromUI()` which writes into `_profile`, the same instance passed from the list).

Evidence: (1) `ApiProfilesPage.OnBackClick` calls `Frame.GoBack()` with no `else`-fallback (unlike most other pages which fall back to `Frame.Navigate(MainPage)`); if this page were ever the root it would be a dead button (today it's always pushed from MainPage, so low risk). (2) `OnProfileClick` passes the live list `ApiProfile` to the edit page (`:41-46`); `ApiProfileEditPage.OnTestClick` (`:133`) and `OnSetDefaultClick` (`:201`) call `SyncFromUI()` which mutates that shared instance even though the user has not pressed Save — pressing Back after Test/SetDefault leaves the in-memory list showing edited-but-unsaved field values.

Proposed fix: (1) Add the standard fallback to `OnBackClick`: `if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(Pages.Library.MainPage));`. (2) In `ApiProfileEditPage.OnNavigatedTo`, clone the incoming `ApiProfile` into an editing copy (as `WorldEditPage`/`SkeletonEditPage` clone their drafts) so Test/SetDefault/typing don't mutate the persisted list instance until Save.
Confidence: medium
Effort: S
