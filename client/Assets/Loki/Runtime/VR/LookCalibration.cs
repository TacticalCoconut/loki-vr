using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 20 (w20tut), point 65 of the design's notes after wave 17 (playtest notes; design note: "a calibration
    /// sequence for the eyesight choosing system, where the player has to look at a sequence of dots and you'll take the average around
    /// the location that the player was looking at to calibrate where the looking circle is"; "Stored per PC"). The CORRECTION the look
    /// calibration measured (UI/LookCalibrator.cs runs it; LokiGame.W20Tut.cs starts it before the tutorial and from Settings → Comfort →
    /// Look select "Recalibrate look").
    ///   What the look is  the rig's one gaze ray (VR/DioramaRig.Ui.cs GazeRay): the HEAD's forward (no headset Loki supports gives the
    ///                     eyes' direction: nothing reads eye tracking), started between the eyes or at one eye (Comfort.GazeSide) and
    ///                     turned up / down by Comfort.GazeUp (wave 11's two sliders, kept with the account). The same ray chooses the
    ///                     buttons, locks the world's targets (Game/Targeting.cs), gives the ground cell, wakes the overhead menu and draws
    ///                     the look ring (the gaze dot).
    ///   What is measured  for each dot the player looked at, the dot's direction in the HEAD's frame minus the sliders' ray's direction
    ///                     there, as a sideways angle (yaw, + to the right) and an up / down angle (pitch, + up), averaged over the samples
    ///                     of its filling ring; the correction is the average of the dots' averages (design note: "take the average"). A player
    ///                     whose head points a little right of and below what the eyes look at gets a correction left and up.
    ///   Applied           on top of the sliders: the sliders' ray's yaw and pitch in the head's frame plus the correction (<see cref="Apply"/>).
    ///                     So the sliders still work (per account) and the calibration corrects what is left on THIS PC.
    ///   Stored            per PC (design note: "Stored per PC"): World/UserSettings.cs lookCalDone / lookCalYaw / lookCalPitch / lookCalDots /
    ///                     lookCalAt, all in UserSettings.PerPcFields (never in the account's settings: w18acct's split).
    /// </summary>
    public static class LookCalibration
    {
        /// <summary>The largest correction kept, each way (degrees): the dots' "near" cone (UI/LookCalibrator.cs NearDeg).</summary>
        public const float MaxDeg = 12f;
        /// <summary>This PC has a calibration.</summary>
        public static bool Done => World.UserSettings.Prefs.lookCalDone;
        /// <summary>The correction, degrees: sideways (+ right) and up / down (+ up). Zero when not calibrated.</summary>
        public static float Yaw => Done ? Mathf.Clamp(World.UserSettings.Prefs.lookCalYaw, -MaxDeg, MaxDeg) : 0f;
        public static float Pitch => Done ? Mathf.Clamp(World.UserSettings.Prefs.lookCalPitch, -MaxDeg, MaxDeg) : 0f;
        /// <summary>Calibrations finished in this run (log, checks).</summary>
        public static int Saves { get; private set; }

        /// <summary>A direction in a head's frame as angles: yaw (+ right, about the head's up) and pitch (+ up), degrees.</summary>
        public static Vector2 Angles(Vector3 local)
        {
            float h = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            return new Vector2(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, Mathf.Atan2(local.y, h) * Mathf.Rad2Deg);
        }
        /// <summary>The direction in a head's frame for these angles (<see cref="Angles"/>' inverse).</summary>
        public static Vector3 FromAngles(Vector2 a)
        {
            float y = a.x * Mathf.Deg2Rad, p = a.y * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p));
        }

        /// <summary>The corrected world direction of a world direction <paramref name="dir"/> of the head <paramref name="head"/>.</summary>
        public static Vector3 Apply(Transform head, Vector3 dir)
        {
            if (!Done) return dir;
            float yaw = Yaw, pitch = Pitch;
            if (Mathf.Abs(yaw) < 1e-4f && Mathf.Abs(pitch) < 1e-4f) return dir;
            var a = Angles(head.InverseTransformDirection(dir));
            return head.TransformDirection(FromAngles(new Vector2(a.x + yaw, a.y + pitch)));
        }

        /// <summary>Keeps a new correction on this PC (saved at once).</summary>
        public static void Save(float yaw, float pitch, int dots)
        {
            var s = World.UserSettings.Prefs;
            s.lookCalDone = true; s.lookCalYaw = Mathf.Clamp(yaw, -MaxDeg, MaxDeg); s.lookCalPitch = Mathf.Clamp(pitch, -MaxDeg, MaxDeg);
            s.lookCalDots = dots; s.lookCalAt = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
            Saves++;
            Comfort.Save();
            Debug.Log($"[Look] calibrated on this PC: {s.lookCalYaw:+0.00;-0.00;0.00}° sideways, {s.lookCalPitch:+0.00;-0.00;0.00}° up / down, from {dots} dots ({World.UserSettings.FilePath})");
        }
    }
}
