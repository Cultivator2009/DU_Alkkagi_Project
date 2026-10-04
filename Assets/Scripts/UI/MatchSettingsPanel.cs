using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The list of match rules (built by the Alkkagi UI builders): MatchSettings.
// Groups in order, each under its heading. Used by the lobby, where the host
// edits and the guest only watches, and by the local setup screen.
// The pieces' count is one row for every side, opened out side by side on
// request (and whenever the sides differ). A rule that another moves (four
// seats rule out the hexagon) says so in a notice at the list's foot.
public class MatchSettingsPanel : MonoBehaviour
{
    public MatchSettingRow[] rows;   // in display order, each group's together
    public RectTransform[] headers;  // by group (MatchSettings.Groups)
    public RectTransform content;    // the rows' parent, scrolled when they're taller than the panel
    public float rowPitch = 50;
    public float headerPitch = 36;
    // The lobby's card: its mode decides which rules are open, and a rule the
    // mode fixes shows dimmed, without arrows. The local setup has no modes.
    public bool modes;
    public Button perSideButton;     // on the shared count's row: open it out by side, or make them all one
    public TMP_Text perSideText;
    public CanvasGroup notice;       // a moment's word on a rule another moved
    public TMP_Text noticeText;
    public float noticeSeconds = 3f;

    // A fresh copy with the change applied, whenever the player steps a row.
    public event Action<MatchSettings> OnChanged;

    public MatchSettings Settings => settings.Clone();

    private MatchSettings settings = new MatchSettings();
    private bool editable;
    private bool perSide;            // the counts opened out by the player
    private float noticeUntil = -1;

    private void Awake()
    {
        foreach (var row in rows)
        {
            var stepped = row;
            row.OnStep += (_, direction) => Step(stepped, direction);
        }
        if (perSideButton != null) perSideButton.onClick.AddListener(TogglePerSide);
        if (notice != null) notice.alpha = 0;
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

    private void Update()
    {
        if (notice == null) return;
        notice.alpha = Mathf.Clamp01((noticeUntil - Time.unscaledTime) / 0.3f);
    }

    public void Show(MatchSettings value, bool canEdit)
    {
        settings = value.Clone();
        editable = canEdit;
        Render();
    }

    private void Step(MatchSettingRow row, int direction)
    {
        if (!editable) return;
        var values = Values(MatchSettings.Defs[(int)row.settingId]);
        var index = Array.IndexOf(values, settings.Get(row.settingId)) + direction;
        if (index < 0 || index >= values.Length) return;

        if (row.allSides)
            foreach (var id in MatchSettings.StoneIds) settings.Set(id, values[index]);
        else settings.Set(row.settingId, values[index]);
        Settle();
    }

    // Open the counts out by side; or, open, make every side's the first's.
    private void TogglePerSide()
    {
        if (!editable) return;
        if (perSide || SidesDiffer)
        {
            perSide = false;
            foreach (var id in MatchSettings.StoneIds) settings.Set(id, settings.Get(MatchSettings.StoneIds[0]));
            Settle();
            return;
        }
        perSide = true;
        Render();
    }

    // The rules the player's change leaves out of place follow it (switched
    // to a mode that fixes rules, they snap back; four seats rule out the
    // hexagon), and the notice says which.
    private void Settle()
    {
        var asSet = settings.Clone();
        settings.Normalize(modes);
        Notice(asSet);
        Render();
        OnChanged?.Invoke(settings.Clone());
    }

    private void Notice(MatchSettings asSet)
    {
        if (noticeText == null) return;
        var moved = MatchSettings.Defs.Where(def => asSet.Get(def.Id) != settings.Get(def.Id) && (def.IsRelevant == null || def.IsRelevant(settings))).ToList();
        if (moved.Count == 0) return;
        var first = moved[0];
        var text = Loc.Get("rules.change", Label(first.Id), first.Format(asSet.Get(first.Id)), first.Format(settings.Get(first.Id)));
        if (moved.Count > 1) text += Loc.Get("rules.andMore", moved.Count - 1);
        noticeText.text = Loc.Get("rules.autoChanged", text);
        noticeUntil = Time.unscaledTime + noticeSeconds;
    }

    // The sides playing (as many as the seats) with different counts.
    private bool SidesDiffer => MatchSettings.StoneIds.Take(settings.Seats).Select(settings.Get).Distinct().Count() > 1;

    private string Label(MatchSettingId id)
    {
        var side = Array.IndexOf(MatchSettings.StoneIds, id);
        return side >= 0 ? Loc.Get("match.stonesOf", SideStyle.Name(side, settings.PieceType)) : Loc.Get(MatchSettings.Defs[(int)id].LabelKey);
    }

    // A rule the others make moot (the placement style when pieces start
    // laid out) is left out, and its heading with it if it was the last;
    // the rest close up. One the mode fixes shows dimmed, without arrows.
    private void Render()
    {
        var opened = perSide || SidesDiffer;
        var shown = rows.Select(row => Shows(row, opened)).ToArray();
        var y = 0f;
        var group = -1;
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.group != group)
            {
                group = row.group;
                var heading = headers[group];
                var any = rows.Where((r, k) => r.group == row.group && shown[k]).Any();
                heading.gameObject.SetActive(any);
                if (any)
                {
                    heading.anchoredPosition = new Vector2(0, -y);
                    y += headerPitch;
                }
            }
            row.gameObject.SetActive(shown[i]);
            if (!shown[i]) continue;
            ((RectTransform)row.transform).anchoredPosition = new Vector2(0, -y);
            y += rowPitch;
            var def = MatchSettings.Defs[(int)row.settingId];
            var values = Values(def);
            var value = settings.Get(row.settingId);
            var index = Array.IndexOf(values, value);
            var open = values.Length > 1;
            if (row.labelText != null) row.labelText.text = Label(row.settingId);
            var text = row.allSides && SidesDiffer ? Loc.Get("rules.perSideValue") : def.Format(value);
            row.Render(text, index > 0, index < values.Length - 1, editable && open, open);
            if (row.allSides && perSideButton != null)
            {
                perSideButton.gameObject.SetActive(editable && open);
                perSideText.text = Loc.Get(opened ? "rules.allSame" : "rules.perSide");
            }
        }
        if (content != null) content.sizeDelta = new Vector2(content.sizeDelta.x, y);
    }

    // The shared count always; a side's own only opened out.
    private bool Shows(MatchSettingRow row, bool opened)
    {
        var def = MatchSettings.Defs[(int)row.settingId];
        if (def.IsRelevant != null && !def.IsRelevant(settings)) return false;
        return row.allSides || Array.IndexOf(MatchSettings.StoneIds, row.settingId) < 0 || opened;
    }

    private int[] Values(MatchSettingDef def) => modes ? settings.Allowed(def) : settings.Available(def);
}
