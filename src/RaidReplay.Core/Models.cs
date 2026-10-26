using System;

namespace RaidReplay.Core
{
    public enum EntitySide : byte { Unknown = 0, Usec = 1, Bear = 2, Savage = 3 }

    public static class SampleFlags
    {
        public const byte Alive = 1, Crouch = 2, Prone = 4, Sprint = 8, Aiming = 16, LeanLeft = 32, LeanRight = 64;
        public static bool Has(byte flags, byte bit) => (flags & bit) != 0;
    }

    /// <summary>One recorded pose of one entity. Angles in degrees, distances in metres.</summary>
    public struct Sample
    {
        public int TimeMs;
        public float Px, Py, Pz;          // feet position
        public float Ex, Ey, Ez;          // eye / camera position
        public float BodyYaw;             // body heading
        public float LookYaw, LookPitch, LookRoll; // view rotation (Euler, Unity order)
        public byte Flags;                // SampleFlags
        public byte Speed;                // m/s * 20
        public byte Fov;                  // degrees (POV track only, 0 otherwise)

        public bool Alive => (Flags & SampleFlags.Alive) != 0;
        public float SpeedMs => Speed / 20f;
    }

    public sealed class EntityInfo
    {
        public int Index;
        public string ProfileId = "";
        public string Nickname = "";
        public EntitySide Side;
        public bool IsAI;
        public bool IsPov;
        public int Level;
        public string Role = "";
        /// <summary>Optional serialized profile/equipment (reserved for model-based puppets).</summary>
        public byte[] Blob = Array.Empty<byte>();
    }

    public enum EventKind : byte { Shot = 1, Kill = 2, Extract = 3, Marker = 4 }

    public struct ReplayEvent
    {
        public int TimeMs;
        public EventKind Type;
        public int Actor;   // entity index or -1
        public int Target;  // entity index or -1
        public float X, Y, Z;
        public float Arg, Arg2; // Shot: yaw, pitch of the bullet direction
    }

    public enum WorldKind : byte { Door = 1, Lamp = 2 }

    /// <summary>State of a door / container (EDoorState) or lamp (Turnable.EState) at a moment of the raid. Time 0 entries are the baseline.</summary>
    public struct WorldEvent
    {
        public int TimeMs;
        public WorldKind Kind;
        public int NetId;
        public byte State;
        public bool Broken;
    }

    public sealed class ReplayHeader
    {
        public ushort Version = ReplayFile.CurrentVersion;
        public string Map = "";
        public long StartedUtcTicks;
        public string PovProfileId = "";
        public string PovNickname = "";
        public string GameInfo = "";
        public int OthersHz;
        public int PovHz;
        public DateTime StartedUtc => new DateTime(StartedUtcTicks, DateTimeKind.Utc);
    }
}
