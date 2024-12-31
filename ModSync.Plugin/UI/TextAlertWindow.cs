using System;
using UnityEngine;

namespace ModSync.Plugin.UI;

public class TextAlertWindow(
    string title,
    string message,
    Vector2 size,
    string continueText = "CONTINUE",
    string cancelText = "SKIP UPDATE",
    bool showTooltip = true,
    bool centerText = false
)
{
    private readonly TextAlertBox alertBox = new(title, message, showTooltip, centerText, continueText, cancelText);
    public bool Active { get; private set; }

    public void Hide() => Active = false;

    private string updatesText;
    private Action onAccept;
    private Action onDecline;

    public void Show(string updatesText, Action onAccept, Action onDecline)
    {
        this.updatesText = updatesText;
        this.onAccept = onAccept;
        this.onDecline = onDecline;
        Active = true;
    }

    public void Draw()
    {
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        GUILayout.BeginArea(new Rect((screenWidth - size.x) / 2f, (screenHeight - size.y) / 2f, size.x, size.y));
        alertBox.Draw(size, updatesText, onAccept, onDecline);
        GUILayout.EndArea();
    }
}

internal class TextAlertBox(string title, string message, bool showTooltip, bool centerText, string continueText, string cancelText) : Bordered
{
    private readonly TextAlertButton acceptButton = new(continueText, Colors.Primary, Colors.PrimaryLight, Colors.Grey, Colors.PrimaryDark);
    private readonly TextAlertButton declineButton = new(
        cancelText,
        Colors.Secondary,
        Colors.SecondaryLight,
        Colors.Grey,
        Colors.SecondaryDark,
        showTooltip ? "Enforced updates will still be downloaded." : null
    );

    private readonly TextAlertButtonTooltip updateButtonTooltip = new();

    private const int borderThickness = 2;
    private Vector2 scrollPosition = Vector2.zero;

    public void Draw(Vector2 size, string updatesText, Action onAccept, Action onDecline)
    {
        var borderRect = GUILayoutUtility.GetRect(size.x, size.y);
        DrawBorder(borderRect, borderThickness, Colors.Grey);

        Rect alertRect = new(
            borderRect.x + borderThickness,
            borderRect.y + borderThickness,
            borderRect.width - 2 * borderThickness,
            borderRect.height - 2 * borderThickness
        );

        GUI.DrawTexture(alertRect, Utility.GetTexture(Colors.Dark.SetAlpha(0.5f)), ScaleMode.StretchToFill, true, 0);

        Rect infoRect = new(alertRect.x, alertRect.y, alertRect.width, 96f);
        Rect scrollRect = new(alertRect.x, alertRect.y + 96f, alertRect.width, alertRect.height - 96f - 48f);
        Rect actionsRect = new(alertRect.x, alertRect.y + alertRect.height - 48f, alertRect.width, 48f);

        GUIStyle titleStyle = new()
        {
            alignment = TextAnchor.LowerCenter,
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Colors.White },
        };

        GUIStyle messageStyle = new()
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Colors.White },
        };

        GUIStyle scrollStyle = new()
        {
            alignment = centerText ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft,
            fontSize = 16,
            normal = { textColor = Colors.White },
            wordWrap = true,
        };

        Rect titleRect = new(infoRect.x, infoRect.y, infoRect.width, infoRect.height / 2);
        GUI.Label(titleRect, title, titleStyle);

        Rect messageRect = new(infoRect.x, infoRect.y + infoRect.height / 2f, infoRect.width, infoRect.height / 2);
        GUI.Label(messageRect, message, messageStyle);

        GUIStyle scrollbarStyle = new(GUI.skin.verticalScrollbar)
        {
            normal = { background = Utility.GetTexture(Colors.Grey.SetAlpha(0.2f)) },
            active = { background = Utility.GetTexture(Colors.Grey.SetAlpha(0.2f)) },
            hover = { background = Utility.GetTexture(Colors.Grey.SetAlpha(0.2f)) },
            focused = { background = Utility.GetTexture(Colors.Grey.SetAlpha(0.2f)) },
        };
        GUIStyle scrollbarThumbStyle = new(GUI.skin.verticalScrollbarThumb)
        {
            normal = { background = Utility.GetTexture(Colors.Primary.SetAlpha(0.66f)) },
            active = { background = Utility.GetTexture(Colors.Primary.SetAlpha(0.5f)) },
            hover = { background = Utility.GetTexture(Colors.Primary.SetAlpha(0.66f)) },
            focused = { background = Utility.GetTexture(Colors.Primary.SetAlpha(0.5f)) },
        };

        var scrollHeight = Math.Max(scrollRect.height - 32f, scrollStyle.CalcHeight(new GUIContent(updatesText), alertRect.width - 40f));
        GUI.DrawTexture(scrollRect, Utility.GetTexture(Color.black.SetAlpha(0.5f)), ScaleMode.StretchToFill, true, 0);

        var oldSkin = GUI.skin;
        GUI.skin.verticalScrollbarThumb = scrollbarThumbStyle;
        GUI.skin.label.wordWrap = true;
        scrollPosition = GUI.BeginScrollView(
            scrollRect,
            scrollPosition,
            new Rect(0f, 0f, alertRect.width, scrollHeight + 32f),
            false,
            false,
            GUIStyle.none,
            scrollbarStyle
        );
        GUI.skin = oldSkin;
        GUI.Label(new Rect(16f, 16f, alertRect.width - 56f, scrollHeight), updatesText, scrollStyle);
        GUI.EndScrollView();

        if (
            onDecline != null
            && declineButton.Draw(new Rect(actionsRect.x, actionsRect.y, onAccept == null ? actionsRect.width : actionsRect.width / 2, actionsRect.height))
        )
            onDecline();
        if (
            onAccept != null
            && acceptButton.Draw(
                new Rect(
                    actionsRect.x + (onDecline == null ? 0 : actionsRect.width / 2),
                    actionsRect.y,
                    onDecline == null ? actionsRect.width : actionsRect.width / 2,
                    actionsRect.height
                )
            )
        )
            onAccept();

        var tooltipRect = new Rect(Event.current.mousePosition.x + 2f, Event.current.mousePosition.y - 20f, 275f, 20f);
        if (showTooltip)
            updateButtonTooltip.Draw(tooltipRect, GUI.tooltip);
    }
}

internal class TextAlertButton(string text, Color normalColor, Color hoverColor, Color activeColor, Color borderColor, string tooltip = null) : Bordered
{
    private const int borderThickness = 2;
    private bool active;

    public bool Draw(Rect borderRect)
    {
        Rect buttonRect = new(
            borderRect.x + borderThickness,
            borderRect.y + borderThickness,
            borderRect.width - 2 * borderThickness,
            borderRect.height - 2 * borderThickness
        );

        var hovered = buttonRect.Contains(Event.current.mousePosition);

        if (hovered && Event.current.type == EventType.MouseDown)
            active = true;
        if (active && Event.current.type == EventType.MouseUp)
            active = false;

        var buttonColor =
            active ? activeColor
            : hovered ? hoverColor
            : normalColor;
        var textColor = active ? Colors.Dark : Colors.White;

        DrawBorder(borderRect, borderThickness, borderColor);
        GUI.DrawTexture(buttonRect, Utility.GetTexture(buttonColor), ScaleMode.StretchToFill, true, 0);

        return GUI.Button(
            buttonRect,
            new GUIContent(text, tooltip),
            new GUIStyle
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = textColor },
            }
        );
    }
}

internal class TextAlertButtonTooltip : Bordered
{
    private const int borderThickness = 1;

    public void Draw(Rect borderRect, string text)
    {
        if (text == string.Empty)
            return;

        DrawBorder(borderRect, borderThickness, Colors.Grey);

        var tooltipRect = new Rect(
            borderRect.x + borderThickness,
            borderRect.y + borderThickness,
            borderRect.width - 2 * borderThickness,
            borderRect.height - 2 * borderThickness
        );

        var labelRect = new Rect(tooltipRect.x + 4f, tooltipRect.y, tooltipRect.width - 8f, tooltipRect.height);

        GUI.DrawTexture(tooltipRect, Utility.GetTexture(Colors.Dark.SetAlpha(0.8f)), ScaleMode.StretchToFill, true, 0);
        GUI.Label(
            labelRect,
            text,
            new GUIStyle
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Colors.White },
            }
        );
    }
}
