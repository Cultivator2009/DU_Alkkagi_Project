using System;
using UnityEngine;

// The list of match rules, one MatchSettingRow per MatchSettings.Defs entry
// (built by the Alkkagi UI builders). Used by the lobby, where the host edits
// and the guest only watches, and by the local setup screen.
public class MatchSettingsPanel : MonoBehaviour
{
    public MatchSettingRow[] rows;

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
        var def = MatchSettings.Defs[(int)id];
        var index = Array.IndexOf(def.Values, settings.Get(id)) + direction;
        if (index < 0 || index >= def.Values.Length) return;

        settings.Set(id, def.Values[index]);
        Render();
        OnChanged?.Invoke(settings.Clone());
    }

    private void Render()
    {
        foreach (var row in rows)
        {
            var def = MatchSettings.Defs[(int)row.settingId];
            var value = settings.Get(row.settingId);
            var index = Array.IndexOf(def.Values, value);
            var relevant = def.IsRelevant == null || def.IsRelevant(settings);
            row.Render(def.Format(value), index > 0, index < def.Values.Length - 1, editable, relevant);
        }
    }
}
