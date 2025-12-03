using System;
using UnityEngine;

[Serializable]
public class ManualSection
{
    [Tooltip("Optional ID for internal reference (e.g., 'getting-started').")]
    public string id;

    [Tooltip("Title shown in the left nav and at the top of the content.")]
    public string title;

    [Tooltip("Body text shown on the right. Supports TMP rich text.")]
    [TextArea(5, 20)]
    public string body;
}
