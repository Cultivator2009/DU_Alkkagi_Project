using System;
using UnityEngine;

// The list of match rules, one MatchSettingRow per MatchSettings.Defs entry
// (built by the Alkkagi UI builders). Used by the lobby, where the host edits
// and the guest only watches, and by the local setup screen.
public class MatchSettingsPanel : MonoBehaviour
{
    public MatchSettingRow[] rows;
    public RectTransform content; // the rows' parent, scrolled when they're taller than the panel
    public float rowPitch = 50;
    // The lobby's card: its mode decides which rules are open, and a rule the
    // mode fixes shows dimmed, without arrows. The local setup has no modes.
    public bool modes;

    // A fresh copy with the change applied, whenever the player steps a row.
    public event Action<MatchSettings> OnChanged;

    public MatchSettings Settings => settings.Clone();

    private MatchSettings settings = new MatchSettings();
    private bool editable;

    private void Awake()
    {
        foreach (var row in rows) row.OnStep += Step;
    }

    private void OnEnable()
    {
        Loc.OnLanguageChanged += Render;
        Render();
    }

    private void OnDisable()
    {
        Loc.OnLanguageChanged -= Render;
    }

    public void Show(MatchSettings value, bool canEdit)
    {
        settings = value.Clone();
        editable = canEdit;
        Render();
    }

    private void Step(MatchSettingId id, int direction)
    {
        if (!editable) return;
        var values = Values(MatchSettings.Defs[(int)id]);
        var index = Array.IndexOf(values, settings.Get(id)) + direction;
        if (index < 0 || index >= values.Length) return;

        settings.Set(id, values[index]);
        // Switched to a ranked mode: its fixed rules snap back; a rule the
        // new value rules out (the hexagon with four seats) moves on too.
        settings.Normalize(modes);
        Render();
        OnChanged?.Invoke(settings.Clone());
    }

    // A rule the others make moot (the placement style when pieces start
    // laid out) is left out and the rows below close up; one the mode fixes
    // shows dimmed, without arrows.
    private void Render()
    {
        var y = 0f;
        foreach (var row in rows)
        {
            var def = MatchSettings.Defs[(int)row.settingId];
            var relevant = def.IsRelevant == null || def.IsRelevant(settings);
            row.gameObject.SetActive(relevant);
            if (!relevant) continue;
            ((RectTransform)row.transform).anchoredPosition = new Vector2(0, -y);
            y += rowPitch;
            var values = Values(def);
            var value = settings.Get(row.settingId);
            var index = Array.IndexOf(values, value);
            var open = values.Length > 1;
            row.Render(def.Format(value), index > 0, index < values.Length - 1, editable && open, open);
        }
        if (content != null) content.sizeDelta = new Vector2(content.sizeDelta.x, y);
    }

    private int[] Values(MatchSettingDef def) => modes ? settings.Allowed(def) : settings.Available(def);
}
