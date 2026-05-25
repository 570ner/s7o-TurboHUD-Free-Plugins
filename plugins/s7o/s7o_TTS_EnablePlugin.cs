using System;
using System.Reflection;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Small FreeHUD native TTS lifecycle helper.
    //
    // Release defaults are cleanup-only: this plugin does not force-enable
    // Hud.Sound TTS or override volume because HUD MENU owns Global TTS
    // settings. Its main purpose is to stop/dispose FreeHUD's internal
    // SpeechSynthesizer on process exit to avoid shutdown lag.
    //
    // Manual source-edit overrides remain available through EnableTts,
    // UseConstantVolume, and ConstantVolume if needed for troubleshooting.
    //
    // Why the ProcessExit handler matters:
    //   Hud.Sound.Speak() lazy-initializes a System.Speech.Synthesis.SpeechSynthesizer
    //   the first time it is called.  The synthesizer creates a COM STA background
    //   thread.  If the process exits without calling SpeechSynthesizer.Dispose(),
    //   .NET's finalizer tries to marshal cleanup back to that STA thread and blocks
    //   waiting for it — causing the 5–30 second lag observed on HUD close.
    //
    //   Calling Dispose() before exit releases the COM object, the STA thread exits
    //   cleanly, and the lag disappears.  We find the synthesizer via reflection
    //   because TurboHUD Free does not expose a public cleanup API.
    //   Everything is wrapped in try/catch so a reflection miss cannot break anything.

    public class s7o_TtsConfigPlugin : BasePlugin, ICustomizer
    {
        public bool EnableTts { get; set; }
        public bool UseConstantVolume { get; set; }
        public int ConstantVolume { get; set; }

        // When true, attempts to Dispose the underlying SpeechSynthesizer on process exit.
        // Set false only if you observe instability from the cleanup (very unlikely).
        public bool CleanupSynthesizerOnExit { get; set; }

        private bool _exitHandlerRegistered;

        public s7o_TtsConfigPlugin()
        {
            Enabled = true;

            // HUD MENU owns Global TTS enable/volume now.
            // This helper should not force-enable native Hud.Sound TTS or override
            // volume during normal release use.
            EnableTts             = false;
            UseConstantVolume     = false;
            ConstantVolume        = 50;

            // Keep this enabled: it prevents FreeHUD/native TTS shutdown lag by
            // stopping and disposing the internal SpeechSynthesizer on process exit.
            CleanupSynthesizerOnExit = true;

            _exitHandlerRegistered = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
        }

        public void Customize()
        {
            // Register the process-exit cleanup handler exactly once.
            // This should not depend on Hud.Sound already being initialized.
            if (CleanupSynthesizerOnExit && !_exitHandlerRegistered)
            {
                try
                {
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    _exitHandlerRegistered = true;
                }
                catch { }
            }

            if (Hud == null || Hud.Sound == null)
                return;

            // Enable TTS if requested.
            try
            {
                if (EnableTts)
                    Hud.Sound.IsSpeakEnabled = true;
            }
            catch { }

            // Set constant volume only if requested.
            try
            {
                if (UseConstantVolume)
                {
                    Hud.Sound.VolumeMode    = VolumeMode.Constant;
                    Hud.Sound.ConstantVolume = ConstantVolume;
                }
            }
            catch { }
        }

        // -----------------------------------------------------------------------
        // Process-exit cleanup
        // -----------------------------------------------------------------------

        private void OnProcessExit(object sender, EventArgs e)
        {
            // Step 1 — stop any in-progress speech so the STA thread is idle
            // before we try to dispose the synthesizer.
            try { Hud?.Sound?.StopSpeak(); }
            catch { }

            // Step 2 — find and dispose the SpeechSynthesizer via reflection.
            // TurboHUD Free holds it as a private field inside its ISound implementation.
            // We try three strategies so we are robust to field name differences
            // across FreeHUD versions.
            try { DisposeSynthesizerByTypeName(); }
            catch { }

            try { DisposeSynthesizerByAllDisposableFields(); }
            catch { }
        }

        // Strategy A: find a field whose declared type name contains "Speech" or "Synth".
        // This is the most targeted match.
        private void DisposeSynthesizerByTypeName()
        {
            var soundObj = Hud?.Sound;
            if (soundObj == null)
                return;

            var fields = soundObj.GetType().GetFields(
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            foreach (var field in fields)
            {
                string typeName = field.FieldType.FullName ?? field.FieldType.Name ?? "";

                bool looksLikeSynth =
                    typeName.IndexOf("SpeechSynthesizer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("SpVoice",           StringComparison.OrdinalIgnoreCase) >= 0;

                if (!looksLikeSynth)
                    continue;

                var value = field.GetValue(soundObj) as IDisposable;
                if (value == null)
                    continue;

                value.Dispose();
                return; // one synthesizer is enough
            }
        }

        // Strategy B: dispose every IDisposable private field on Hud.Sound
        // whose declared type lives in the System.Speech namespace.
        // Catches the synthesizer even if the field name or wrapper type is unexpected.
        private void DisposeSynthesizerByAllDisposableFields()
        {
            var soundObj = Hud?.Sound;
            if (soundObj == null)
                return;

            var fields = soundObj.GetType().GetFields(
                BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                string ns = field.FieldType.Namespace ?? "";

                if (ns.IndexOf("System.Speech", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var value = field.GetValue(soundObj) as IDisposable;
                if (value == null)
                    continue;

                try { value.Dispose(); }
                catch { }
            }
        }
    }
}
