using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Field;
using FFI_ScreenReader.Patches;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Manages wall tone and audio beacon coroutine loops.
    /// Handles suppression timing during map transitions and scene loads.
    /// </summary>
    public class AudioLoopManager
    {
        private readonly FFI_ScreenReaderMod mod;
        private readonly EntityScanner entityScanner;
        private readonly WaypointNavigator waypointNavigator;

        // Coroutine state
        private IEnumerator wallToneCoroutine;
        private IEnumerator beaconCoroutine;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;

        // Beacon navigation constants — proximity-based interval modulation.
        // Mode A (valid path): 1.0s at 31.5 tiles (pathfinding limit) → 0.2s at 2 tiles; silent at ≤1 tile.
        // Mode B (no valid path / out of range): 1.0s at ≥100 tiles → 0.5s at 32 tiles; halved pitch.
        private const float MODE_A_INTERVAL_FAR  = 1.0f;
        private const float MODE_A_INTERVAL_NEAR = 0.2f;
        private const float MODE_A_FAR_TILES     = 31.5f;
        private const float MODE_A_NEAR_TILES    = 2.0f;
        private const float BEACON_STOP_TILES    = 1.0f;
        private const float MODE_B_INTERVAL_FAR  = 1.0f;
        private const float MODE_B_INTERVAL_NEAR = 0.5f;
        private const float MODE_B_FAR_TILES     = 100f;
        private const float MODE_B_NEAR_TILES    = 32f;
        private const float TILE_SIZE            = 16f;

        // Beacon state
        private bool beaconSilenced = false;
        private object lastBeaconTarget = null;
        private float nextBeaconTime = 0f;

        // Map transition suppression
        private int wallToneMapId = -1;
        private float wallToneSuppressedUntil = 0f;
        private float beaconSuppressedUntil = 0f;
        private float lastBeaconPlayedAt = 0f;

        // Reusable direction list buffer to avoid per-cycle allocations
        private static readonly List<SoundPlayer.Direction> wallDirectionsBuffer = new List<SoundPlayer.Direction>(4);

        // Pre-cached direction vectors for map exit checks (avoids per-cycle Vector3 allocations)
        private static readonly Vector3 DirNorth = new Vector3(0, 16, 0);
        private static readonly Vector3 DirSouth = new Vector3(0, -16, 0);
        private static readonly Vector3 DirEast = new Vector3(16, 0, 0);
        private static readonly Vector3 DirWest = new Vector3(-16, 0, 0);

        internal AudioLoopManager(FFI_ScreenReaderMod mod, EntityScanner scanner, WaypointNavigator waypointNavigator)
        {
            this.mod = mod;
            this.entityScanner = scanner;
            this.waypointNavigator = waypointNavigator;
        }

        /// <summary>
        /// Starts the wall tone coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        public void StartWallToneLoop()
        {
            if (!mod.IsWallTonesEnabled()) return;
            if (wallToneCoroutine != null) return;
            wallToneCoroutine = WallToneLoop();
            CoroutineManager.StartManaged(wallToneCoroutine);
        }

        /// <summary>
        /// Stops the wall tone coroutine loop and silences any playing tone.
        /// </summary>
        public void StopWallToneLoop()
        {
            if (wallToneCoroutine != null)
            {
                CoroutineManager.StopManaged(wallToneCoroutine);
                wallToneCoroutine = null;
            }
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Starts the audio beacon coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        public void StartBeaconLoop()
        {
            if (!mod.IsAudioBeaconsEnabled()) return;
            if (beaconCoroutine != null) return;
            beaconCoroutine = BeaconLoop();
            CoroutineManager.StartManaged(beaconCoroutine);
        }

        /// <summary>
        /// Stops the audio beacon coroutine loop.
        /// </summary>
        public void StopBeaconLoop()
        {
            if (beaconCoroutine != null)
            {
                CoroutineManager.StopManaged(beaconCoroutine);
                beaconCoroutine = null;
            }
            beaconSilenced = false;
            lastBeaconTarget = null;
        }

        /// <summary>
        /// Forces the beacon to ping on the next loop iteration and clears any silence latch.
        /// Called by the pathfinding commands when beacon navigation mode is on.
        /// </summary>
        public void RestartBeacon()
        {
            beaconSilenced = false;
            nextBeaconTime = 0f;
        }

        /// <summary>
        /// Stops both wall tone and beacon loops.
        /// </summary>
        public void StopAll()
        {
            StopWallToneLoop();
            StopBeaconLoop();
        }

        /// <summary>
        /// Suppresses wall tones and beacons for the specified duration.
        /// Used during scene loads and map transitions.
        /// </summary>
        public void SuppressBriefly(float seconds)
        {
            float until = Time.time + seconds;
            wallToneSuppressedUntil = until;
            beaconSuppressedUntil = until;
        }

        /// <summary>
        /// Starts loops that are currently enabled. Called after scene loads.
        /// </summary>
        public void StartIfEnabled()
        {
            if (mod.IsWallTonesEnabled()) StartWallToneLoop();
            if (mod.IsAudioBeaconsEnabled()) StartBeaconLoop();
        }

        /// <summary>
        /// Coroutine loop that checks for adjacent walls every 100ms and plays looping tones.
        /// Uses manual time-based waiting because WaitForSeconds doesn't work through IL2CPP wrapper.
        /// </summary>
        private IEnumerator WallToneLoop()
        {
            float nextCheckTime = Time.time + 0.3f;  // Delay first check by 300ms for scene stability

            while (mod.IsWallTonesEnabled())
            {
                if (Time.time < nextCheckTime)
                {
                    yield return null;
                    continue;
                }
                nextCheckTime = Time.time + WALL_TONE_LOOP_INTERVAL;

                try
                {
                    float currentTime = Time.time;

                    // Detect sub-map transitions and suppress tones briefly
                    int currentMapId = FFI_ScreenReaderMod.GetCurrentMapId();
                    if (currentMapId > 0 && wallToneMapId > 0 && currentMapId != wallToneMapId)
                    {
                        wallToneSuppressedUntil = currentTime + 1.0f;
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                    }
                    if (currentMapId > 0)
                        wallToneMapId = currentMapId;

                    if (currentTime < wallToneSuppressedUntil)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    if (MapTransitionPatches.IsScreenFading)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    // Silence when any menu or battle is active, or while a mod dialog is open.
                    // SuppressGameInput covers mod menu / TextInputWindow / ConfirmationDialog —
                    // their A* work would stutter keyboard polling in TextInputWindow.
                    if (!ControllerRouter.IsFieldActive || ControllerRouter.SuppressGameInput)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var player = FFI_ScreenReaderMod.GetFieldPlayer();
                    if (player == null)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var walls = FieldNavigationHelper.GetNearbyWallsWithDistance(player);
                    var mapExitPositions = entityScanner?.GetMapExitPositions();
                    Vector3 playerPos = player.transform.localPosition;

                    // Reuse static buffer to avoid per-cycle allocations
                    wallDirectionsBuffer.Clear();

                    if (walls.NorthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirNorth, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.North);

                    if (walls.SouthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirSouth, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.South);

                    if (walls.EastDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirEast, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.East);

                    if (walls.WestDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirWest, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.West);

                    // Pass buffer directly (IList<Direction>) - no ToArray() allocation
                    SoundPlayer.PlayWallTonesLooped(wallDirectionsBuffer);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[WallTones] Error: {ex.Message}");
                }
            }

            // Clean up when exiting
            wallToneCoroutine = null;
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Coroutine loop that plays proximity-based audio beacon pings.
        /// Interval shortens as the player nears the selected entity.
        /// Mode A (valid path): normal pitch, 1.0s→0.2s over 31.5→2 tiles, silent at ≤1 tile.
        /// Mode B (no valid path): halved pitch, 1.0s→0.5s over 100→32 tiles, no silence latch.
        /// Uses manual time-based waiting because WaitForSeconds doesn't work through IL2CPP wrapper.
        /// </summary>
        private IEnumerator BeaconLoop()
        {
            nextBeaconTime = Time.time + 0.3f;  // Delay first beacon by 300ms for scene stability

            while (mod.IsAudioBeaconsEnabled())
            {
                if (Time.time < nextBeaconTime)
                {
                    yield return null;
                    continue;
                }

                // Suppress beacons briefly after scene load
                if (Time.time < beaconSuppressedUntil)
                {
                    nextBeaconTime = Time.time + 0.1f;
                    continue;
                }

                // Silence when any menu or battle is active, or while a mod dialog is open.
                // The A* tick is expensive — running it during TextInputWindow typing
                // stutters the GetAsyncKeyState poll enough to drop characters.
                if (!ControllerRouter.IsFieldActive || ControllerRouter.SuppressGameInput)
                {
                    nextBeaconTime = Time.time + 0.1f;
                    continue;
                }

                try
                {
                    object targetRef = null;
                    Vector3 targetPos = Vector3.zero;
                    switch (NavigationTargetTracker.LastKind)
                    {
                        case NavigationTargetTracker.Kind.Entity:
                            var e = entityScanner?.CurrentEntity;
                            if (e != null) { targetRef = e; targetPos = e.Position; }
                            break;
                        case NavigationTargetTracker.Kind.Waypoint:
                            var w = waypointNavigator?.SelectedWaypoint;
                            if (w != null) { targetRef = w; targetPos = w.Position; }
                            break;
                    }

                    if (targetRef == null)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    // Selection change clears the silence latch so new targets always ping.
                    if (!ReferenceEquals(targetRef, lastBeaconTarget))
                    {
                        beaconSilenced = false;
                        lastBeaconTarget = targetRef;
                    }

                    var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                    if (playerController?.fieldPlayer == null)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;

                    // Sanity check: skip if positions look invalid (garbage data during load)
                    if (float.IsNaN(playerPos.x) || float.IsNaN(targetPos.x) ||
                        Mathf.Abs(playerPos.x) > 10000f || Mathf.Abs(targetPos.x) > 10000f)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    float distTiles = Vector3.Distance(playerPos, targetPos) / TILE_SIZE;

                    // Mode selection — expensive (A* per beacon tick) but only 1–5 Hz.
                    bool pathValid;
                    try
                    {
                        var pathInfo = FieldNavigationHelper.FindPathTo(
                            playerPos, targetPos,
                            playerController.mapHandle,
                            playerController.fieldPlayer);
                        pathValid = pathInfo.Success;
                    }
                    catch
                    {
                        pathValid = false;
                    }

                    float interval;
                    bool lowPitch;
                    if (pathValid)
                    {
                        // Mode A: valid path
                        if (distTiles <= BEACON_STOP_TILES)
                        {
                            beaconSilenced = true;
                            nextBeaconTime = Time.time + 0.2f;
                            continue;
                        }
                        float t = Mathf.Clamp01((distTiles - MODE_A_NEAR_TILES) /
                                                (MODE_A_FAR_TILES - MODE_A_NEAR_TILES));
                        interval = Mathf.Lerp(MODE_A_INTERVAL_NEAR, MODE_A_INTERVAL_FAR, t);
                        lowPitch = false;
                    }
                    else
                    {
                        // Mode B: out of range or blocked — halved pitch, no silence latch
                        float t = Mathf.Clamp01((distTiles - MODE_B_NEAR_TILES) /
                                                (MODE_B_FAR_TILES - MODE_B_NEAR_TILES));
                        interval = Mathf.Lerp(MODE_B_INTERVAL_NEAR, MODE_B_INTERVAL_FAR, t);
                        lowPitch = true;
                    }

                    // Silence latch only holds while the path is valid (Mode A).
                    if (beaconSilenced && pathValid)
                    {
                        nextBeaconTime = Time.time + 0.2f;
                        continue;
                    }

                    nextBeaconTime = Time.time + interval;

                    float maxDist = 500f;
                    float volumeScale = Mathf.Clamp(1f - (distTiles * TILE_SIZE / maxDist), 0.15f, 0.60f);

                    float deltaX = targetPos.x - playerPos.x;
                    float pan = Mathf.Clamp(deltaX / 100f, -1f, 1f) * 0.5f + 0.5f;

                    bool isSouth = targetPos.y < playerPos.y - 8f;

                    // Debounce: ensure at least 80% of the current interval has elapsed
                    float timeSinceLast = Time.time - lastBeaconPlayedAt;
                    if (timeSinceLast < interval * 0.8f)
                        continue;

                    SoundPlayer.PlayBeacon(isSouth, pan, volumeScale, lowPitch);
                    lastBeaconPlayedAt = Time.time;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Beacon] Error: {ex.Message}");
                    nextBeaconTime = Time.time + 0.5f;
                }
            }

            // Clean up when exiting
            beaconCoroutine = null;
            beaconSilenced = false;
            lastBeaconTarget = null;
        }
    }
}
