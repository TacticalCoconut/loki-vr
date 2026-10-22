using Loki.Game;
using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 4 (w4vr): rumble the game itself decides (not a packet's): a heartbeat while HP is at a quarter or less (both
    /// hands, a double beat every 1.2 s; Settings → Controls "Heartbeat at low HP"), and the menu button that opens Settings on
    /// controllers where it is free (Touch's left, Reverb G2: LokiInput.Hand.SettingsDown). Job level ups rumble like base level ups:
    /// the job level arrives as ZC_PAR_CHANGE 0x00b0 SP_JOBLEVEL 55 (pc_checkjoblevelup pc.cpp:8342 → clif_updatestatus → clif.cpp:3652
    /// clif_par_change; map.hpp:505 numbers SP_JOBLEVEL 55), a higher value than the one PlayerState holds is a level up.
    /// </summary>
    public sealed class VrFeedback : MonoBehaviour
    {
        public const float LowHpFraction = 0.25f, BeatSeconds = 1.2f;
        private PlayerState _ps; private System.Func<bool> _playing; private DioramaRig _rig; private System.Action _settings;
        private float _nextBeat, _secondBeat = -1f;
        public int Beats { get; private set; }
        public bool LowHp { get; private set; }

        public void Init(PlayerState ps, System.Func<bool> playing, DioramaRig rig, System.Action openSettings)
        {
            _ps = ps; _playing = playing; _rig = rig; _settings = openSettings;
            ps.Par += (var, value) => { if (var == PlayerState.SP_JOBLEVEL && ps.JobLevel > 0 && value > ps.JobLevel) Haptics.Both(Haptics.Kind.LevelUp, 0.7f, 0.3f); }; // before PlayerState stores it (PlayerState.Set)
        }

        private void Update()
        {
            if (_rig != null && _rig.Input != null && (_rig.Input.Left.SettingsDown || _rig.Input.Right.SettingsDown)) _settings?.Invoke();
            LowHp = _ps != null && (_playing == null || _playing()) && !_ps.Dead && _ps.MaxHp > 1 && _ps.Hp > 0 && _ps.Hp <= _ps.MaxHp * LowHpFraction;
            if (_secondBeat > 0 && Time.time >= _secondBeat) { _secondBeat = -1f; if (LowHp && Comfort.Heartbeat) Haptics.Both(Haptics.Kind.LowHp, 0.22f, 0.05f); }
            if (!LowHp || !Comfort.Heartbeat) { _nextBeat = 0f; return; }
            if (Time.time < _nextBeat) return;
            _nextBeat = Time.time + BeatSeconds; _secondBeat = Time.time + 0.18f; Beats++;
            Haptics.Both(Haptics.Kind.LowHp, 0.35f, 0.06f);
        }
    }
}
