using MelonLoader;
using UnityEngine;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    public class AudioAwareAnnouncementManager
    {
        private static AudioAwareAnnouncementManager _instance;
        public static AudioAwareAnnouncementManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AudioAwareAnnouncementManager();
                }
                return _instance;
            }
        }

        private class QueuedAnnouncement
        {
            public string Text { get; set; }
            public bool Interrupt { get; set; }
            public float QueueTime { get; set; }
            public bool HasWaitedForAudio { get; set; }
        }

        private Queue<QueuedAnnouncement> announcementQueue = new Queue<QueuedAnnouncement>();
        private float lastAudioStopTime = 0f;
        private bool wasPlayingLastFrame = false;
        private const float DELAY_AFTER_AUDIO_STOPS = 0.4f; // 400ms delay after audio stops

        private Tolk.Tolk tolkInstance;

        public void Initialize(Tolk.Tolk tolk)
        {
            tolkInstance = tolk;
        }

        public void Update()
        {
            // Only process if there's something in the queue
            if (announcementQueue.Count == 0)
            {
                return;
            }

            // Check if audio is currently playing
            bool isAudioPlaying = IsVoiceAudioPlaying();

            // Detect when audio stops
            if (wasPlayingLastFrame && !isAudioPlaying)
            {
                lastAudioStopTime = Time.time;
                MelonLogger.Msg($"[AudioAware] Voice audio stopped");
            }

            wasPlayingLastFrame = isAudioPlaying;

            // Process queue only when audio is not playing
            if (!isAudioPlaying)
            {
                // Wait a short delay after audio stops before speaking
                float timeSinceAudioStopped = Time.time - lastAudioStopTime;
                if (timeSinceAudioStopped >= DELAY_AFTER_AUDIO_STOPS)
                {
                    ProcessQueue();
                }
            }
        }

        private bool IsVoiceAudioPlaying()
        {
            try
            {
                // Get the SoundManager instance
                var soundManager = Il2CppChickenPolice.SoundManager.Instance;
                if (soundManager == null)
                {
                    return false;
                }

                // Get the VoiceOver channel's AudioSource
                var voiceOverSource = soundManager.GetChannel(Il2CppChickenPolice.SoundChannel.VoiceOver);
                if (voiceOverSource != null && voiceOverSource.isPlaying)
                {
                    return true;
                }

                return false;
            }
            catch (System.Exception)
            {
                // Silently handle errors - audio system may not be initialized yet
                return false;
            }
        }

        public void QueueAnnouncement(string text, bool interrupt = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // Check if voiceover audio is currently playing
            bool isVoiceoverPlaying = IsVoiceAudioPlaying();

            // If no voiceover is playing, speak immediately (e.g., in menus)
            if (!isVoiceoverPlaying)
            {
                // Speak immediately without queueing
                if (tolkInstance != null && tolkInstance.IsLoaded())
                {
                    tolkInstance.Speak(text, interrupt);
                    // MelonLogger.Msg($"[AudioAware] Speaking immediately (no voiceover): {text}");
                }
                return;
            }

            // Voiceover is playing, so queue the announcement
            // Check for duplicates already in queue
            foreach (var existing in announcementQueue)
            {
                if (existing.Text == text)
                {
                    return; // Skip duplicate
                }
            }

            var announcement = new QueuedAnnouncement
            {
                Text = text,
                Interrupt = interrupt,
                QueueTime = Time.time,
                HasWaitedForAudio = false
            };

            announcementQueue.Enqueue(announcement);
            MelonLogger.Msg($"[AudioAware] Queued (voiceover active): {text} (Queue size: {announcementQueue.Count})");
        }

        private void ProcessQueue()
        {
            if (announcementQueue.Count == 0)
            {
                return;
            }

            // Dequeue and speak the next announcement (no IsSpeaking check - causes deadlocks)
            var announcement = announcementQueue.Dequeue();
            // MelonLogger.Msg($"[AudioAware] Speaking from queue: {announcement.Text}");

            // Speak directly using the tolk instance
            if (tolkInstance != null && tolkInstance.IsLoaded())
            {
                // Use the original interrupt value from when it was queued
                tolkInstance.Speak(announcement.Text, announcement.Interrupt);
            }
            else
            {
                MelonLogger.Warning($"[AudioAware] Cannot speak - Tolk not ready");
            }
        }

        public int GetQueueSize()
        {
            return announcementQueue.Count;
        }

        public void ClearQueue()
        {
            announcementQueue.Clear();
        }

        /// <summary>
        /// Check if voice audio is currently playing
        /// This can be used by other systems to decide whether to queue announcements
        /// </summary>
        public bool IsDialogueAudioPlaying()
        {
            return IsVoiceAudioPlaying();
        }
    }
}
