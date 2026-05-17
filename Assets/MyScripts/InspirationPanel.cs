using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// Attach to any GameObject in the CreateStory panel.
/// Wire the Inspiration button's OnClick to OnInspireButtonPressed().
/// Pre-fetches a result as soon as the panel opens so tapping the button is instant.
/// Wikipedia is tried up to 3 times per session, then falls back to curated prompts.
/// Prompts are weighted by context: user-selected tags (×3), sticker tags (×2), nearby places (×1).
/// Prompt text is appended to the content field in a tinted colour and stripped when the user taps to edit.
/// </summary>
public class InspirationPanel : MonoBehaviour
{
    [System.Serializable]
    public class TaggedPrompt
    {
        [TextArea(2, 4)]
        public string text;
        public List<string> tags = new List<string>();
    }

    [System.Serializable]
    public class StickerTagMapping
    {
        [Tooltip("Drag the sticker sprite here — the thumbnail shows exactly which sticker this entry covers")]
        public Sprite sprite;
        public List<string> tags = new List<string>();
    }

    [System.Serializable]
    public class PlaceTagMapping
    {
        [Tooltip("Google Places type string — e.g. museum, theatre, park, restaurant")]
        public string placeType;
        public string tag;
    }

    [Header("Typewriter")]
    [Tooltip("Seconds between each character")]
    public float charDelay = 0.025f;

    [Header("Prompt Styling")]
    [Tooltip("Colour used to tint prompt text in the content field. Cleared when the user taps to edit.")]
    public Color promptColor = new Color(0.55f, 0.55f, 0.75f, 1f);

    [Header("Tagged Prompts")]
    public List<TaggedPrompt> taggedPrompts = new List<TaggedPrompt>();

    // Called by Unity when "Reset" is clicked on the component in the Inspector.
    // Also used to populate prompts on first setup.
    void Reset()
    {
        charDelay        = 0.025f;
        promptColor      = new Color(0.55f, 0.55f, 0.75f, 1f);
        taggedPrompts    = BuildDefaultPrompts();
        placeTagMappings = BuildDefaultPlaceMappings();
    }

    [ContextMenu("Populate Default Prompts")]
    void PopulateDefaultPrompts()
    {
        taggedPrompts    = BuildDefaultPrompts();
        placeTagMappings = BuildDefaultPlaceMappings();
    }

    static List<PlaceTagMapping> BuildDefaultPlaceMappings() => new List<PlaceTagMapping>
    {
        // These match the Google Places type strings configured in PlacesFetcher
        new PlaceTagMapping { placeType = "museum",           tag = "history"          },
        new PlaceTagMapping { placeType = "park",             tag = "nature"           },
        new PlaceTagMapping { placeType = "library",          tag = "creative_writing" },
        new PlaceTagMapping { placeType = "cafe",             tag = "food"             },
        new PlaceTagMapping { placeType = "bar",              tag = "nightlife"        },
        new PlaceTagMapping { placeType = "book_store",       tag = "creative_writing" },
        new PlaceTagMapping { placeType = "movie_theatre",    tag = "theatre"          },
        new PlaceTagMapping { placeType = "place_of_worship", tag = "history"          },
        new PlaceTagMapping { placeType = "train_station",    tag = "history"          },
    };

    static List<TaggedPrompt> BuildDefaultPrompts() => new List<TaggedPrompt>
    {
        // ── Place & Observation ────────────────────────────────────────────
        new TaggedPrompt { text = "Describe something you can see from where you're standing right now.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "What would this street look like in 100 years?", tags = new List<string> { "creative_writing", "history" } },
        new TaggedPrompt { text = "Describe the sounds around you as if writing a film scene.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "What secret might this place be hiding?", tags = new List<string> { "secret_spots", "spooky" } },
        new TaggedPrompt { text = "Write a letter to the next person who will stand exactly where you are.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "What happened here at midnight last night?", tags = new List<string> { "spooky", "creative_writing" } },
        new TaggedPrompt { text = "You find something unexpected on the ground. What is it?", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Write the opening line of a novel set right here.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Describe this place to someone who has never left their hometown.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "If this street could talk, what would it say?", tags = new List<string> { "creative_writing", "history" } },
        new TaggedPrompt { text = "Name three things you can smell, hear, and feel right now.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Describe the light. What time of day does it feel like, even if you know the answer?", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "What is the oldest thing you can see from here?", tags = new List<string> { "history", "architecture" } },
        new TaggedPrompt { text = "What does the sky look like above you right now?", tags = new List<string> { "nature" } },
        new TaggedPrompt { text = "Write a weather report for the mood of this place.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "If this location were a character in a story, what would their personality be?", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "What would a tourist notice here that you no longer see?", tags = new List<string> { "secret_spots", "nostalgia" } },
        new TaggedPrompt { text = "Describe the ground beneath your feet in as much detail as possible.", tags = new List<string> { "nature" } },
        new TaggedPrompt { text = "What has been built over, paved over, or forgotten here?", tags = new List<string> { "history", "architecture" } },
        new TaggedPrompt { text = "Who else is nearby? Invent their story in two sentences.", tags = new List<string> { "creative_writing", "funny" } },

        // ── Memory & Personal ──────────────────────────────────────────────
        new TaggedPrompt { text = "Write about a memory this place brings up.", tags = new List<string> { "personal", "nostalgia" } },
        new TaggedPrompt { text = "How are you feeling right now, standing here?", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "What does this place mean to you and only you?", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Write about someone you're thinking about right now.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "What brought you here today, really?", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Describe this moment as if you're looking back on it in ten years.", tags = new List<string> { "personal", "nostalgia" } },
        new TaggedPrompt { text = "What were you worried about this time last year?", tags = new List<string> { "personal", "nostalgia" } },
        new TaggedPrompt { text = "Write the last thing someone said to you that you can't stop thinking about.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Describe a place from your childhood that felt like this one.", tags = new List<string> { "nostalgia", "personal" } },
        new TaggedPrompt { text = "What do you wish you'd said to someone?", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Write about a decision that led you here.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "What habit are you trying to build or break right now?", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Describe a version of yourself that no longer exists.", tags = new List<string> { "personal", "nostalgia" } },
        new TaggedPrompt { text = "What are you looking forward to?", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Write about something you recently changed your mind about.", tags = new List<string> { "personal" } },

        // ── Fiction & Imagination ──────────────────────────────────────────
        new TaggedPrompt { text = "A stranger hands you an envelope with your name on it. What's inside?", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Write the first paragraph of a ghost story set exactly here.", tags = new List<string> { "spooky", "creative_writing" } },
        new TaggedPrompt { text = "Someone has been watching this spot for years. Who are they?", tags = new List<string> { "spooky", "creative_writing" } },
        new TaggedPrompt { text = "This place holds a door that only appears at a certain time. When, and where does it lead?", tags = new List<string> { "creative_writing", "spooky" } },
        new TaggedPrompt { text = "Write a story in exactly five sentences that begins and ends here.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "A time traveller arrives here from 200 years ago. What shocks them most?", tags = new List<string> { "history", "creative_writing" } },
        new TaggedPrompt { text = "Something is buried nearby. What is it, and who left it?", tags = new List<string> { "spooky", "secret_spots" } },
        new TaggedPrompt { text = "Two people meet here by accident and it changes both their lives. What happens?", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Write about the last person to stand exactly where you are.", tags = new List<string> { "creative_writing", "history" } },
        new TaggedPrompt { text = "What crime was committed here that was never solved?", tags = new List<string> { "spooky", "history" } },
        new TaggedPrompt { text = "Describe this place as it appears in someone's recurring dream.", tags = new List<string> { "creative_writing", "spooky" } },
        new TaggedPrompt { text = "A child growing up here would have one strange belief about this spot. What is it?", tags = new List<string> { "creative_writing" } },

        // ── Short & Poetic ─────────────────────────────────────────────────
        new TaggedPrompt { text = "Write three sentences. Make the last one surprising.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Start with the words: 'Nobody knows that here...'", tags = new List<string> { "secret_spots" } },
        new TaggedPrompt { text = "Start with the words: 'I used to think...'", tags = new List<string> { "personal", "nostalgia" } },
        new TaggedPrompt { text = "Start with the words: 'The last time I was somewhere like this...'", tags = new List<string> { "nostalgia", "personal" } },
        new TaggedPrompt { text = "Write something true that sounds like fiction.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Write something fictional that sounds completely true.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Describe this place using only questions.", tags = new List<string> { "creative_writing" } },
        new TaggedPrompt { text = "Write about something ending.", tags = new List<string> { "personal" } },
        new TaggedPrompt { text = "Write about something beginning.", tags = new List<string> { "personal" } },

        // ── Art ────────────────────────────────────────────────────────────
        new TaggedPrompt { text = "Describe something visually striking about where you're standing — colour, shape, contrast.", tags = new List<string> { "art" } },
        new TaggedPrompt { text = "If a painter chose this spot as their subject, what would they focus on?", tags = new List<string> { "art", "creative_writing" } },
        new TaggedPrompt { text = "Is there street art, graffiti, or a public sculpture nearby? What does it say about this place?", tags = new List<string> { "art", "secret_spots" } },
        new TaggedPrompt { text = "Describe the colours of this moment as if naming paint shades.", tags = new List<string> { "art" } },
        new TaggedPrompt { text = "What would this scene look like as a photograph? Where would you point the camera?", tags = new List<string> { "art" } },

        // ── Music ──────────────────────────────────────────────────────────
        new TaggedPrompt { text = "What song would play if this moment were in a film?", tags = new List<string> { "music", "creative_writing" } },
        new TaggedPrompt { text = "Is there a busker, music, or sound nearby? Describe it.", tags = new List<string> { "music" } },
        new TaggedPrompt { text = "What does the rhythm of this street feel like — fast, slow, chaotic, steady?", tags = new List<string> { "music" } },
        new TaggedPrompt { text = "Write a single lyric that captures right now.", tags = new List<string> { "music", "creative_writing" } },
        new TaggedPrompt { text = "Name a song that reminds you of a place like this. Why?", tags = new List<string> { "music", "nostalgia" } },

        // ── Theatre ────────────────────────────────────────────────────────
        new TaggedPrompt { text = "Stage this location as a scene. Who enters? What do they want?", tags = new List<string> { "theatre", "creative_writing" } },
        new TaggedPrompt { text = "Write the stage directions for the moment you're in right now.", tags = new List<string> { "theatre", "creative_writing" } },
        new TaggedPrompt { text = "If this place were a theatre, what kind of stories would it stage?", tags = new List<string> { "theatre", "creative_writing" } },
        new TaggedPrompt { text = "Who is the most interesting person you can see? Write their monologue.", tags = new List<string> { "theatre", "funny" } },
        new TaggedPrompt { text = "What is the dramatic tension in this street right now?", tags = new List<string> { "theatre" } },
    };

    [Header("Context Mappings")]
    [Tooltip("Maps sticker IDs to tags for prompt weighting")]
    public List<StickerTagMapping> stickerTagMappings = new List<StickerTagMapping>();
    [Tooltip("Maps Google Places type strings to tags for prompt weighting")]
    public List<PlaceTagMapping> placeTagMappings = new List<PlaceTagMapping>();

    // ── Private ────────────────────────────────────────────────────────────
    private const int MaxWikiAttempts = 3;
    private int _wikiAttemptsUsed = 0;

    private Coroutine _activeCoroutine;
    private Coroutine _prefetchCoroutine;
    private bool _suppressInputCallback;

    private string _prefetchedBody;
    private string _prefetchedTitle;
    private List<string> _prefetchedTags = new List<string>();
    private bool _prefetchComplete;

    private int    _promptInsertIndex = -1;
    private string _promptColorHex;

    public static InspirationPanel instance;
    public int PromptInsertIndex => _promptInsertIndex;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake() { instance = this; }

    private void OnEnable()
    {
        _wikiAttemptsUsed  = 0;
        _prefetchedBody    = null;
        _prefetchedTitle   = null;
        _prefetchedTags    = new List<string>();
        _prefetchComplete  = false;
        _promptInsertIndex = -1;
        _prefetchCoroutine = StartCoroutine(Prefetch());
    }

    private void OnDisable()
    {
        if (_prefetchCoroutine != null) { StopCoroutine(_prefetchCoroutine); _prefetchCoroutine = null; }
        if (_activeCoroutine   != null) { StopCoroutine(_activeCoroutine);   _activeCoroutine   = null; }

        var contentField = CreateNewStory.instance?.content;
        if (contentField != null)
        {
            contentField.onValueChanged.RemoveListener(OnContentChanged);
            contentField.onSelect.RemoveListener(OnContentFieldSelected);
        }
    }

    // ── Button OnClick ─────────────────────────────────────────────────────

    public void OnInspireButtonPressed()
    {
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
            return;
        }

        Analytics.PromptOpened();
        _activeCoroutine = StartCoroutine(WriteInspiration());
    }

    // ── Prefetch ───────────────────────────────────────────────────────────

    private IEnumerator Prefetch()
    {
        yield return StartCoroutine(FetchResult());
        _prefetchComplete = true;
    }

    // ── Write ──────────────────────────────────────────────────────────────

    private IEnumerator WriteInspiration()
    {
        if (!_prefetchComplete)
            yield return new WaitUntil(() => _prefetchComplete);

        string body  = _prefetchedBody;
        string title = _prefetchedTitle;
        var    tags  = _prefetchedTags ?? new List<string>();

        // Kick off next prefetch immediately
        _prefetchedBody   = null;
        _prefetchedTitle  = null;
        _prefetchedTags   = new List<string>();
        _prefetchComplete = false;
        _prefetchCoroutine = StartCoroutine(Prefetch());

        if (string.IsNullOrEmpty(body)) { _activeCoroutine = null; yield break; }

        // Apply tags
        if (CreateNewStory.instance != null)
            foreach (string tagId in tags)
                if (CreateNewStory.instance.CanSelectTag(tagId))
                    CreateNewStory.instance.AddTag(tagId);

        // Fill title instantly (Wikipedia only, only if title field is empty)
        if (!string.IsNullOrEmpty(title) && CreateNewStory.instance?.title != null
            && string.IsNullOrWhiteSpace(CreateNewStory.instance.title.text))
            CreateNewStory.instance.title.text = title;

        var contentField = CreateNewStory.instance?.content;
        if (contentField == null) { _activeCoroutine = null; yield break; }

        // Strip any previous prompt before appending a new one
        StripActivePrompt(contentField);

        // Record where the prompt starts and build colour tag
        string existingText = contentField.text;
        _promptInsertIndex  = existingText.Length;
        _promptColorHex     = "#" + ColorUtility.ToHtmlStringRGB(promptColor);

        // Scroll to show where the prompt will appear
        yield return new WaitForEndOfFrame();
        if (contentField.verticalScrollbar != null)
        {
            Canvas.ForceUpdateCanvases();
            contentField.verticalScrollbar.value = 0f;
        }

        string promptSoFar = "";
        contentField.onSelect.AddListener(OnContentFieldSelected);
        contentField.onValueChanged.AddListener(OnContentChanged);

        foreach (char c in body)
        {
            promptSoFar += c;
            _suppressInputCallback = true;
            contentField.text = existingText + $"<color={_promptColorHex}>{promptSoFar}</color>";
            _suppressInputCallback = false;
            yield return new WaitForSeconds(charDelay);
        }

        contentField.onValueChanged.RemoveListener(OnContentChanged);
        // onSelect stays subscribed so tapping the field strips the prompt
        _activeCoroutine = null;
    }

    // Called when the user taps the content field while a prompt is showing
    private void OnContentFieldSelected(string value)
    {
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        Analytics.PromptUsed();

        var contentField = CreateNewStory.instance?.content;
        if (contentField != null)
        {
            StripActivePrompt(contentField);
            contentField.onSelect.RemoveListener(OnContentFieldSelected);
        }
    }

    private void StripActivePrompt(TMP_InputField contentField)
    {
        if (_promptInsertIndex < 0) return;
        _suppressInputCallback = true;
        if (contentField.text.Length > _promptInsertIndex)
            contentField.text = contentField.text.Substring(0, _promptInsertIndex);
        _suppressInputCallback = false;
        _promptInsertIndex = -1;
    }

    private void OnContentChanged(string value)
    {
        if (_suppressInputCallback) return;
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }
        var contentField = CreateNewStory.instance?.content;
        if (contentField != null)
            contentField.onValueChanged.RemoveListener(OnContentChanged);
    }

    // ── Fetch ──────────────────────────────────────────────────────────────

    private IEnumerator FetchResult()
    {
        string body  = null;
        string title = null;
        List<string> tags = new List<string>();

        if (_wikiAttemptsUsed < MaxWikiAttempts
            && GPSManager.Instance != null && GPSManager.Instance.latitude != 0f)
        {
            _wikiAttemptsUsed++;

            float lat = GPSManager.Instance.latitude;
            float lon = GPSManager.Instance.longitude;

            string geoUrl = "https://en.wikipedia.org/w/api.php" +
                            $"?action=query&list=geosearch&gscoord={lat}|{lon}" +
                            "&gsradius=1000&gslimit=1&format=json";

            using (var req = UnityWebRequest.Get(geoUrl))
            {
                req.SetRequestHeader("User-Agent", "InkJourney/1.0");
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var geo = JsonUtility.FromJson<WikiGeoResult>(req.downloadHandler.text);
                    string wikiTitle = geo?.query?.geosearch?.Length > 0
                        ? geo.query.geosearch[0].title : null;

                    if (!string.IsNullOrEmpty(wikiTitle))
                    {
                        string encoded = UnityWebRequest.EscapeURL(wikiTitle);
                        using (var sumReq = UnityWebRequest.Get(
                            $"https://en.wikipedia.org/api/rest_v1/page/summary/{encoded}"))
                        {
                            sumReq.SetRequestHeader("User-Agent", "InkJourney/1.0");
                            yield return sumReq.SendWebRequest();

                            if (sumReq.result == UnityWebRequest.Result.Success)
                            {
                                var summary = JsonUtility.FromJson<WikiSummary>(sumReq.downloadHandler.text);
                                if (!string.IsNullOrEmpty(summary?.extract) && summary.extract.Length >= 50)
                                {
                                    body  = TrimToSentences(summary.extract, 2);
                                    title = summary.title;
                                    tags  = MatchTagsFromText(body + " " + title);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Fall back to weighted curated prompt
        if (string.IsNullOrEmpty(body))
        {
            var prompt = SelectWeightedPrompt();
            if (prompt != null)
            {
                body = prompt.text;
                tags = prompt.tags ?? new List<string>();
            }
        }

        _prefetchedBody  = body;
        _prefetchedTitle = title;
        _prefetchedTags  = tags;
    }

    // ── Weighted prompt selection ──────────────────────────────────────────

    private TaggedPrompt SelectWeightedPrompt()
    {
        var prompts = (taggedPrompts != null && taggedPrompts.Count > 0)
            ? taggedPrompts : BuildDefaultPrompts();

        var contextScores = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);

        // User-selected story tags (weight 3)
        if (CreateNewStory.instance != null)
            foreach (var t in CreateNewStory.instance.SelectedTagIds)
                contextScores[t] = contextScores.TryGetValue(t, out float v) ? v + 3f : 3f;

        // Selected sticker tags (weight 2)
        Sprite currentSprite = StickerManager.instance?.GetSticker(StickerManager.CurrentPreviewStickerID);
        var stickerMap = currentSprite != null
            ? stickerTagMappings?.Find(m => m.sprite == currentSprite)
            : null;
        if (stickerMap != null)
            foreach (var t in stickerMap.tags)
                contextScores[t] = contextScores.TryGetValue(t, out float v) ? v + 2f : 2f;

        // Nearby place type tags (weight 1)
        if (MapLabelSpawner.instance != null)
            foreach (var placeType in MapLabelSpawner.instance.NearbyPlaceTypes)
            {
                var placeMap = placeTagMappings?.Find(m => m.placeType == placeType);
                if (placeMap != null)
                    contextScores[placeMap.tag] = contextScores.TryGetValue(placeMap.tag, out float v) ? v + 1f : 1f;
            }

        bool hasContext = contextScores.Count > 0;
        var scored = new List<(TaggedPrompt prompt, float score)>();

        foreach (var p in prompts)
        {
            if (p == null || string.IsNullOrEmpty(p.text)) continue;

            float score = 0f;
            if (p.tags != null)
                foreach (var t in p.tags)
                    if (contextScores.TryGetValue(t, out float w))
                        score += w;

            // Untagged prompts get a small base score so they can still surface
            if (p.tags == null || p.tags.Count == 0) score = hasContext ? 0.5f : 1f;

            scored.Add((p, score));
        }

        if (scored.Count == 0) return null;

        float total = 0f;
        foreach (var (_, s) in scored) total += s;
        if (total <= 0f) return scored[Random.Range(0, scored.Count)].prompt;

        float roll       = Random.value * total;
        float cumulative = 0f;
        foreach (var (prompt, score) in scored)
        {
            cumulative += score;
            if (roll <= cumulative) return prompt;
        }

        return scored[scored.Count - 1].prompt;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static List<string> MatchTagsFromText(string text)
    {
        var matched = new List<string>();
        if (TagManager.instance == null || string.IsNullOrEmpty(text)) return matched;

        string lower = text.ToLowerInvariant();
        foreach (var def in TagManager.instance.tags)
        {
            if (matched.Count >= 3) break;
            if (def == null || string.IsNullOrEmpty(def.tagId)) continue;

            string id      = def.tagId.ToLowerInvariant();
            string display = (def.displayName ?? def.tagId).ToLowerInvariant();

            if (ContainsWord(lower, id) || ContainsWord(lower, display))
                matched.Add(def.tagId);
        }
        return matched;
    }

    private static bool ContainsWord(string text, string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        int idx = text.IndexOf(word, System.StringComparison.Ordinal);
        if (idx < 0) return false;
        bool startOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
        bool endOk   = idx + word.Length >= text.Length || !char.IsLetterOrDigit(text[idx + word.Length]);
        return startOk && endOk;
    }

    private static string TrimToSentences(string text, int maxSentences)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '.' || text[i] == '!' || text[i] == '?')
                if (++count >= maxSentences)
                    return text.Substring(0, i + 1).Trim();
        }
        return text.Trim();
    }

    // ── JSON ───────────────────────────────────────────────────────────────

    [System.Serializable] class WikiGeoResult { public WikiGeoQuery  query;     }
    [System.Serializable] class WikiGeoQuery  { public WikiGeoPage[] geosearch; }
    [System.Serializable] class WikiGeoPage   { public string title;            }
    [System.Serializable] class WikiSummary   { public string extract; public string title; }
}
