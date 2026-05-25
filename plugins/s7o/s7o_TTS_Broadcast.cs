// s7o_TTS_Broadcast.cs
// Standalone .tts broadcaster for TurboHUD Free / FREEHUD and LightningMOD.
// Drop into: TurboHUD\plugins\s7o\s7o_TTS_Broadcast.cs
//
// Usage:
//   Type .tts your message here in chat.
//
// This isolated baseline is intentionally compact and conservative:
//   - no anti-spam
//   - no duplicate-message suppression
//   - no queue
//   - no channel filters
//   - .tts messages speak immediately
//   - native Hud.Sound.Speak fallback is OFF by default to avoid conflicts
//     with other HUD TTS plugins such as pylon/item/goblin alerts.
//
// Safety model:
//   The plugin only speaks text after a real .tts command token. It will not
//   repeat generic HUD/native TTS lines such as "you have conduit" unless that
//   text was explicitly sent as: .tts you have conduit

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SharpDX.DirectInput;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_TTS_Broadcast : BasePlugin, IChatLineChangedHandler, IKeyEventHandler, IAfterCollectHandler
    {
        // ------------------------------------------------------------------
        // USER SETTINGS - edit these in Notepad if needed
        // ------------------------------------------------------------------

        // Chat command. Everything after this command is spoken.
        public string Prefix = ".tts";

        // Independent Windows SAPI voice settings. This does NOT modify
        // Hud.Sound.VolumeMultiplier or any native HUD TTS volume setting.
        public bool UseIndependentSapiVoice = true;
        public int Volume = 100;       // SAPI only: 0-100
        public int Rate = 0;           // SAPI only: -10 to 10

        // IMPORTANT:
        // Keep this false if you have other plugins using Hud.Sound.Speak(),
        // such as pylon alerts. Hud.Sound is shared/global. Enabling fallback
        // can conflict with native HUD TTS queues/state.
        public bool UseHudSoundSpeakFallback = false;

        // Respect HUD MENU Global TTS master enable/volume when HUD MENU is installed.
        // This is set by HUD MENU through optional reflection; no compile-time dependency.
        public bool RespectGlobalTtsSettings = true;

        // Reads the local chat edit box on Enter so repeated identical .tts messages
        // still speak even when FreeHUD does not fire a chat-line-changed event.
        public bool EnableLocalInputListener = true;

        // Chat line listener lets you hear .tts messages from other players.
        public bool EnableChatLineListener = true;

        // Strict command parsing rejects accidental matches inside plugin names,
        // paths, debug lines, or native HUD TTS labels. Recommended true.
        public bool StrictCommandToken = true;

        // If true, your own chat-line echo is ignored after the local Enter path
        // already spoke it. This prevents double-speaking your own message.
        // It does NOT block repeated Enter sends.
        public bool IgnoreOwnChatEchoAfterLocalSpeak = true;

        // ------------------------------------------------------------------
        // Internal constants
        // ------------------------------------------------------------------
        private const string ChatInputPath = "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline";
        private const int CachedInputMaxAgeMs = 350;
        private const int LocalEchoWindowMs = 800;

        private IUiElement _chatInput;
        private bool _chatWasOpen;
        private string _cachedInputText;
        private int _cachedInputTick = int.MinValue;
        private string _lastLocalPayload;
        private int _lastLocalSpeakTick = int.MinValue;
        private bool _sapiUnavailable;

        private bool _globalTtsEnabled = true;
        private int _globalTtsVolume = 100;

        private readonly object _sapiLock = new object();
        private readonly List<object> _activeSapiVoices = new List<object>();

        public s7o_TTS_Broadcast()
        {
            Enabled = true;
            Order = 0;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            try { _chatInput = Hud.Render.RegisterUiElement(ChatInputPath, null, null); }
            catch { _chatInput = null; }
        }

        public void SetGlobalTtsSettings(bool enabled, int volume)
        {
            try
            {
                _globalTtsEnabled = enabled;
                _globalTtsVolume = Clamp(volume, 0, 100);

                if (!_globalTtsEnabled)
                    StopAllSapiSpeech();
                else
                    ApplyVolumeToActiveSapiVoices();
            }
            catch { }
        }

        public void ForceStopForDisable()
        {
            try { StopAllSapiSpeech(); }
            catch { }
        }

        public void AfterCollect()
        {
            CacheChatInputOnly();
        }

        public void OnChatLineChanged(string currentLine, string previousLine)
        {
            if (!EnableChatLineListener)
                return;
            if (!CanSpeakByGlobalTts())
                return;

            string payload;
            if (!TryExtractTtsFromChatLine(currentLine, out payload))
                return;

            if (IgnoreOwnChatEchoAfterLocalSpeak && LooksLikeOwnEcho(currentLine, payload))
                return;

            SpeakNow(payload);
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!EnableLocalInputListener)
                return;
            if (!CanSpeakByGlobalTts())
                return;
            if (keyEvent == null || !keyEvent.IsPressed || !IsEnter(keyEvent.Key))
                return;

            string raw = null;

            // Prefer live read while the chat box is still visible.
            if (IsChatInputVisible())
                raw = ReadChatInput();

            // If Enter already closed/cleared the edit box, use the last value
            // captured while the chat box was open. This cache is intentionally
            // very short-lived so it cannot replay stale unrelated text later.
            if (string.IsNullOrWhiteSpace(raw) && IsFreshCachedInput())
                raw = _cachedInputText;

            string payload;
            if (!TryExtractTtsFromTypedInput(raw, out payload))
                return;

            _lastLocalPayload = payload;
            _lastLocalSpeakTick = NowTick();
            SpeakNow(payload);
        }

        private void CacheChatInputOnly()
        {
            bool open = IsChatInputVisible();

            if (open)
            {
                string raw = ReadChatInput();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    _cachedInputText = raw;
                    _cachedInputTick = NowTick();
                }
            }
            else if (_chatWasOpen)
            {
                // Do not clear immediately: OnKeyEvent may arrive just after
                // the game closes the chat edit box. Freshness gate prevents
                // old text from being reused later.
            }

            _chatWasOpen = open;
        }

        private bool TryExtractTtsFromTypedInput(string raw, out string payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrEmpty(Prefix))
                return false;

            string s = raw.TrimStart();
            if (!StartsWithPrefixCommand(s, 0))
                return false;

            return ExtractPayloadAfterPrefix(s, 0, out payload);
        }

        private bool TryExtractTtsFromChatLine(string raw, out string payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrEmpty(Prefix))
                return false;

            int index = FindPrefixCommand(raw);
            if (index < 0)
                return false;

            return ExtractPayloadAfterPrefix(raw, index, out payload);
        }

        private int FindPrefixCommand(string raw)
        {
            int start = 0;
            while (start < raw.Length)
            {
                int i = raw.IndexOf(Prefix, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0)
                    return -1;

                if (StartsWithPrefixCommand(raw, i))
                    return i;

                start = i + Prefix.Length;
            }
            return -1;
        }

        private bool StartsWithPrefixCommand(string raw, int index)
        {
            if (raw == null || index < 0 || index + Prefix.Length > raw.Length)
                return false;
            if (!raw.Substring(index, Prefix.Length).Equals(Prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!StrictCommandToken)
                return true;

            char before = index > 0 ? raw[index - 1] : '\0';
            char after = index + Prefix.Length < raw.Length ? raw[index + Prefix.Length] : '\0';

            // Reject matches inside names/paths/namespaces such as s7o.TTS.Alert.
            if (index > 0 && (char.IsLetterOrDigit(before) || before == '_' || before == '.' || before == '/' || before == '\\'))
                return false;

            // Require a clean command terminator after .tts.
            if (after != '\0' && !char.IsWhiteSpace(after) && after != ':' && after != '-' && after != '>' && after != '–' && after != '—')
                return false;

            return true;
        }

        private bool ExtractPayloadAfterPrefix(string raw, int index, out string payload)
        {
            payload = null;
            int start = index + Prefix.Length;
            if (start > raw.Length)
                return false;

            string text = raw.Substring(start).Trim();
            while (text.Length > 0 && (text[0] == ':' || text[0] == '-' || text[0] == '>' || text[0] == '–' || text[0] == '—'))
                text = text.Substring(1).TrimStart();

            text = NormalizeTtsPayloadForSpeech(text);

            if (string.IsNullOrWhiteSpace(text))
                return false;

            payload = text;
            return true;
        }

        private static string NormalizeTtsPayloadForSpeech(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace('\u00A0', ' ');
            text = text.Replace('\r', ' ').Replace('\n', ' ');

            // Remove duplicate-line salt and normal accidental edge spaces.
            text = text.Trim();

            return text;
        }

        private bool CanSpeakByGlobalTts()
        {
            try
            {
                return !RespectGlobalTtsSettings || _globalTtsEnabled;
            }
            catch
            {
                return true;
            }
        }

        private int EffectiveSapiVolume()
        {
            try
            {
                int local = Clamp(Volume, 0, 100);

                if (!RespectGlobalTtsSettings)
                    return local;

                int global = Clamp(_globalTtsVolume, 0, 100);
                return Clamp((int)Math.Round(local * (global / 100.0)), 0, 100);
            }
            catch
            {
                return Clamp(Volume, 0, 100);
            }
        }

        private void ApplyVolumeToActiveSapiVoices()
        {
            object[] voices = null;

            try
            {
                lock (_sapiLock)
                    voices = _activeSapiVoices.ToArray();
            }
            catch { }

            if (voices == null)
                return;

            int volume = EffectiveSapiVolume();

            for (int i = 0; i < voices.Length; i++)
            {
                try
                {
                    object voice = voices[i];
                    if (voice == null)
                        continue;

                    Type t = voice.GetType();
                    t.InvokeMember("Volume", BindingFlags.SetProperty, null, voice, new object[] { volume });
                }
                catch { }
            }
        }

        private void StopAllSapiSpeech()
        {
            object[] voices = null;

            try
            {
                lock (_sapiLock)
                    voices = _activeSapiVoices.ToArray();
            }
            catch { }

            if (voices == null)
                return;

            for (int i = 0; i < voices.Length; i++)
                TryPurgeSapiVoice(voices[i]);
        }

        private void TryPurgeSapiVoice(object voice)
        {
            try
            {
                if (voice == null)
                    return;

                Type t = voice.GetType();

                // SAPI flags: 1 = async, 2 = purge before speak, 3 = async + purge.
                t.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { string.Empty, 3 });
            }
            catch { }
        }

        public bool SpeakExternal(string text)
        {
            return SpeakNow(text);
        }

        private bool SpeakNow(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (!CanSpeakByGlobalTts())
                return false;

            if (UseIndependentSapiVoice && !_sapiUnavailable && StartSapiWorker(text))
                return true;

            if (UseHudSoundSpeakFallback && CanSpeakByGlobalTts())
            {
                try
                {
                    if (Hud != null && Hud.Sound != null)
                    {
                        Hud.Sound.Speak(text);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private bool StartSapiWorker(string text)
        {
            try
            {
                if (Type.GetTypeFromProgID("SAPI.SpVoice") == null)
                {
                    _sapiUnavailable = true;
                    return false;
                }

                Thread thread = new Thread(delegate() { SpeakSapiBlocking(text); });
                thread.IsBackground = true;
                thread.Name = "s7o .tts voice";
                try { thread.SetApartmentState(ApartmentState.STA); } catch { }
                thread.Start();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SpeakSapiBlocking(string text)
        {
            object voice = null;

            try
            {
                if (!CanSpeakByGlobalTts())
                    return;

                Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t == null)
                    return;

                voice = Activator.CreateInstance(t);

                try
                {
                    lock (_sapiLock)
                        _activeSapiVoices.Add(voice);
                }
                catch { }

                t.InvokeMember("Volume", BindingFlags.SetProperty, null, voice, new object[] { EffectiveSapiVolume() });
                t.InvokeMember("Rate", BindingFlags.SetProperty, null, voice, new object[] { Clamp(Rate, -10, 10) });

                // 1 = async. This lets the worker poll for Global TTS OFF and purge speech.
                t.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { text, 1 });

                while (true)
                {
                    if (!CanSpeakByGlobalTts())
                    {
                        TryPurgeSapiVoice(voice);
                        break;
                    }

                    object doneObj = null;
                    try
                    {
                        doneObj = t.InvokeMember("WaitUntilDone", BindingFlags.InvokeMethod, null, voice, new object[] { 50 });
                    }
                    catch
                    {
                        break;
                    }

                    bool done = false;
                    try
                    {
                        if (doneObj is bool)
                            done = (bool)doneObj;
                    }
                    catch { }

                    if (done)
                        break;
                }
            }
            catch { }
            finally
            {
                try
                {
                    if (voice != null)
                    {
                        lock (_sapiLock)
                            _activeSapiVoices.Remove(voice);
                    }
                }
                catch { }

                try
                {
                    if (voice != null && Marshal.IsComObject(voice))
                        Marshal.FinalReleaseComObject(voice);
                }
                catch { }
            }
        }

        private bool LooksLikeOwnEcho(string rawLine, string payload)
        {
            if (string.IsNullOrEmpty(rawLine) || string.IsNullOrEmpty(payload))
                return false;
            if (string.IsNullOrEmpty(_lastLocalPayload))
                return false;
            if (!string.Equals(payload, _lastLocalPayload, StringComparison.Ordinal))
                return false;
            if (_lastLocalSpeakTick == int.MinValue || NowTick() - _lastLocalSpeakTick > LocalEchoWindowMs)
                return false;

            try
            {
                var me = Hud.Game.Me;
                if (me == null)
                    return false;

                string hero = me.HeroName;
                string tag = me.BattleTagAbovePortrait;

                if (!string.IsNullOrEmpty(hero) && rawLine.IndexOf(hero, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (!string.IsNullOrEmpty(tag) && rawLine.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }

            return false;
        }

        private bool IsFreshCachedInput()
        {
            return !string.IsNullOrWhiteSpace(_cachedInputText)
                && _cachedInputTick != int.MinValue
                && NowTick() - _cachedInputTick <= CachedInputMaxAgeMs;
        }

        private bool IsChatInputVisible()
        {
            try { return _chatInput != null && _chatInput.Visible; }
            catch { return false; }
        }

        private string ReadChatInput()
        {
            try
            {
                if (_chatInput == null || !_chatInput.Visible)
                    return null;
                return _chatInput.ReadText(Encoding.UTF8, true);
            }
            catch { return null; }
        }

        private static bool IsEnter(Key key)
        {
            string s = key.ToString();
            return key == Key.Return || s == "NumberPadEnter" || s == "NumpadEnter";
        }

        private static int NowTick()
        {
            return Environment.TickCount;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
