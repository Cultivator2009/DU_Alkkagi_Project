using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Tools > Alkkagi > Self check: what can be checked without a person.
//  - Static (seconds, any time): the rules' values, every network message
//    there and back, the strings in both languages, the glyphs the fonts
//    were baked with, the sounds the code asks for, the scenes in the build.
//  - Full (minutes): the static part, then in play mode (it enters it
//    itself, from MainMenuScene) how fast each kind of piece slows, the
//    cross board's seams, AI matches across boards, pieces and rules (each
//    must end, no turn stuck, nothing logged as an error), and a network
//    match: the host's messages, bots and all, replayed into a guest that
//    must end the same.
// A line per check and a summary in the console and in Logs/SelfCheck.txt.
// Entering play mode reloads the scripts, so the play part is picked up
// from SessionState on the other side. Every step catches its own errors:
// one that threw on every editor update would stall the editor's other
// update work (Unity-MCP among it).
[InitializeOnLoad]
internal static class SelfCheck
{
    private const string PendingKey = "Alkkagi.SelfCheck.Pending";
    private const string StaticKey = "Alkkagi.SelfCheck.Static";
    private static readonly string ReportPath = Path.Combine("Logs", "SelfCheck.txt");

    static SelfCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                new PlayRunner().Start();
            }
        };
    }

    [MenuItem("Tools/Alkkagi/Self check (static)")]
    private static void RunStatic()
    {
        var report = new Report();
        StaticChecks(report);
        report.Finish("Static");
    }

    [MenuItem("Tools/Alkkagi/Self check (full)")]
    private static void RunFull()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[SelfCheck] Stop play mode first: the full check enters it itself.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var report = new Report();
        StaticChecks(report);
        SessionState.SetString(StaticKey, report.Text);
        SessionState.SetBool(PendingKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenuScene.unity");
        EditorApplication.isPlaying = true;
    }

    // ---- The report ----

    private sealed class Report
    {
        private readonly StringBuilder text = new StringBuilder();
        public int Passed, Failed;
        public string Text => text.ToString();

        public Report(string carried = null)
        {
            if (string.IsNullOrEmpty(carried)) return;
            text.Append(carried);
            Passed = Regex.Matches(carried, @"^  ok ", RegexOptions.Multiline).Count;
            Failed = Regex.Matches(carried, @"^  FAIL ", RegexOptions.Multiline).Count;
        }

        public void Section(string name) => text.AppendLine(name);

        // Worth knowing, not a fault.
        public void Note(string line) => text.AppendLine("  note " + line);

        public void Check(string name, bool ok, string detail = "")
        {
            if (ok) Passed++;
            else Failed++;
            text.AppendLine($"  {(ok ? "ok  " : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
        }

        // Runs one check, its exception a failure rather than the end of the run.
        public void Try(string name, Func<(bool ok, string detail)> check)
        {
            try
            {
                var (ok, detail) = check();
                Check(name, ok, detail);
            }
            catch (Exception e)
            {
                Check(name, false, e.GetType().Name + ": " + e.Message);
            }
        }

        public void Finish(string kind)
        {
            var summary = $"[SelfCheck] {kind}: {Passed} passed, {Failed} failed.";
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, summary + "\n" + text);
            var message = summary + " (" + ReportPath + ")\n" + text;
            if (Failed > 0) Debug.LogError(message);
            else Debug.Log(message);
        }
    }

    // ---- Static ----

    private static void StaticChecks(Report report)
    {
        report.Section("Rules");
        report.Try("every rule's default is one of its values", () =>
        {
            var bad = MatchSettings.Defs.Where(d => Array.IndexOf(d.Values, d.Default) < 0).Select(d => d.Key).ToList();
            return (bad.Count == 0, string.Join(", ", bad));
        });
        report.Try("Normal's and Ranked's fixed values are values of the rule", () =>
        {
            var bad = new List<string>();
            foreach (var def in MatchSettings.Defs)
            foreach (var mode in new[] { MatchMode.Normal, MatchMode.Ranked })
            {
                var fixedValues = def.FixedValues(mode);
                if (fixedValues != null && fixedValues.Any(v => Array.IndexOf(def.Values, v) < 0)) bad.Add(def.Key + "/" + mode);
            }
            return (bad.Count == 0, string.Join(", ", bad));
        });
        report.Try("Normal's rules (as a lobby applies them) and Ranked's are all playable as they stand", () =>
        {
            var normal = new MatchSettings();
            normal.ApplyMode();
            var ranked = MatchSettings.ForRanked();
            var bad = new List<string>();
            foreach (var (name, rules) in new[] { ("Normal", normal), ("Ranked", ranked) })
            foreach (var def in MatchSettings.Defs)
                if (rules.Allowed(def).Length > 0 && Array.IndexOf(rules.Allowed(def), rules.Get(def.Id)) < 0) bad.Add(name + ":" + def.Key);
            return (bad.Count == 0 && ranked.Rated && !normal.Rated, bad.Count > 0 ? string.Join(", ", bad) : $"ranked rated {ranked.Rated}, normal rated {normal.Rated}");
        });
        report.Try("every seat count has a board", () =>
        {
            var boards = Enum.GetValues(typeof(BoardType)).Cast<BoardType>().Where(b => b != BoardType.Random).ToList();
            var bad = Enumerable.Range(2, 3).Where(n => !boards.Any(b => MatchSettings.BoardFits(b, n))).ToList();
            return (bad.Count == 0, string.Join(", ", bad));
        });
        report.Try("the rule lists' groups hold every rule once", () =>
        {
            var listed = MatchSettings.Groups.SelectMany(g => g.ids).ToList();
            var missing = MatchSettings.Defs.Select(d => d.Id).Where(id => !listed.Contains(id)).ToList();
            var twice = listed.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            return (missing.Count == 0 && twice.Count == 0, $"missing {string.Join(",", missing)} twice {string.Join(",", twice)}");
        });

        report.Section("Network");
        report.Try("every message goes out and comes back the same", () =>
        {
            var entries = ((IDictionary)typeof(NetProtocol).GetField("byType", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Values.Cast<NetProtocol.Entry>().ToList();
            var bad = new List<string>();
            foreach (var entry in entries)
            {
                var message = entry.Create();
                if (message is Msg.LoadGameScene load)
                {
                    load.Roster = new MatchRoster(new ulong[] { 76561198000000001UL, MatchRoster.BotId(0) }, new[] { 1200, 1000 }, new[] { Opponent.Human, Opponent.AIHard });
                    load.Series = RankedSeries.Begin(new[] { 1200, 1350 });
                }
                var once = NetProtocol.Encode(message);
                if (!NetProtocol.TryDecode(once, out var back, out var decoded) || back.Type != entry.Type || !NetProtocol.Encode(decoded).SequenceEqual(once)) bad.Add(entry.Type.ToString());
            }
            return (bad.Count == 0, $"{entries.Count} messages{(bad.Count > 0 ? ", wrong: " + string.Join(", ", bad) : "")}");
        });
        report.Try("rules, a roster with bots and a ranked series keep every value", () =>
        {
            var rules = MatchSettings.ForRanked();
            rules.Set(MatchSettingId.TurnSeconds, 60);
            var w = new NetWriter();
            rules.Write(w);
            var rulesBack = MatchSettings.Read(new NetReader(w.ToArray()));
            var rulesSame = MatchSettings.Defs.All(d => rules.Get(d.Id) == rulesBack.Get(d.Id));
            var roster = new MatchRoster(new ulong[] { 76561198000000001UL, MatchRoster.BotId(1) }, new[] { 1234, 1000 }, new[] { Opponent.Human, Opponent.AIEasy });
            w = new NetWriter();
            roster.Write(w);
            var rosterBack = MatchRoster.Read(new NetReader(w.ToArray()));
            var rosterSame = rosterBack.Count == 2 && rosterBack.SteamIds[0] == roster.SteamIds[0] && rosterBack.Ratings[0] == 1234 && rosterBack.Who(1) == Opponent.AIEasy;
            var series = RankedSeries.Begin(new[] { 1300, 1200 });
            series.Record(series.First);
            series = series.Next();
            w = new NetWriter();
            series.Write(w);
            var seriesBack = RankedSeries.Read(new NetReader(w.ToArray()));
            var seriesSame = seriesBack.Game == series.Game && seriesBack.First == series.First && seriesBack.Wins.SequenceEqual(series.Wins);
            return (rulesSame && rosterSame && seriesSame, $"rules {rulesSame}, roster {rosterSame}, series {seriesSame}");
        });

        report.Section("Strings and fonts");
        report.Try("every string has both languages", () =>
        {
            var bad = Loc.Entries.Where(e => string.IsNullOrWhiteSpace(e.ko) || string.IsNullOrWhiteSpace(e.en)).Select(e => e.key).ToList();
            return (bad.Count == 0, $"{Loc.Entries.Count()} strings{(bad.Count > 0 ? ", missing: " + string.Join(", ", bad) : "")}");
        });
        report.Try("every string key the code names exists", () =>
        {
            var missing = UsedStringKeys().Where(k => !Loc.Has(k)).Distinct().OrderBy(k => k).ToList();
            return (missing.Count == 0, string.Join(", ", missing.Take(20)));
        });
        report.Try("the fonts have every character the strings use", () =>
        {
            var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Kit/Fonts/Pretendard-Regular SDF.asset");
            var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/Kit/Fonts/Pretendard-Bold SDF.asset");
            var hanja = AssetDatabase.FindAssets("NotoSerifKR-Janggi SDF t:TMP_FontAsset").Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (regular == null || bold == null) return (false, "font assets not found");
            var chars = Loc.Entries.SelectMany(e => e.ko + e.en).Where(c => !char.IsWhiteSpace(c) && c != '{' && c != '}').Distinct().ToList();
            bool Has(TMP_FontAsset font, char c) => font.HasCharacter(c) || (hanja != null && hanja.HasCharacter(c));
            var missing = chars.Where(c => !Has(regular, c) || !Has(bold, c)).ToList();
            return (missing.Count == 0, missing.Count > 0 ? "rerun Tools > Alkkagi UI > 1: " + new string(missing.Take(30).ToArray()) : $"{chars.Count} characters");
        });

        report.Section("Sounds and scenes");
        report.Try("every sound the game plays is in the bank", () =>
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>("Assets/Resources/SoundBank.asset");
            bank.ClearCache();
            var missing = new List<string>();
            foreach (var type in Enum.GetValues(typeof(PieceType)).Cast<PieceType>().Where(t => t != PieceType.Random))
            {
                var set = PieceSet.Of(type);
                var keys = new List<string> { "hit", "wall", "shatter", "hinge", "flick", "fall", "place" };
                if (set.KnocksBoard) keys.Add("land");
                missing.AddRange(keys.Where(k => bank.Get(k, set.Sound)?.Pick() == null).Select(k => k + "_" + set.Sound));
            }
            foreach (var property in typeof(SoundBank).GetProperties().Where(p => p.PropertyType == typeof(SoundSet)))
                if (((SoundSet)property.GetValue(bank))?.Pick() == null) missing.Add(property.Name);
            return (missing.Count == 0, string.Join(", ", missing));
        });
        report.Try("the three scenes are in the build, the menu first", () =>
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToList();
            var ok = scenes.Count > 0 && scenes[0] == "MainMenuScene" && scenes.Contains("LobbyScene") && scenes.Contains("GameScene");
            return (ok, string.Join(", ", scenes));
        });
        if (Directory.Exists("Assets/Plugins/NuGet")) report.Note("Unity-MCP is installed: run unity-mcp-cli remove-plugin before a release build.");
    }

    // Keys the code names outright, and those it builds from enum names.
    private static IEnumerable<string> UsedStringKeys()
    {
        foreach (var file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/Editor/AlkkagiUI", "*.cs")))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"Loc\.Get\(""([A-Za-z0-9_.]+)""(?!\s*\+)|(?:Label|CapsuleButton)\([^;]*?""([a-z]+\.[A-Za-z0-9_.]+)""(?!\s*\+)"))
        {
            var key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!key.EndsWith(".")) yield return key;
        }
        foreach (var def in MatchSettings.Defs) yield return def.LabelKey;
        foreach (var (titleKey, _) in MatchSettings.Groups) yield return titleKey;
        string[] Names<T>() => Enum.GetNames(typeof(T));
        foreach (var name in Names<MatchMode>().Where(n => n != nameof(MatchMode.Item))) yield return "mode." + name;
        foreach (var name in Names<BoardType>()) yield return "board." + name;
        foreach (var name in Names<PieceType>()) yield return "pieces." + name;
        foreach (var name in Names<GameVariant>()) yield return "variant." + name;
        foreach (var name in Names<HealthRule>()) yield return "healthRule." + name;
        foreach (var name in Names<BothOutRule>()) yield return "bothOut." + name;
        foreach (var name in Names<PlacementStyle>()) yield return "placement.style" + name;
        foreach (var name in Names<Opponent>()) yield return "opponent." + name;
        foreach (var name in Names<Opponent>().Where(n => n != nameof(Opponent.Human)))
        {
            yield return "bot." + name;
            yield return "level." + name;
        }
        foreach (var type in Enum.GetValues(typeof(PieceType)).Cast<PieceType>())
        foreach (var key in PieceSet.Of(type).SideKeys)
            yield return key;
    }

    // ---- Play ----

    // The play checks, one after another on the editor's update: each an
    // iterator stepped once a tick, its exceptions and anything logged as an
    // error while it runs counted against it.
    private sealed class PlayRunner
    {
        private readonly Report report = new Report(SessionState.GetString(StaticKey, ""));
        private readonly Queue<(string name, Func<IEnumerator> run)> steps = new Queue<(string, Func<IEnumerator>)>();
        private readonly List<string> errors = new List<string>();
        private readonly int[] savedSeats = new int[4];
        private IEnumerator current;
        private string currentName;
        private float began;

        public void Start()
        {
            for (var i = 0; i < 4; i++) savedSeats[i] = PlayerPrefs.GetInt("local.seat" + i, -1);
            report.Section("Play");
            steps.Enqueue(("go stones and janggi pieces slow at about 17.4 m/s², gonggi stones at 8.6", Slowing));
            steps.Enqueue(("nothing catches on the cross board's seams", Seams));
            foreach (var (name, rules) in AiMatches()) steps.Enqueue(("AI match: " + name, () => AiMatch(rules)));
            steps.Enqueue(("network: the host's match with bots, replayed into a guest, ends the same", Network));
            Application.logMessageReceived += Logged;
            EditorApplication.update += Tick;
            began = Time.realtimeSinceStartup;
        }

        private void Logged(string condition, string stack, LogType type)
        {
            // Not this tool's own report, nor the Unity-MCP plugin's connection
            // trouble (its lines carry [AI]; a socket refused while it redials).
            if (type != LogType.Error && type != LogType.Exception) return;
            if (condition.StartsWith("[SelfCheck]") || condition.Contains("[AI]") || condition.StartsWith("SocketException")) return;
            errors.Add(condition.Split('\n')[0]);
        }

        private void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    Stop("play mode ended");
                    return;
                }
                if (current == null)
                {
                    if (steps.Count == 0)
                    {
                        Stop(null);
                        return;
                    }
                    var (name, run) = steps.Dequeue();
                    currentName = name;
                    errors.Clear();
                    current = run();
                    began = Time.realtimeSinceStartup;
                }
                if (Time.realtimeSinceStartup - began > 240)
                {
                    report.Check(currentName, false, "took over 4 minutes");
                    current = null;
                    return;
                }
                if (current.MoveNext()) return;
                current = null;
            }
            catch (Exception e)
            {
                report.Check(currentName, false, e.GetType().Name + ": " + e.Message);
                current = null;
            }
        }

        // What a step found, with the errors logged meanwhile.
        private void Result(bool ok, string detail)
        {
            if (errors.Count > 0) detail += $" | {errors.Count} errors logged, first: {errors[0]}";
            report.Check(currentName, ok && errors.Count == 0, detail);
        }

        private void Stop(string why)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Logged;
            if (why != null && current != null) report.Check(currentName, false, why);
            for (var i = 0; i < 4; i++)
            {
                if (savedSeats[i] < 0) PlayerPrefs.DeleteKey("local.seat" + i);
                else PlayerPrefs.SetInt("local.seat" + i, savedSeats[i]);
            }
            try
            {
                if (GameManager.manager != null) GameManager.manager.EndMatch();
            }
            catch
            {
                // Leaving anyway.
            }
            MatchSettings.Current = new MatchSettings();
            MatchRoster.Current = null;
            report.Finish("Full");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        // ---- Helpers ----

        private static IEnumerator Wait(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static void Seats(Opponent who)
        {
            for (var i = 0; i < 4; i++) PlayerPrefs.SetInt("local.seat" + i, (int)who);
        }

        private static MatchSettings Rules(BoardType board, PieceType pieces, int seats = 2, int stones = 5)
        {
            var rules = new MatchSettings();
            rules.Set(MatchSettingId.Mode, (int)MatchMode.Custom);
            rules.Set(MatchSettingId.BoardType, (int)board);
            rules.Set(MatchSettingId.PieceType, (int)pieces);
            rules.Set(MatchSettingId.Seats, seats);
            foreach (var id in MatchSettings.StoneIds) rules.Set(id, stones);
            return rules;
        }

        // A local match, as the setup card starts one, and on until its first turn (or placement).
        private static IEnumerator Load(MatchSettings rules)
        {
            if (GameManager.manager != null) GameManager.manager.EndMatch();
            MatchSettings.Current = rules;
            MatchRoster.Current = null;
            SceneManager.LoadScene("GameScene");
            var until = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < until && !Begun(GameManager.manager)) yield return null;
        }

        // Its first turn, or placing.
        private static bool Begun(GameManager gameManager) =>
            gameManager != null && gameManager.TurnController != null && (gameManager.TurnController.HasStarted || gameManager.gameState == GameManager.GameState.Placement);

        private static void Place(GamePieceDragAndReleaseForce piece, Vector3 at, Quaternion rotation)
        {
            piece.Body.position = at;
            piece.Body.rotation = rotation;
            piece.transform.SetPositionAndRotation(at, rotation);
            piece.Body.linearVelocity = Vector3.zero;
            piece.Body.angularVelocity = Vector3.zero;
        }

        // ---- Slowing ----

        private IEnumerator Slowing()
        {
            Seats(Opponent.Human);
            var found = new List<string>();
            var ok = true;
            foreach (var (type, expected, tolerance) in new[] { (PieceType.GoStones, 17.4f, 1.5f), (PieceType.JanggiPieces, 17.4f, 1.5f), (PieceType.GonggiStones, 8.6f, 1f) })
            {
                var load = Load(Rules(BoardType.Go, type));
                while (load.MoveNext()) yield return load.Current;
                var settle = Wait(1.5f);
                while (settle.MoveNext()) yield return null;
                var gameManager = GameManager.manager;
                var piece = gameManager.gamePieceScripts.Where(p => p.Manager.playerIndex == 0).OrderBy(p => p.Body.mass).First();
                foreach (var other in gameManager.gamePieceScripts.Where(p => p != piece)) other.gameObject.SetActive(false);
                var rest = piece.Body.rotation;
                var probe = piece.gameObject.AddComponent<SpeedProbe>();
                Place(piece, new Vector3(-0.3f, piece.Body.position.y, -1.3f), rest);
                piece.Body.AddForce(new Vector3(0.25f, 0, 1f).normalized * (PieceSet.Of(type).Grip > 1 ? 4f : 2.4f), ForceMode.VelocityChange);
                var until = Time.realtimeSinceStartup + 5;
                var moved = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup < until && (Time.realtimeSinceStartup - moved < 0.3f || piece.Body.linearVelocity.magnitude > 0.01f)) yield return null;
                var slowing = probe.MidSlowing();
                var fits = Mathf.Abs(slowing - expected) <= tolerance;
                ok &= fits;
                found.Add($"{type} {slowing:F1}{(fits ? "" : " (expected " + expected + ")")}");
            }
            Result(ok, string.Join(", ", found));
        }

        // ---- Seams ----

        private IEnumerator Seams()
        {
            Seats(Opponent.Human);
            var found = new List<string>();
            var ok = true;
            foreach (var type in new[] { PieceType.GonggiStones, PieceType.JanggiPieces })
            {
                var rules = Rules(BoardType.Cross, type);
                rules.Set(MatchSettingId.Zone, 0);
                var load = Load(rules);
                while (load.MoveNext()) yield return load.Current;
                var gameManager = GameManager.manager;
                var piece = gameManager.gamePieceScripts.First(p => p.Manager.playerIndex == 0);
                foreach (var other in gameManager.gamePieceScripts.Where(p => p != piece)) other.gameObject.SetActive(false);
                var probe = piece.gameObject.AddComponent<EdgeProbe>();
                var restY = piece.Body.position.y;
                var rest = piece.Body.rotation;
                var touched = 0;
                // Across the seam where the bars met, from an arm in and from the middle out.
                foreach (var z in new[] { 0f, 0.4f })
                foreach (var speed in new[] { 2f, 3f, 4.5f })
                foreach (var direction in new[] { -1, 1 })
                {
                    var from = direction < 0 ? 1.15f : 0.85f;
                    Place(piece, new Vector3(from, restY, z), rest);
                    probe.Contacts = 0;
                    piece.Body.linearVelocity = new Vector3(direction * speed, 0, 0);
                    var until = Time.realtimeSinceStartup + 1.2f;
                    while (Time.realtimeSinceStartup < until && Mathf.Abs(piece.Body.position.x - from) < 0.45f) yield return null;
                    piece.Body.linearVelocity = Vector3.zero;
                    if (probe.Contacts > 0) touched++;
                }
                ok &= touched == 0;
                found.Add($"{type} {touched}/12 slides caught an edge");
            }
            Result(ok, string.Join(", ", found));
        }

        // ---- AI matches ----

        private static IEnumerable<(string name, MatchSettings rules)> AiMatches()
        {
            yield return ("go board, go stones, 2, classic", Rules(BoardType.Go, PieceType.GoStones));
            var placing = Rules(BoardType.Janggi, PieceType.JanggiPieces);
            placing.Set(MatchSettingId.SpawnMode, (int)SpawnMode.Placement);
            placing.Set(MatchSettingId.PlacementStyle, (int)PlacementStyle.Alternating);
            yield return ("janggi board, janggi pieces, 2, placed in turn", placing);
            yield return ("hexagon, chess pieces, 3", Rules(BoardType.Hexagon, PieceType.ChessPieces, 3));
            var teams = Rules(BoardType.Cross, PieceType.GonggiStones, 4, 4);
            teams.Set(MatchSettingId.Teams, 1);
            teams.Set(MatchSettingId.Variant, (int)GameVariant.Health);
            teams.Set(MatchSettingId.PieceHealth, 50);
            teams.Set(MatchSettingId.Barrier, 1);
            yield return ("cross, gonggi stones, 2 v 2, health A behind walls", teams);
            var sideHealth = Rules(BoardType.Cross, PieceType.GoStones, 4);
            sideHealth.Set(MatchSettingId.Variant, (int)GameVariant.Health);
            sideHealth.Set(MatchSettingId.HealthRule, (int)HealthRule.Side);
            sideHealth.Set(MatchSettingId.SideHealth, 400);
            yield return ("cross, go stones, 4, health B", sideHealth);
            yield return ("ranked rules", MatchSettings.ForRanked().Resolve(2));
        }

        private IEnumerator AiMatch(MatchSettings rules)
        {
            Seats(Opponent.AINormal);
            var load = Load(rules);
            while (load.MoveNext()) yield return load.Current;
            var gameManager = GameManager.manager;
            var turns = gameManager.TurnController;
            var over = false;
            var winner = -1;
            turns.OnMatchEnded += (w, r) =>
            {
                over = true;
                winner = w;
            };
            var started = Time.realtimeSinceStartup;
            var state = turns.State;
            var since = started;
            var longest = 0f;
            while (!over && Time.realtimeSinceStartup - started < 150)
            {
                if (turns.HasStarted) Time.timeScale = 5f;
                if (turns.State != state)
                {
                    if (state == GameManager.GameState.ProcessingTurn) longest = Mathf.Max(longest, Time.realtimeSinceStartup - since);
                    state = turns.State;
                    since = Time.realtimeSinceStartup;
                }
                if (state == GameManager.GameState.ProcessingTurn && Time.realtimeSinceStartup - since > 15)
                {
                    var unsettled = gameManager.gamePieceScripts.Where(p => p != null && !p.IsSettled).Select(p => p.name);
                    Result(false, "a shot never settled: " + string.Join(", ", unsettled));
                    yield break;
                }
                yield return null;
            }
            Result(over, over ? $"winner {winner}, {turns.TurnsEnded} turns, longest shot {longest:F1}s, {Time.realtimeSinceStartup - started:F0}s" : $"not over after 150 s ({turns.TurnsEnded} turns)");
        }

        // ---- Network ----

        private const ulong HostId = 100, GuestId = 200;

        private IEnumerator Network()
        {
            Seats(Opponent.Human);
            var rules = Rules(BoardType.Go, PieceType.GoStones, 4, 3);
            rules.Set(MatchSettingId.Teams, 1);
            rules.Set(MatchSettingId.Variant, (int)GameVariant.Health);
            rules.Set(MatchSettingId.PieceHealth, 50);
            rules.Set(MatchSettingId.Barrier, 1);
            rules.Set(MatchSettingId.SpawnMode, (int)SpawnMode.Placement);
            rules.Set(MatchSettingId.PlacementStyle, (int)PlacementStyle.Alternating);
            rules.Set(MatchSettingId.TurnSeconds, 20);
            var roster = new MatchRoster(new[] { HostId, GuestId, MatchRoster.BotId(0), MatchRoster.BotId(1) }, null, new[] { Opponent.Human, Opponent.Human, Opponent.AINormal, Opponent.AIHard });
            var saved = NetSession.Current;
            var hostSide = new FakeTransport(HostId);
            var guestSide = new FakeTransport(GuestId);
            Msg.TurnResult final = null;
            MatchState guestState = null;
            var guestPieces = new Dictionary<char, (Vector3 position, int health)>();
            var detail = "";
            try
            {
                // The host: its AI plays its bots (InitHost); stand-ins play the host and the guest.
                var host = Bridge(hostSide, rules, roster, HostId);
                while (host.MoveNext()) yield return null;
                var gameManager = GameManager.manager;
                var waiting = !(bool)Field(Object.FindAnyObjectByType<NetworkMatchBridge>(), "matchBegun");
                foreach (var seat in new[] { 0, 1 }) new GameObject("Stand-in " + seat).AddComponent<AIOpponent>().playerId = seat;
                hostSide.Inject(GuestId, NetProtocol.Encode(new Msg.ClientReady()));
                var turns = gameManager.TurnController;
                var over = false;
                turns.OnMatchEnded += (w, r) => over = true;
                var started = Time.realtimeSinceStartup;
                while (!over && Time.realtimeSinceStartup - started < 150)
                {
                    if (turns.HasStarted) Time.timeScale = 5f;
                    yield return null;
                }
                var toBots = hostSide.Sent.Count(m => MatchRoster.IsBotId(m.to));
                var recording = hostSide.Sent.Where(m => m.to == 0 || m.to == GuestId).ToList();
                final = recording.Select(m => Decode(m.data)).OfType<Msg.TurnResult>().LastOrDefault();
                detail = $"waited for the guest {waiting}, over {over}, {recording.Count} messages, {toBots} to bots";
                if (!over || final == null || toBots > 0 || !waiting)
                {
                    Result(false, detail);
                    yield break;
                }

                // The guest: the same messages, as they came.
                var guest = Bridge(guestSide, rules, roster, GuestId);
                while (guest.MoveNext()) yield return null;
                var replayed = 0;
                var startAt = Time.realtimeSinceStartup - recording[0].at;
                while (replayed < recording.Count)
                {
                    while (replayed < recording.Count && recording[replayed].at + startAt <= Time.realtimeSinceStartup) guestSide.Inject(HostId, recording[replayed++].data);
                    yield return null;
                }
                var settle = Wait(3f);
                while (settle.MoveNext()) yield return null;
                guestState = GameManager.manager.TurnController.Snapshot();
                foreach (var piece in GameManager.manager.gamePieceScripts.Where(p => p != null)) guestPieces[piece.Manager.pieceID] = (piece.Body.position, piece.Manager.health);
            }
            finally
            {
                NetSession.Current = saved;
            }
            var same = guestState.Over == final.State.Over && guestState.Winner == final.State.Winner && guestState.TurnsEnded == final.State.TurnsEnded
                       && guestState.Sides.Select(s => s.Health).SequenceEqual(final.State.Sides.Select(s => s.Health));
            var off = final.Pieces.Count(t => !guestPieces.TryGetValue(t.PieceId, out var g) || Vector3.Distance(g.position, t.Position) > 0.001f || g.health != t.Health);
            Result(same && off == 0, $"{detail}; guest: winner {guestState.Winner} (host {final.State.Winner}), turns {guestState.TurnsEnded}/{final.State.TurnsEnded}, pieces off {off}/{final.Pieces.Count}");
        }

        // GameScene with a match bridge over a fake transport, as the
        // NetworkBootstrap would add it in a Steam lobby: its Start finds no
        // lobby and stays dormant, so it's woken with what Start would have set.
        private static IEnumerator Bridge(FakeTransport transport, MatchSettings rules, MatchRoster roster, ulong localId)
        {
            NetSession.Current = new NetSession(transport);
            if (GameManager.manager != null) GameManager.manager.EndMatch();
            MatchSettings.Current = rules;
            MatchRoster.Current = roster;
            NetworkMatchBridge bridge = null;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> loaded = null;
            loaded = (scene, mode) =>
            {
                if (scene.name != "GameScene") return;
                SceneManager.sceneLoaded -= loaded;
                bridge = new GameObject("NetworkMatchBridge").AddComponent<NetworkMatchBridge>();
            };
            SceneManager.sceneLoaded += loaded;
            SceneManager.LoadScene("GameScene");
            var until = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < until && (bridge == null || bridge.enabled || GameManager.manager == null || GameManager.manager.TurnController == null)) yield return null;
            var host = localId == HostId;
            Field(bridge, "localId", localId);
            Field(bridge, "roster", roster);
            Field(bridge, "hostId", HostId);
            Field(bridge, "isHost", host);
            Field(bridge, "localPlayerId", roster.PlayerOf(localId));
            GameManager.manager.SkipLocalTurnProcessing = !host;
            typeof(NetworkMatchBridge).GetMethod("OpenScope", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bridge, null);
            bridge.enabled = true;
            bridge.StartCoroutine((IEnumerator)typeof(NetworkMatchBridge).GetMethod("WaitForTurnControllerThenInit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bridge, null));
            var settle = Wait(1f);
            while (settle.MoveNext()) yield return null;
        }

        private static object Field(object target, string name, object value = null)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (value != null) field.SetValue(target, value);
            return field.GetValue(target);
        }

        private static INetMessage Decode(byte[] data) => NetProtocol.TryDecode(data, out _, out var message) ? message : null;
    }

    // A transport to nowhere: what's sent is kept (0 for a broadcast), and
    // what's received is handed in.
    private sealed class FakeTransport : ISessionTransport
    {
        public ulong LocalId { get; }
        public bool IsReady => true;
        public readonly List<(float at, ulong to, byte[] data)> Sent = new List<(float, ulong, byte[])>();
        public event Action<ulong, byte[]> OnMessageReceived;
#pragma warning disable 67 // a fake link never drops
        public event Action<ulong> OnPeerDisconnected;
#pragma warning restore 67

        public FakeTransport(ulong localId) => LocalId = localId;

        public void ConnectPeer(ulong id) { }
        public void Send(ulong targetId, byte[] data, bool reliable = true) => Sent.Add((Time.realtimeSinceStartup, targetId, data));
        public void Broadcast(byte[] data, bool reliable = true) => Sent.Add((Time.realtimeSinceStartup, 0, data));
        public void Inject(ulong from, byte[] data) => OnMessageReceived?.Invoke(from, data);
    }
}

// The piece's speed across the board every physics step, for its slowing
// mid-slide (from 80% of its top speed down to 20%).
internal sealed class SpeedProbe : MonoBehaviour
{
    private readonly List<(float t, float v)> samples = new List<(float, float)>();
    private Rigidbody body;

    private void Awake() => body = GetComponent<Rigidbody>();

    private void FixedUpdate() => samples.Add((Time.fixedTime, new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude));

    public float MidSlowing()
    {
        if (samples.Count < 3) return 0;
        var peak = samples.FindIndex(s => s.v == samples.Max(x => x.v));
        var top = samples[peak].v;
        var after = samples.Skip(peak).ToList();
        var from = after.FirstOrDefault(s => s.v <= 0.8f * top);
        var to = after.FirstOrDefault(s => s.v <= 0.2f * top);
        return to.t > from.t ? (from.v - to.v) / (to.t - from.t) : 0;
    }
}

// Contacts with the board that don't push straight up: an edge caught.
internal sealed class EdgeProbe : MonoBehaviour
{
    public int Contacts;

    private void OnCollisionEnter(Collision collision) => Look(collision);
    private void OnCollisionStay(Collision collision) => Look(collision);

    private void Look(Collision collision)
    {
        if (collision.collider.transform.name != "Slab") return;
        for (var i = 0; i < collision.contactCount; i++)
            if (collision.GetContact(i).normal.y < 0.9f) Contacts++;
    }
}
