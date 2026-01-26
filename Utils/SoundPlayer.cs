using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Sound channels for concurrent playback.
    /// Each channel has its own waveOut handle and plays completely independently.
    /// </summary>
    public enum SoundChannel
    {
        Movement,    // Footsteps only
        WallBump,    // Wall bump sounds (separate from footsteps to avoid timing conflicts)
        WallTone,    // Wall proximity tones (loopable)
        Beacon       // Audio beacon pings
    }

    /// <summary>
    /// Request for a wall tone in a specific direction (adjacent only).
    /// </summary>
    public struct WallToneRequest
    {
        public SoundPlayer.Direction Direction;

        public WallToneRequest(SoundPlayer.Direction dir)
        {
            Direction = dir;
        }
    }

    /// <summary>
    /// Sound player using Windows waveOut API for true concurrent playback.
    /// Each channel has its own waveOut handle, allowing independent playback
    /// without mixing or timing synchronization issues.
    /// Wall tones use hardware looping for continuous steady tones.
    /// </summary>
    public static class SoundPlayer
    {
        #region waveOut P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;      // WAVE_FORMAT_PCM = 1
            public ushort nChannels;       // 2 for stereo
            public uint nSamplesPerSec;    // 22050
            public uint nAvgBytesPerSec;   // 22050 * 2 (stereo 8-bit)
            public ushort nBlockAlign;     // 2 (stereo 8-bit)
            public ushort wBitsPerSample;  // 8
            public ushort cbSize;          // 0
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0;
        private const uint WHDR_PREPARED = 0x02;
        private const uint WHDR_DONE = 0x01;
        private const uint WHDR_BEGINLOOP = 0x04;
        private const uint WHDR_ENDLOOP = 0x08;

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID,
            ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        #endregion

        #region Channel State

        /// <summary>
        /// State for each independent waveOut channel.
        /// Each channel has its own handle, buffer, and header - completely independent.
        /// </summary>
        private class ChannelState
        {
            public IntPtr WaveOutHandle;
            public IntPtr BufferPtr;        // Unmanaged memory for WAV PCM data
            public WAVEHDR Header;
            public bool IsPlaying;
            public bool HeaderPrepared;
            public bool IsLooping;          // True if currently in hardware loop mode
            public readonly object Lock = new object();
        }

        private static ChannelState[] channels;
        private static WAVEFORMATEX waveFormat;
        private static bool initialized = false;

        // Track current wall tone directions to avoid unnecessary loop restarts
        private static Direction[] currentWallDirections = null;

        #endregion

        #region Pre-cached Sounds

        // Pre-generated wall bump sound (cached) - stored as stereo
        private static byte[] wallBumpWav;

        // Footstep click sound - stored as stereo
        private static byte[] footstepWav;

        // Wall tones with decay (one-shot) - all stereo
        private static byte[] wallToneNorth;
        private static byte[] wallToneSouth;
        private static byte[] wallToneEast;
        private static byte[] wallToneWest;

        // Wall tones without decay (sustain, for looping) - all stereo
        private static byte[] wallToneNorthSustain;
        private static byte[] wallToneSouthSustain;
        private static byte[] wallToneEastSustain;
        private static byte[] wallToneWestSustain;

        // Audio beacon tones - stereo
        private static byte[] beaconNormalWav;  // 400 Hz (North/East/West)
        private static byte[] beaconLowWav;     // 280 Hz (South)

        #endregion

        /// <summary>
        /// Initializes the SoundPlayer by pre-generating tones and opening waveOut handles.
        /// Call this once during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;

            // Pre-generate wall bump tone: deep "thud" with soft attack
            byte[] wallBumpMono = GenerateThudTone(frequency: 27, durationMs: 60, volume: 0.405f);
            wallBumpWav = MonoToStereo(wallBumpMono);

            // Footstep: light click simulating 8/16-bit character steps
            byte[] footstepMono = GenerateClickTone(frequency: 500, durationMs: 25, volume: 0.27f);
            footstepWav = MonoToStereo(footstepMono);

            // Wall tones with frequency-compensated volumes (Fletcher-Munson equal-loudness)
            const float BASE_VOLUME = 0.12f;

            // One-shot tones (with decay) - kept for PlayWallTone single-direction
            wallToneNorth = GenerateStereoTone(330, 150, BASE_VOLUME * 1.00f, 0.5f);
            wallToneSouth = GenerateStereoTone(110, 150, BASE_VOLUME * 0.70f, 0.5f);
            wallToneEast = GenerateStereoTone(200, 150, BASE_VOLUME * 0.85f, 1.0f);
            wallToneWest = GenerateStereoTone(200, 150, BASE_VOLUME * 0.85f, 0.0f);

            // Sustain tones (no decay, for seamless looping) - 200ms buffer
            wallToneNorthSustain = GenerateStereoToneSustain(330, 200, BASE_VOLUME * 1.00f, 0.5f);
            wallToneSouthSustain = GenerateStereoToneSustain(110, 200, BASE_VOLUME * 0.70f, 0.5f);
            wallToneEastSustain = GenerateStereoToneSustain(200, 200, BASE_VOLUME * 0.85f, 1.0f);
            wallToneWestSustain = GenerateStereoToneSustain(200, 200, BASE_VOLUME * 0.85f, 0.0f);

            // Audio beacons (base tones - volume/pan adjusted at playback)
            beaconNormalWav = GenerateStereoTone(400, 60, 0.35f, 0.5f);
            beaconLowWav = GenerateStereoTone(280, 60, 0.35f, 0.5f);

            // Setup wave format (stereo 8-bit 22050Hz - matches our generated tones)
            waveFormat = new WAVEFORMATEX
            {
                wFormatTag = 1,           // PCM
                nChannels = 2,            // Stereo
                nSamplesPerSec = 22050,
                nAvgBytesPerSec = 44100,  // 22050 * 2
                nBlockAlign = 2,          // 2 channels * 1 byte
                wBitsPerSample = 8,
                cbSize = 0
            };

            // Open one waveOut handle per channel (4 channels = 4 independent handles)
            channels = new ChannelState[4];

            for (int i = 0; i < 4; i++)
            {
                channels[i] = new ChannelState();

                int result = waveOutOpen(out channels[i].WaveOutHandle, WAVE_MAPPER,
                    ref waveFormat, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);

                if (result != 0)
                {
                    MelonLogger.Error($"[SoundPlayer] Failed to open waveOut for channel {i}: error {result}");
                    channels[i].WaveOutHandle = IntPtr.Zero;
                }
                else
                {
                    // Pre-allocate buffer for longest sound
                    // Sustain tones: 200ms * 22050 * 2 = 8820 bytes
                    // Mixed sustain (same length): 8820 bytes
                    // Use 16384 for headroom
                    channels[i].BufferPtr = Marshal.AllocHGlobal(16384);
                }
            }

            initialized = true;
            MelonLogger.Msg("[SoundPlayer] Initialized with 4 independent waveOut channels (looping supported)");
        }

        /// <summary>
        /// Plays a sound on the specified channel using waveOut API.
        /// Each channel plays independently - no waiting, no mixing, no batching.
        /// When loop=true, uses hardware looping for continuous playback until StopChannel is called.
        /// </summary>
        private static void PlayOnChannel(byte[] wavData, SoundChannel channel, bool loop = false)
        {
            if (wavData == null || !initialized) return;

            int channelIndex = (int)channel;
            var state = channels[channelIndex];
            if (state?.WaveOutHandle == IntPtr.Zero) return;

            lock (state.Lock)
            {
                // If still playing, reset (stops current sound on THIS channel only)
                if (state.IsPlaying || state.HeaderPrepared)
                {
                    waveOutReset(state.WaveOutHandle);

                    if (state.HeaderPrepared)
                    {
                        waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());
                        state.HeaderPrepared = false;
                    }
                    state.IsPlaying = false;
                    state.IsLooping = false;
                }

                // Skip WAV header (44 bytes), copy PCM data to unmanaged buffer
                const int WAV_HEADER_SIZE = 44;
                if (wavData.Length <= WAV_HEADER_SIZE) return;

                int dataLength = wavData.Length - WAV_HEADER_SIZE;
                Marshal.Copy(wavData, WAV_HEADER_SIZE, state.BufferPtr, dataLength);

                // Setup WAVEHDR with optional loop flags
                state.Header = new WAVEHDR
                {
                    lpData = state.BufferPtr,
                    dwBufferLength = (uint)dataLength,
                    dwBytesRecorded = 0,
                    dwUser = IntPtr.Zero,
                    dwFlags = loop ? (WHDR_BEGINLOOP | WHDR_ENDLOOP) : 0,
                    dwLoops = loop ? 0xFFFFFFFF : 0,
                    lpNext = IntPtr.Zero,
                    reserved = IntPtr.Zero
                };

                // Prepare and write
                int prepResult = waveOutPrepareHeader(state.WaveOutHandle, ref state.Header,
                    (uint)Marshal.SizeOf<WAVEHDR>());

                if (prepResult == 0)
                {
                    state.HeaderPrepared = true;

                    int writeResult = waveOutWrite(state.WaveOutHandle, ref state.Header,
                        (uint)Marshal.SizeOf<WAVEHDR>());

                    if (writeResult == 0)
                    {
                        state.IsPlaying = true;
                        state.IsLooping = loop;
                    }
                    else
                    {
                        // Write failed, unprepare immediately
                        waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());
                        state.HeaderPrepared = false;
                    }
                }
            }
        }

        /// <summary>
        /// Stops playback on a specific channel. Used to halt looping tones.
        /// </summary>
        public static void StopChannel(SoundChannel channel)
        {
            if (!initialized || channels == null) return;

            int channelIndex = (int)channel;
            var state = channels[channelIndex];
            if (state?.WaveOutHandle == IntPtr.Zero) return;

            lock (state.Lock)
            {
                if (state.IsPlaying || state.HeaderPrepared)
                {
                    waveOutReset(state.WaveOutHandle);

                    if (state.HeaderPrepared)
                    {
                        waveOutUnprepareHeader(state.WaveOutHandle, ref state.Header,
                            (uint)Marshal.SizeOf<WAVEHDR>());
                        state.HeaderPrepared = false;
                    }
                    state.IsPlaying = false;
                    state.IsLooping = false;
                }
            }
        }

        /// <summary>
        /// Shuts down all waveOut channels and frees resources.
        /// Call this when the mod is unloaded.
        /// </summary>
        public static void Shutdown()
        {
            if (!initialized || channels == null) return;

            for (int i = 0; i < 4; i++)
            {
                if (channels[i] != null)
                {
                    lock (channels[i].Lock)
                    {
                        if (channels[i].WaveOutHandle != IntPtr.Zero)
                        {
                            waveOutReset(channels[i].WaveOutHandle);

                            if (channels[i].HeaderPrepared)
                            {
                                waveOutUnprepareHeader(channels[i].WaveOutHandle, ref channels[i].Header,
                                    (uint)Marshal.SizeOf<WAVEHDR>());
                                channels[i].HeaderPrepared = false;
                            }

                            waveOutClose(channels[i].WaveOutHandle);
                            channels[i].WaveOutHandle = IntPtr.Zero;
                        }

                        if (channels[i].BufferPtr != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(channels[i].BufferPtr);
                            channels[i].BufferPtr = IntPtr.Zero;
                        }
                    }
                }
            }

            currentWallDirections = null;
            initialized = false;
            MelonLogger.Msg("[SoundPlayer] All waveOut channels closed");
        }

        #region Public Playback Methods

        /// <summary>
        /// Plays the wall bump sound effect on the WallBump channel.
        /// </summary>
        public static void PlayWallBump()
        {
            if (wallBumpWav == null) return;
            PlayOnChannel(wallBumpWav, SoundChannel.WallBump);
        }

        /// <summary>
        /// Plays the footstep click sound on the Movement channel.
        /// </summary>
        public static void PlayFootstep()
        {
            if (footstepWav == null) return;
            PlayOnChannel(footstepWav, SoundChannel.Movement);
        }

        /// <summary>
        /// Cardinal direction enum for wall tones.
        /// </summary>
        public enum Direction
        {
            North,
            South,
            East,
            West
        }

        /// <summary>
        /// Plays a one-shot wall proximity tone for the given direction.
        /// Uses WallTone channel - independent from movement and beacon channels.
        /// </summary>
        public static void PlayWallTone(Direction dir)
        {
            byte[] tone = null;

            switch (dir)
            {
                case Direction.North: tone = wallToneNorth; break;
                case Direction.South: tone = wallToneSouth; break;
                case Direction.East: tone = wallToneEast; break;
                case Direction.West: tone = wallToneWest; break;
            }

            if (tone == null) return;
            PlayOnChannel(tone, SoundChannel.WallTone);
        }

        /// <summary>
        /// Plays one-shot wall tones (multiple directions mixed).
        /// </summary>
        public static void PlayWallTones(WallToneRequest[] requests)
        {
            if (requests == null || requests.Length == 0) return;

            var tonesToMix = new List<byte[]>();
            foreach (var req in requests)
            {
                byte[] tone = null;
                switch (req.Direction)
                {
                    case Direction.North: tone = wallToneNorth; break;
                    case Direction.South: tone = wallToneSouth; break;
                    case Direction.East: tone = wallToneEast; break;
                    case Direction.West: tone = wallToneWest; break;
                }
                if (tone != null)
                    tonesToMix.Add(tone);
            }

            if (tonesToMix.Count == 0) return;

            if (tonesToMix.Count == 1)
            {
                PlayOnChannel(tonesToMix[0], SoundChannel.WallTone);
                return;
            }

            byte[] mixedWav = MixWavFiles(tonesToMix);
            if (mixedWav != null)
            {
                PlayOnChannel(mixedWav, SoundChannel.WallTone);
            }
        }

        /// <summary>
        /// Plays wall tones as a continuous looping sound.
        /// Mixes sustain tones for all given directions and loops them.
        /// Only restarts the loop if directions have changed.
        /// Pass empty/null to stop wall tones.
        /// </summary>
        public static void PlayWallTonesLooped(Direction[] directions)
        {
            if (directions == null || directions.Length == 0)
            {
                StopWallTone();
                return;
            }

            // Skip restart if directions haven't changed
            if (DirectionsMatch(currentWallDirections, directions))
                return;

            // Store new directions
            currentWallDirections = (Direction[])directions.Clone();

            // Collect sustain tones for mixing
            var tonesToMix = new List<byte[]>();
            foreach (var dir in directions)
            {
                byte[] tone = null;
                switch (dir)
                {
                    case Direction.North: tone = wallToneNorthSustain; break;
                    case Direction.South: tone = wallToneSouthSustain; break;
                    case Direction.East: tone = wallToneEastSustain; break;
                    case Direction.West: tone = wallToneWestSustain; break;
                }
                if (tone != null)
                    tonesToMix.Add(tone);
            }

            if (tonesToMix.Count == 0)
            {
                StopWallTone();
                return;
            }

            // Single tone or mix multiple
            byte[] loopBuffer;
            if (tonesToMix.Count == 1)
            {
                loopBuffer = tonesToMix[0];
            }
            else
            {
                loopBuffer = MixWavFiles(tonesToMix);
            }

            if (loopBuffer != null)
            {
                PlayOnChannel(loopBuffer, SoundChannel.WallTone, loop: true);
            }
        }

        /// <summary>
        /// Stops the continuous wall tone loop.
        /// </summary>
        public static void StopWallTone()
        {
            currentWallDirections = null;
            StopChannel(SoundChannel.WallTone);
        }

        /// <summary>
        /// Plays an audio beacon on Channel 3 (Beacon).
        /// Beacon has its own waveOut handle - does NOT interrupt other channels.
        /// </summary>
        public static void PlayBeacon(bool isSouth, float pan, float volumeScale)
        {
            try
            {
                int frequency = isSouth ? 280 : 400;
                float adjustedVolume = Math.Max(0.10f, Math.Min(0.50f, volumeScale));

                byte[] dynamicTone = GenerateStereoTone(frequency, 60, adjustedVolume, pan);
                PlayOnChannel(dynamicTone, SoundChannel.Beacon);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SoundPlayer] Error playing beacon: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Checks if two direction arrays contain the same directions (order-independent).
        /// </summary>
        private static bool DirectionsMatch(Direction[] a, Direction[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            // Sort both and compare (small arrays, max 4 elements)
            var sortedA = (Direction[])a.Clone();
            var sortedB = (Direction[])b.Clone();
            Array.Sort(sortedA);
            Array.Sort(sortedB);

            for (int i = 0; i < sortedA.Length; i++)
            {
                if (sortedA[i] != sortedB[i]) return false;
            }
            return true;
        }

        #endregion

        #region Tone Generation

        /// <summary>
        /// Converts a mono WAV to stereo by duplicating each sample to both channels.
        /// </summary>
        private static byte[] MonoToStereo(byte[] monoWav)
        {
            if (monoWav == null || monoWav.Length < 44) return monoWav;

            using (var reader = new BinaryReader(new MemoryStream(monoWav)))
            {
                reader.ReadBytes(4);  // "RIFF"
                reader.ReadInt32();   // file size
                reader.ReadBytes(4);  // "WAVE"
                reader.ReadBytes(4);  // "fmt "
                int fmtSize = reader.ReadInt32();
                reader.ReadInt16();   // audio format
                int channels = reader.ReadInt16();
                int sampleRate = reader.ReadInt32();
                reader.ReadInt32();   // byte rate
                reader.ReadInt16();   // block align
                int bitsPerSample = reader.ReadInt16();

                if (fmtSize > 16)
                    reader.ReadBytes(fmtSize - 16);

                reader.ReadBytes(4);  // "data"
                int dataSize = reader.ReadInt32();

                if (channels == 2)
                    return monoWav;

                byte[] monoData = reader.ReadBytes(dataSize);

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    int stereoDataSize = dataSize * 2;

                    writer.Write(new[] { 'R', 'I', 'F', 'F' });
                    writer.Write(36 + stereoDataSize);
                    writer.Write(new[] { 'W', 'A', 'V', 'E' });

                    writer.Write(new[] { 'f', 'm', 't', ' ' });
                    writer.Write(16);
                    writer.Write((short)1);           // PCM
                    writer.Write((short)2);           // Stereo
                    writer.Write(sampleRate);
                    writer.Write(sampleRate * 2);     // Byte rate (stereo)
                    writer.Write((short)2);           // Block align
                    writer.Write((short)8);           // Bits per sample

                    writer.Write(new[] { 'd', 'a', 't', 'a' });
                    writer.Write(stereoDataSize);

                    for (int i = 0; i < monoData.Length; i++)
                    {
                        writer.Write(monoData[i]);  // Left
                        writer.Write(monoData[i]);  // Right
                    }

                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Generates a WAV file containing a "thud" sound with soft attack and noise mix.
        /// </summary>
        private static byte[] GenerateThudTone(int frequency, int durationMs, float volume)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;
            int attackSamples = samples / 4;
            var random = new System.Random(42);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);     // PCM
                writer.Write((short)1);     // Mono
                writer.Write(sampleRate);
                writer.Write(sampleRate);
                writer.Write((short)1);
                writer.Write((short)8);

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples);

                double filteredNoise = 0;

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;

                    double attackLinear = Math.Min(1.0, (double)i / attackSamples);
                    double attack = attackLinear * attackLinear;
                    double decay = (double)(samples - i) / samples;
                    double envelope = attack * decay;

                    double sine = Math.Sin(2 * Math.PI * frequency * t);
                    double rawNoise = (random.NextDouble() * 2 - 1);
                    filteredNoise = filteredNoise * 0.9 + rawNoise * 0.1;
                    double noise = filteredNoise * 0.3 * attack;
                    double value = (sine * 0.7 + noise) * volume * envelope;

                    writer.Write((byte)((value * 127) + 128));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a short click/tap sound for footsteps using filtered noise burst.
        /// </summary>
        private static byte[] GenerateClickTone(int frequency, int durationMs, float volume)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;
            var random = new System.Random(42);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);     // PCM
                writer.Write((short)1);     // Mono
                writer.Write(sampleRate);
                writer.Write(sampleRate);
                writer.Write((short)1);
                writer.Write((short)8);

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples);

                for (int i = 0; i < samples; i++)
                {
                    double decay = Math.Exp(-10.0 * i / samples);
                    double noise = (random.NextDouble() * 2 - 1) * volume * decay;
                    writer.Write((byte)((noise * 127) + 128));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a stereo WAV tone with constant-power panning and decay envelope.
        /// Used for one-shot sounds (beacons, single wall tone pings).
        /// </summary>
        private static byte[] GenerateStereoTone(int frequency, int durationMs, float volume, float pan)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;

            double panAngle = pan * Math.PI / 2;
            float leftVol = volume * (float)Math.Cos(panAngle);
            float rightVol = volume * (float)Math.Sin(panAngle);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)2);           // Stereo
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);     // Byte rate
                writer.Write((short)2);           // Block align
                writer.Write((short)8);           // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples * 2);

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;

                    int attackSamples = samples / 10;
                    double attack = Math.Min(1.0, (double)i / attackSamples);
                    double decay = (double)(samples - i) / samples;
                    double envelope = attack * decay;

                    double sineValue = Math.Sin(2 * Math.PI * frequency * t) * envelope;

                    writer.Write((byte)((sineValue * leftVol * 127) + 128));
                    writer.Write((byte)((sineValue * rightVol * 127) + 128));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Generates a stereo WAV tone with sustain (no decay) for seamless hardware looping.
        /// Quick attack to avoid pop, then full amplitude for the rest of the buffer.
        /// The buffer loops seamlessly because it sustains at full level.
        /// </summary>
        private static byte[] GenerateStereoToneSustain(int frequency, int durationMs, float volume, float pan)
        {
            int sampleRate = 22050;
            int samples = (sampleRate * durationMs) / 1000;

            double panAngle = pan * Math.PI / 2;
            float leftVol = volume * (float)Math.Cos(panAngle);
            float rightVol = volume * (float)Math.Sin(panAngle);

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);           // PCM
                writer.Write((short)2);           // Stereo
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);     // Byte rate
                writer.Write((short)2);           // Block align
                writer.Write((short)8);           // Bits per sample

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples * 2);

                // Attack over first 5% of samples to avoid click at start
                int attackSamples = Math.Max(1, samples / 20);

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;

                    // Quick attack, then full sustain - NO decay
                    double envelope = Math.Min(1.0, (double)i / attackSamples);

                    double sineValue = Math.Sin(2 * Math.PI * frequency * t) * envelope;

                    writer.Write((byte)((sineValue * leftVol * 127) + 128));
                    writer.Write((byte)((sineValue * rightVol * 127) + 128));
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Mixes multiple WAV files into a single stereo WAV.
        /// Used for playing multiple wall tones in different directions simultaneously.
        /// </summary>
        private static byte[] MixWavFiles(List<byte[]> wavFiles)
        {
            if (wavFiles == null || wavFiles.Count == 0) return null;

            const int HEADER_SIZE = 44;

            int maxDataLength = 0;
            foreach (var wav in wavFiles)
            {
                if (wav.Length > HEADER_SIZE)
                {
                    int dataLen = wav.Length - HEADER_SIZE;
                    if (dataLen > maxDataLength)
                        maxDataLength = dataLen;
                }
            }

            if (maxDataLength == 0) return null;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                int sampleRate = 22050;

                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + maxDataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });

                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);     // PCM
                writer.Write((short)2);     // Stereo
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)8);

                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(maxDataLength);

                for (int i = 0; i < maxDataLength; i++)
                {
                    int mixedValue = 0;
                    int count = 0;

                    foreach (var wav in wavFiles)
                    {
                        int pos = HEADER_SIZE + i;
                        if (pos < wav.Length)
                        {
                            mixedValue += (wav[pos] - 128);
                            count++;
                        }
                    }

                    if (count > 1)
                    {
                        double headroom = 1.0 / Math.Sqrt(count);
                        mixedValue = (int)(mixedValue * headroom);
                    }

                    mixedValue = Math.Max(-127, Math.Min(127, mixedValue));
                    writer.Write((byte)(mixedValue + 128));
                }

                return ms.ToArray();
            }
        }

        #endregion
    }
}
