using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StoryCommentsPanel : MonoBehaviour
{
    [Header("Comments List")]
    public RectTransform commentsParent;
    public GameObject commentItemPrefab;

    [Header("Counts")]
    public TextMeshProUGUI commentCountTextA;
    public TextMeshProUGUI commentCountTextB;
    public string countPrefixA = "";
    public string countPrefixB = "";

    [Header("Send")]
    public TMP_InputField commentInput;
    public Button sendButton;
    public int minimumCommentLength = 10;

    private GoogleSheetsFetcher.Entry activeEntry;
    private readonly List<GameObject> spawnedItems = new List<GameObject>();
    private RectTransform commentInputRect;
    private RectTransform commentBoxRect;
    private Vector2 lockedCommentInputSize;
    private Vector2 lockedCommentInputPos;
    private Vector2 lockedCommentBoxSize;
    private Vector2 lockedCommentBoxPos;

    private void Awake()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendComment);

        if (commentInput != null)
        {
            commentInput.onValueChanged.AddListener(_ => RefreshSendButtonState());
            commentInput.onSelect.AddListener(_ => EnforceCommentInputLayoutLock());
            commentInput.onValueChanged.AddListener(_ => EnforceCommentInputLayoutLock());
            commentInput.onDeselect.AddListener(_ => EnforceCommentInputLayoutLock());

            commentInputRect = commentInput.transform as RectTransform;
            if (commentInputRect != null)
            {
                lockedCommentInputSize = commentInputRect.sizeDelta;
                lockedCommentInputPos = commentInputRect.anchoredPosition;
            }

            if (commentInputRect != null && commentInputRect.parent != null)
            {
                commentBoxRect = commentInputRect.parent as RectTransform;
                if (commentBoxRect != null)
                {
                    lockedCommentBoxSize = commentBoxRect.sizeDelta;
                    lockedCommentBoxPos = commentBoxRect.anchoredPosition;
                }
            }
        }

        RefreshSendButtonState();
        RefreshCountTexts(0);
    }

    private void LateUpdate()
    {
        EnforceCommentInputLayoutLock();
    }

    public void SetCountDisplay(TextMeshProUGUI countText)
    {
        commentCountTextA = countText;
    }

    public void BindStory(GoogleSheetsFetcher.Entry entry)
    {
        activeEntry = entry;
        
        // Clear input from previous story
        if (commentInput != null)
            commentInput.text = string.Empty;
        
        RebuildComments();
        RefreshSendButtonState();
    }

    public void RebuildComments()
    {
        // Clear all spawned items
        foreach (GameObject go in spawnedItems)
            if (go != null) Destroy(go);
        spawnedItems.Clear();

        // Clear any existing children that may not be tracked in spawnedItems.
        if (commentsParent != null)
        {
            for (int i = commentsParent.childCount - 1; i >= 0; i--)
            {
                Transform child = commentsParent.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }

        if (activeEntry == null || commentsParent == null || commentItemPrefab == null)
        {
            RefreshCountTexts(0);
            return;
        }

        if (activeEntry.Comments == null)
            activeEntry.Comments = new List<GoogleSheetsFetcher.Entry.Comment>();

        activeEntry.Comments.Sort((a, b) => (a?.Created ?? 0L).CompareTo(b?.Created ?? 0L));

        foreach (GoogleSheetsFetcher.Entry.Comment comment in activeEntry.Comments)
        {
            if (comment == null || string.IsNullOrWhiteSpace(comment.Text))
                continue;

            GameObject go = Instantiate(commentItemPrefab, commentsParent);
            go.SetActive(true);
            StoryCommentItem item = go.GetComponent<StoryCommentItem>();
            if (item != null)
                item.Bind(activeEntry, comment, this);
            spawnedItems.Add(go);
        }

        RefreshCountTexts(spawnedItems.Count);
    }

    public void TryDeleteComment(string commentId)
    {
        if (activeEntry == null || string.IsNullOrWhiteSpace(commentId) || GoogleSheetsFetcher.instance == null)
            return;

        bool deleted = GoogleSheetsFetcher.instance.DeleteComment(activeEntry, commentId);
        if (!deleted)
            return;

        RebuildComments();
    }

    public string FormatTimeAgo(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return "just now";

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long delta = Mathf.Max(0, (int)(now - unixSeconds));

        if (delta < 60)
            return "just now";

        long minutes = delta / 60;
        long hours = minutes / 60;
        long days = hours / 24;

        if (days > 0)
        {
            long remHours = hours % 24;
            return remHours > 0 ? $"{days}d {remHours}h ago" : $"{days}d ago";
        }

        if (hours > 0)
        {
            long remMinutes = minutes % 60;
            return remMinutes > 0 ? $"{hours}h {remMinutes}m ago" : $"{hours}h ago";
        }

        return $"{minutes}m ago";
    }

    private void OnSendComment()
    {
        if (activeEntry == null || commentInput == null || GoogleSheetsFetcher.instance == null)
            return;

        string text = commentInput.text ?? string.Empty;
        if (NormalizedLength(text) < minimumCommentLength)
            return;

        bool added = GoogleSheetsFetcher.instance.AddComment(activeEntry, text);
        if (!added)
            return;

        commentInput.text = string.Empty;
        RebuildComments();
        RefreshSendButtonState();
    }

    private void RefreshSendButtonState()
    {
        if (sendButton == null)
            return;

        bool hasStory = activeEntry != null;
        int len = commentInput != null ? NormalizedLength(commentInput.text) : 0;
        bool shouldEnable = hasStory && len >= minimumCommentLength;
        
        sendButton.gameObject.SetActive(shouldEnable);
    }

    private void RefreshCountTexts(int count)
    {
        if (commentCountTextA != null)
            commentCountTextA.text = $"{countPrefixA}{count}";
        if (commentCountTextB != null)
            commentCountTextB.text = $"{countPrefixB}{count}";
    }

    private static int NormalizedLength(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? 0 : value.Trim().Length;
    }

    private void EnforceCommentInputLayoutLock()
    {
        if (commentInputRect != null)
        {
            commentInputRect.sizeDelta = lockedCommentInputSize;
            commentInputRect.anchoredPosition = lockedCommentInputPos;
        }

        if (commentBoxRect != null)
        {
            commentBoxRect.sizeDelta = lockedCommentBoxSize;
            commentBoxRect.anchoredPosition = lockedCommentBoxPos;
        }
    }
}
