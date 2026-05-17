# InkJourney Changelog

## 2026-05-14 — New creation flow wiring (session start)

### CreateNewStory.cs
- Added `public GameObject screen1Canvas` field (assign in Inspector)
- Added `StartNewStory()` public method — entry point for new story creation
  - Sets `isEditMode = false`
  - Resets entry to fresh `GoogleSheetsFetcher.Entry`
  - Captures current GPS via `UpdateEntryLocation()`
  - Resets tags/privacy via `ResetTagSelection()`
  - Activates `screen1Canvas`
- "Leave a Note" button OnClick should be wired to `StartNewStory()` in Inspector
  - Previously called `OpenContentEditor()` directly — that was wrong, remove it

### CreateNewStory.cs (Screen 3 — Review page)
- Added `screen2Canvas`, `screen3Canvas` fields (assign in Inspector)
- Added `reviewContentText` (TMP_Text) and `reviewPhoto` (RawImage) fields (assign in Inspector)
- Added `ShowReviewScreen()` — hides Screen 2, shows Screen 3, populates review content and photo
  - Wire Screen 2's "Review" button OnClick to `CreateNewStory.ShowReviewScreen()`
- Added `RefreshReviewContent()` — copies content InputField text into reviewContentText
- Added `RefreshReviewPhoto()` — syncs reviewPhoto from photoManager.CapturedPhoto; auto-called via `onPhotoChanged`
- `OpenContentEditor()` onComplete now calls `RefreshReviewContent()` — review text updates immediately after native editor closes
- Photo edits on Screen 3: wire "Edit Photo" button to `photoManager.OpenCamera()` — onPhotoChanged handles the rest

### StoryPhotoManager.cs
- Capture crop changed from live feed display ratio to fixed 2:1 landscape aspect (2f)
- Camera viewfinder is unchanged — crop happens post-capture only
- `CropToAspect()` method unchanged, just different ratio passed in (was `feedW/feedH`, now `2f`)

### UI_StoryPanel.cs
- Added `public GameObject editButton` field (assign in Inspector — button only visible to story owner)
- `BindStoryEntry()` now shows/hides `editButton` based on whether `BoundEntry.User == UserProfileManager.instance.UserId`
- Added `EditBoundStory()` public method — closes the story panel and opens `CreateNewStory.LoadForEdit(BoundEntry)`
  - Wire the edit button's OnClick to this method in Inspector
