using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StoryCommentItem : MonoBehaviour
{
    private static readonly Regex UrlRegex = new Regex("(https?://[^\\s<>\"\\)\\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Image profilePicImage;
    public TextMeshProUGUI userNameText;
    public TextMeshProUGUI timeAgoText;
    public TextMeshProUGUI commentText;
    public Button deleteButton;
    public RectTransform rebuildTargetA;
    public RectTransform rebuildTargetB;

    private StoryCommentsPanel ownerPanel;
    private string commentId;

    public void Bind(GoogleSheetsFetcher.Entry story, GoogleSheetsFetcher.Entry.Comment comment, StoryCommentsPanel panel)
    {
        ownerPanel = panel;
        commentId = comment != null ? comment.CommentId : string.Empty;

        if (commentText != null)
        {
            commentText.richText = true;
            commentText.raycastTarget = true;
            commentText.text = LinkifyUrls(comment != null ? comment.Text ?? string.Empty : string.Empty);
            commentText.ForceMeshUpdate();

            StoryContentLinkHandler handler = commentText.GetComponent<StoryContentLinkHandler>();
            if (handler == null)
                handler = commentText.gameObject.AddComponent<StoryContentLinkHandler>();
            handler.targetText = commentText;
        }

        if (timeAgoText != null)
        {
            timeAgoText.text = panel != null && comment != null ? panel.FormatTimeAgo(comment.Created) : string.Empty;
            timeAgoText.ForceMeshUpdate();
        }

        if (userNameText != null)
        {
            string display = comment != null ? comment.UserName : string.Empty;
            if (UserProfileManager.instance != null)
                display = UserProfileManager.instance.GetStoryAuthorDisplayName(comment != null ? comment.UserId : string.Empty, display);
            userNameText.text = string.IsNullOrWhiteSpace(display) ? "Anonymous" : display;
            userNameText.ForceMeshUpdate();
        }

        bool canDelete = comment != null
            && UserProfileManager.instance != null
            && UserProfileManager.instance.IsCurrentUser(comment.UserId);

        if (deleteButton != null)
        {
            deleteButton.interactable = canDelete;
            deleteButton.onClick.RemoveListener(OnDeletePressed);
            deleteButton.onClick.AddListener(OnDeletePressed);
        }

        RefreshProfilePic(comment != null ? comment.UserId : string.Empty);
    }

    private void RefreshProfilePic(string authorId)
    {
        if (profilePicImage == null)
            return;

        profilePicImage.sprite = null;
        profilePicImage.enabled = false;

        if (UserProfileManager.instance == null)
            return;

        UserProfileManager.instance.GetProfilePicIDForAuthor(authorId, picId =>
        {
            if (profilePicImage == null || UserProfileManager.instance == null)
                return;

            Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(picId);
            profilePicImage.sprite = sprite;
            profilePicImage.enabled = sprite != null;
        });
    }

    private void OnDeletePressed()
    {
        if (ownerPanel == null || string.IsNullOrWhiteSpace(commentId))
            return;

        ownerPanel.TryDeleteComment(commentId);
    }

    private static string LinkifyUrls(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Contains("<link="))
            return text;

        return UrlRegex.Replace(text, match =>
        {
            string url = match.Value;
            return $"<link=\"{url}\"><u><color=#66B3FF>{url}</color></u></link>";
        });
    }
}
