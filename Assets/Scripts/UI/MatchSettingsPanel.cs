using System;
using UnityEngine;

// The list of match rules, one MatchSettingRow per MatchSettings.Defs entry
// (built by the Alkkagi UI builders). Used by the lobby, where the host edits
// and the guest only watches, and by the local setup screen.
public class MatchSettingsPanel : MonoBehaviour
{
    public MatchSettingRow[] rows;
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
        if (modes) settings.ApplyMode(); // switched to a ranked mode: its fixed rules snap back
        Render();
        OnChanged?.Invoke(settings.Clone());
    }

    private void Render()
    {
        foreach (var row in rows)
        {
            var def = MatchSettings.Defs[(int)row.settingId];
            var values = Values(def);
            var value = settings.Get(row.settingId);
            var index = Array.IndexOf(values, value);
            var open = values.Length > 1;
            var relevant = def.IsRelevant == null || def.IsRelevant(settings);
            row.Render(def.Format(value), index > 0, index < values.Length - 1, editable && open, relevant && open);
        }
    }

    private int[] Values(MatchSettingDef def) => modes ? settings.Allowed(def) : def.Values;
}
