using System.Collections.Generic;
using System.Threading.Tasks;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Audio;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>
    /// Ton (Kapitel 34): Musik mit Überblendung, Umgebungsgeräusch, Effekte im Raum, Oberflächenklänge, Schritte und
    /// Sprechlaute von NPCs. Alle Klänge erzeugt <see cref="Synth"/> – Musik im Hintergrund-Thread, damit nichts ruckelt.
    /// Lautstärken: Gesamt × Musik/Effekte/Umgebung/Sprache aus den Einstellungen.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; } = null!;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, Task<float[]>> _pending = new Dictionary<string, Task<float[]>>();
        private readonly MusicDirector _director = new MusicDirector();
        private AudioSource _musicA = null!, _musicB = null!, _ambience = null!, _ui = null!;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private bool _aActive = true;
        private float _fade = 1;
        private string _wantedMusic = "", _wantedAmbience = "";

        private Shared.Client.GameClient Client => NetworkManager.Instance.Client;
        private Shared.Client.Ui.GameSettings Settings => Client.Settings;

        private void Awake()
        {
            Instance = this;
            _musicA = NewSource("Musik A", true);
            _musicB = NewSource("Musik B", true);
            _ambience = NewSource("Umgebung", true);
            _ui = NewSource("Oberfläche", false);
            for (int i = 0; i < 12; i++)
            {
                var s = NewSource("Effekt " + i, false);
                s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 4; s.maxDistance = 60;
                _pool.Add(s);
            }
            Client.MessageReceived += OnMessage;
        }

        private AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0;
            return s;
        }

        /// <summary>Clip sofort (Effekte, kurz) oder erst, wenn der Hintergrund-Thread fertig ist (Musik, Umgebung).</summary>
        private AudioClip? Clip(string key, bool background)
        {
            if (_clips.TryGetValue(key, out var c)) return c;
            if (!background) return _clips[key] = Make(key, Synth.Effect(key));
            if (!_pending.TryGetValue(key, out var task))
            {
                bool music = key.StartsWith("music_");
                _pending[key] = task = Task.Run(() => music ? Synth.Music(key) : Synth.Ambience(key));
            }
            if (!task.IsCompleted) return null;
            _pending.Remove(key);
            if (task.IsFaulted) { Debug.LogWarning($"Klang {key}: {task.Exception?.GetBaseException().Message}"); return _clips[key] = Make(key, new float[Synth.DefaultRate]); }
            return _clips[key] = Make(key, task.Result);
        }

        private static AudioClip Make(string key, float[] data)
        {
            var clip = AudioClip.Create(key, data.Length, 1, Synth.DefaultRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void PlayUi(string key, float volume = 1f)
        {
            var clip = Clip(key, false);
            if (clip != null) _ui.PlayOneShot(clip, volume * Settings.VolumeEffects);
        }

        public void PlayAt(string key, Vector3 position, float volume, float pitch = 1f, bool dialog = false)
        {
            var clip = Clip(key, false);
            if (clip == null) return;
            AudioSource? src = null;
            foreach (var s in _pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) return; // lieber ein Geräusch auslassen als laut abschneiden
            src.transform.position = position;
            src.pitch = pitch;
            src.volume = volume * (dialog ? Settings.VolumeDialog : Settings.VolumeEffects);
            src.clip = clip;
            src.Play();
        }

        private void OnMessage(Message m)
        {
            if (!Client.InWorld) return;
            var cue = SoundEvents.For(m, Client, NetworkManager.Instance.Content);
            if (cue != null)
            {
                var c = cue.Value;
                var view = WorldRenderer.Instance.ViewOf(c.EntityId);
                if (c.Ui || view == null || c.EntityId == Client.State.MyId) PlayUi(c.Key, c.Volume);
                else PlayAt(c.Key, view.transform.position, c.Volume, Random.Range(0.94f, 1.06f));
            }
            // Sprechlaut, wenn ein NPC in der Nähe spricht (es gibt keine Sprachaufnahmen)
            if (m is S_Chat chat && Client.State.Get(chat.SenderId) is { } speaker && (speaker.Kind == EntityKind.Npc || speaker.Kind == EntityKind.Creature)
                && WorldRenderer.Instance.ViewOf(speaker.Id) is { } v)
                PlayAt("emote", v.transform.position, 0.5f, 0.7f + (Synth.Hash(speaker.Name) & 7) * 0.05f, dialog: true);
        }

        private void Update()
        {
            float master = Settings.MuteInBackground && !Application.isFocused ? 0 : Settings.VolumeMaster;
            AudioListener.volume = master;
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            if (!Client.InWorld)
            {
                _musicA.volume = Mathf.MoveTowards(_musicA.volume, 0, Time.deltaTime);
                _musicB.volume = Mathf.MoveTowards(_musicB.volume, 0, Time.deltaTime);
                _ambience.volume = 0;
                return;
            }
            var me = Client.Me;
            var content = NetworkManager.Instance.Content;
            content.Zones.TryGetValue(Client.ZoneId, out var zone);
            var area = zone != null && me != null ? zone.Areas.Find(a => Vec3.DistanceXZ(a.Center, me.Position) <= a.Radius) : null;
            bool boss = false;
            if (me != null && (me.Flags & UnitFlags.InCombat) != 0)
                foreach (var e in Client.State.Entities.Values)
                    if ((e.Flags & UnitFlags.Boss) != 0 && (e.Flags & UnitFlags.InCombat) != 0 && !e.IsDead && Vec3.DistanceXZ(e.Position, me.Position) < 60) { boss = true; break; }
            float hour = Client.State.World?.GameHour ?? 12;
            var sit = new MusicSituation
            {
                Zone = zone, Area = area, InCombat = me != null && (me.Flags & UnitFlags.InCombat) != 0, BossFight = boss,
                Ghost = me != null && (me.Flags & UnitFlags.Ghost) != 0, Night = hour < 6 || hour >= 21,
            };
            if (_director.Update(Time.deltaTime, sit)) { _wantedMusic = _director.Current; }
            _wantedAmbience = _director.Ambience;
            UpdateMusic();
            UpdateAmbience();
            // Eigene Schritte
            if (me != null && WorldRenderer.Instance.ViewOf(me.Id)?.Rig is { StepThisFrame: true })
                PlayUi("footstep_" + (zone?.Biome.ToLowerInvariant() is { Length: > 0 } b ? b : "wald"), 0.35f);
        }

        private void UpdateMusic()
        {
            var active = _aActive ? _musicA : _musicB;
            var other = _aActive ? _musicB : _musicA;
            float target = Settings.VolumeMusic * 0.7f;
            if (_wantedMusic != "" && (active.clip == null || active.clip.name != _wantedMusic))
            {
                var clip = Clip(_wantedMusic, true);
                if (clip != null)
                {
                    // Neues Stück auf der freien Quelle starten und überblenden
                    _aActive = !_aActive;
                    (active, other) = (other, active);
                    active.clip = clip;
                    active.volume = 0;
                    active.Play();
                    _fade = 0;
                }
            }
            _fade = Mathf.Min(1, _fade + Time.deltaTime / MusicDirector.Crossfade);
            active.volume = target * _fade;
            other.volume = target * (1 - _fade);
            if (_fade >= 1 && other.isPlaying) other.Stop();
        }

        private void UpdateAmbience()
        {
            if (_wantedAmbience != "" && (_ambience.clip == null || _ambience.clip.name != _wantedAmbience))
            {
                var clip = Clip(_wantedAmbience, true);
                if (clip != null) { _ambience.clip = clip; _ambience.Play(); }
            }
            _ambience.volume = Settings.VolumeAmbient * 0.5f;
        }
    }
}
