using System.Collections.Generic;
using UnityEngine;

public struct PieceOwnerEntry
{
    public char PieceId;
    public int PlayerId;
}

public struct PieceTransform
{
    public char PieceId;
    public Vector3 Position;
    public Quaternion Rotation;
    public int Health; // a battle of health's, A (GamePieceManager.health)

    public static void Write(NetWriter w, PieceTransform t)
    {
        w.Piece(t.PieceId);
        w.Vector3(t.Position);
        w.Quaternion(t.Rotation);
        w.Int(t.Health);
    }

    public static PieceTransform Read(NetReader r) => new PieceTransform { PieceId = r.Piece(), Position = r.Vector3(), Rotation = r.Quaternion(), Health = r.Int() };
}

// The messages, one class each (NetProtocol has their numbers and routes).
// Fields are written in order; add new ones at the end of Write and Read.
public static class Msg
{
    // Host -> a guest that has loaded: which side it plays, and every
    // piece's owner, to check both built the same match.
    public sealed class StartMatch : INetMessage
    {
        public int PlayerId;
        public List<PieceOwnerEntry> Owners = new List<PieceOwnerEntry>();

        public void Write(NetWriter w)
        {
            w.Int(PlayerId);
            w.List(Owners, (x, o) =>
            {
                x.Piece(o.PieceId);
                x.Int(o.PlayerId);
            });
        }

        public void Read(NetReader r)
        {
            PlayerId = r.Int();
            Owners = r.List(x => new PieceOwnerEntry { PieceId = x.Piece(), PlayerId = x.Int() });
        }
    }

    // Guest -> host: my shot.
    public sealed class Flick : INetMessage
    {
        public char PieceId;
        public Vector3 Force;

        public void Write(NetWriter w)
        {
            w.Piece(PieceId);
            w.Vector3(Force);
        }

        public void Read(NetReader r)
        {
            PieceId = r.Piece();
            Force = r.Vector3();
        }
    }

    // Host -> guests, a few times a second while pieces move (unreliable).
    public sealed class PieceSnapshot : INetMessage
    {
        public List<PieceTransform> Pieces = new List<PieceTransform>();

        public void Write(NetWriter w) => w.List(Pieces, PieceTransform.Write);
        public void Read(NetReader r) => Pieces = r.List(PieceTransform.Read);
    }

    // Host -> guests at every turn's start and at the result: the match
    // (MatchState), the kill feed of the shot that just resolved, the pieces
    // gone for good since the last one, and where every piece settled, so
    // a guest converges on the host's board whatever snapshots it missed.
    public sealed class TurnResult : INetMessage
    {
        public MatchState State = new MatchState();
        public List<KillEvent> Kills = new List<KillEvent>();
        public List<char> Removed = new List<char>();
        public List<PieceTransform> Pieces = new List<PieceTransform>();

        public void Write(NetWriter w)
        {
            w.Record(State.Write);
            w.List(Kills, KillEvent.Write);
            w.List(Removed, (x, id) => x.Piece(id));
            w.List(Pieces, PieceTransform.Write);
        }

        public void Read(NetReader r)
        {
            State = r.Record(MatchState.Read);
            Kills = r.List(KillEvent.Read);
            Removed = r.List(x => x.Piece());
            Pieces = r.List(PieceTransform.Read);
        }
    }

    // Host -> guests: the match changed between turn results (a side
    // conceded or left).
    public sealed class MatchStateUpdate : INetMessage
    {
        public MatchState State = new MatchState();

        public void Write(NetWriter w) => w.Record(State.Write);
        public void Read(NetReader r) => State = r.Record(MatchState.Read);
    }

    // Host -> everyone in its lobby: the rules and seats of the match to
    // load, so every side plays exactly these whatever its lobby data says.
    public sealed class LoadGameScene : INetMessage
    {
        public MatchSettings Settings = new MatchSettings();
        public MatchRoster Roster;

        public void Write(NetWriter w)
        {
            w.Record(Settings.Write);
            w.Record(Roster.Write);
        }

        public void Read(NetReader r)
        {
            Settings = r.Record(MatchSettings.Read);
            Roster = r.Record(MatchRoster.Read);
        }
    }

    // Host -> each guest: the placement phase as that guest may see it.
    public sealed class PlacementState : INetMessage
    {
        public PlacementSnapshot State = new PlacementSnapshot();

        public void Write(NetWriter w) => w.Record(State.Write);
        public void Read(NetReader r) => State = r.Record(PlacementSnapshot.Read);
    }

    // Guest -> host: put or move one of my pieces.
    public sealed class PlaceRequest : INetMessage
    {
        public char PieceId;
        public float X, Z;

        public void Write(NetWriter w)
        {
            w.Piece(PieceId);
            w.Float(X);
            w.Float(Z);
        }

        public void Read(NetReader r)
        {
            PieceId = r.Piece();
            X = r.Float();
            Z = r.Float();
        }
    }

    // Host -> guests: a knock, hinge, flick, fall or topple to play, and
    // where (unreliable).
    public sealed class BoardSound : INetMessage
    {
        public BoardSoundEvent Sound;

        public void Write(NetWriter w)
        {
            w.Byte((byte)Sound.Kind);
            w.Byte((byte)Mathf.RoundToInt(Mathf.Clamp01(Sound.Volume) * 255));
            w.Vector3(Sound.Position);
            w.Piece(Sound.Piece);
        }

        public void Read(NetReader r)
        {
            var kind = (global::BoardSound)r.Byte();
            var volume = r.Byte() / 255f;
            var position = r.Vector3();
            var piece = r.Piece();
            Sound = new BoardSoundEvent { Kind = kind, Volume = volume, Pan = BoardSounds.Pan(position), Owner = -1, Position = position, Piece = piece };
        }
    }

    // Host -> guests: a battle of health's knock as it lands, for the HUD
    // (the turn's result has the health that counts). Health: what the
    // piece (A) or its side (B) has left.
    public sealed class Damage : INetMessage
    {
        public char PieceId;
        public int Amount;
        public int Health;
        public Vector3 Position;

        public void Write(NetWriter w)
        {
            w.Piece(PieceId);
            w.Int(Amount);
            w.Int(Health);
            w.Vector3(Position);
        }

        public void Read(NetReader r)
        {
            PieceId = r.Piece();
            Amount = r.Int();
            Health = r.Int();
            Position = r.Vector3();
        }
    }

    // Host -> guests: the shot playing out is (or no longer is) fast-forwarded.
    public sealed class FastForward : INetMessage
    {
        public bool On;

        public void Write(NetWriter w) => w.Bool(On);
        public void Read(NetReader r) => On = r.Bool();
    }

    // The messages that are only their type.
    public abstract class Signal : INetMessage
    {
        public void Write(NetWriter w) { }
        public void Read(NetReader r) { }
    }

    public sealed class ClientReady : Signal { }    // guest -> host: loaded and spawned
    public sealed class RematchRequest : Signal { } // anyone: I want another
    public sealed class ReturnToLobby : Signal { }  // anyone: I'm going back to the lobby
    public sealed class Pass : Signal { }           // guest -> host: skip my turn
    public sealed class PlacementReady : Signal { } // guest -> host: my pieces are final
    public sealed class Kick : Signal { }           // host -> a guest: leave my lobby
    public sealed class Concede : Signal { }        // guest -> host: I give up
}
