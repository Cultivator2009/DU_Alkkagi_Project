using System.Collections.Generic;
using UnityEngine;

// CS2-style kill feed (built by Tools > Alkkagi UI > 2. Build HUD): a line
// per piece knocked out, newest at the bottom, each fading out after a few
// seconds. Online, the lines of this screen's own shots get the red outline.
// MainGameUIController hands it every resolved shot, on the host, the guest
// and a local game alike.
public class KillFeed : MonoBehaviour
{
    public KillFeedEntry template; // inactive, inside the list's layout group
    public int maxEntries = 6;
    public float lifetime = 6f;
    public float fadeSeconds = 0.6f;
    public Color outlineColor = Color.black;
    public Color localOutlineColor = Color.red;

    private readonly List<(KillFeedEntry entry, float shownAt)> entries = new List<(KillFeedEntry, float)>();

    public void Add(IReadOnlyList<KillEvent> events, BoardSetup board, PieceType pieces, int? localPlayerId)
    {
        foreach (var kill in events)
        {
            var entry = Instantiate(template, template.transform.parent);
            Fill(entry, kill, board, pieces);
            entry.outline.color = kill.ShooterId == localPlayerId ? localOutlineColor : outlineColor;
            entry.gameObject.SetActive(true);
            entries.Add((entry, Time.time));
        }
        while (entries.Count > maxEntries) RemoveAt(0);
    }

    private void Update()
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var left = lifetime - (Time.time - entries[i].shownAt);
            if (left <= 0) RemoveAt(i);
            else entries[i].entry.canvasGroup.alpha = Mathf.Clamp01(left / fadeSeconds);
        }
    }

    private void RemoveAt(int index)
    {
        Destroy(entries[index].entry.gameObject);
        entries.RemoveAt(index);
    }

    // Kill: 흑 ● ▸ ○ 백. A suicide is only the shooter and their piece.
    private static void Fill(KillFeedEntry entry, KillEvent kill, BoardSetup board, PieceType pieces)
    {
        entry.shooterName.text = SideStyle.Name(kill.ShooterId, pieces);
        Icon(entry.shotIcon, entry.shotLetter, kill.ShooterId, kill.ShotPieceId, board, pieces);

        var suicide = kill.Kind == KillKind.Suicide;
        entry.arrow.SetActive(!suicide);
        entry.victimIcon.gameObject.SetActive(!suicide);
        entry.victimName.gameObject.SetActive(!suicide);
        if (!suicide)
        {
            Icon(entry.victimIcon, entry.victimLetter, kill.VictimOwnerId, kill.VictimId, board, pieces);
            entry.victimName.text = SideStyle.Name(kill.VictimOwnerId, pieces);
        }

        var badgeKey = kill.Kind switch
        {
            KillKind.Nongae => "kill.nongae",
            KillKind.Suicide => "kill.suicide",
            KillKind.TeamKill => "kill.teamKill",
            _ => null,
        };
        entry.badge.SetActive(badgeKey != null);
        if (badgeKey != null) entry.badgeText.text = Loc.Get(badgeKey);
    }

    // The side's stone, or for a janggi piece its wood disc with the letter.
    private static void Icon(SideMark mark, TMPro.TMP_Text letter, int playerId, char pieceId, BoardSetup board, PieceType pieces)
    {
        mark.playerId = playerId;
        mark.Show(pieces);
        var key = board.LetterKey(pieceId);
        letter.gameObject.SetActive(key != null);
        if (key == null) return;
        letter.text = SideStyle.PieceLetter(key);
        letter.color = playerId == 0 ? SideStyle.Cho : SideStyle.Han;
    }
}
