using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    internal sealed class SteamAvatarCache
    {
        private readonly Dictionary<ulong, Texture2D> textures = new Dictionary<ulong, Texture2D>();
        private readonly HashSet<ulong> pending = new HashSet<ulong>();
        private readonly Queue<RawAvatar> completed = new Queue<RawAvatar>();
        private readonly object lockObject = new object();

        internal void Request(ulong steamId)
        {
            if (steamId == 0 || textures.ContainsKey(steamId)) return;
            lock (lockObject) { if (!pending.Add(steamId)) return; }
            Fetch(steamId);
        }

        private async void Fetch(ulong steamId)
        {
            try
            {
                Image? image = await SteamFriends.GetMediumAvatarAsync((SteamId)steamId);
                if (!image.HasValue || image.Value.Data == null || image.Value.Data.Length == 0) return;
                RawAvatar raw = new RawAvatar { SteamId = steamId, Width = (int)image.Value.Width, Height = (int)image.Value.Height, Data = image.Value.Data };
                lock (lockObject) completed.Enqueue(raw);
            }
            catch (Exception)
            {
                // Persona data may be delayed. A future UI refresh retries safely.
            }
            finally { lock (lockObject) pending.Remove(steamId); }
        }

        internal void Pump()
        {
            for (int i = 0; i < 4; i++)
            {
                RawAvatar raw;
                lock (lockObject) { if (completed.Count == 0) return; raw = completed.Dequeue(); }
                if (raw.Width <= 0 || raw.Height <= 0 || raw.Width > 512 || raw.Height > 512 || raw.Data == null || raw.Data.Length != raw.Width * raw.Height * 4) continue;
                Texture2D old;
                if (textures.TryGetValue(raw.SteamId, out old) && old != null) UnityEngine.Object.Destroy(old);
                Texture2D texture = new Texture2D(raw.Width, raw.Height, TextureFormat.RGBA32, false);
                // Steam's raw image rows are top-to-bottom; Unity's raw texture
                // upload expects the first row at the bottom. Flip once here so
                // both lobby cards and world cursor avatars use the same upright
                // cached texture without doing any work every frame.
                texture.LoadRawTextureData(FlipRows(raw.Data, raw.Width, raw.Height));
                texture.Apply(false, true);
                textures[raw.SteamId] = texture;
            }
        }

        internal Texture2D Get(ulong steamId) { Texture2D texture; return textures.TryGetValue(steamId, out texture) ? texture : null; }

        internal void Clear()
        {
            foreach (Texture2D texture in textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            textures.Clear();
            lock (lockObject) { pending.Clear(); completed.Clear(); }
        }

        private static byte[] FlipRows(byte[] source, int width, int height)
        {
            int rowBytes = width * 4;
            byte[] result = new byte[source.Length];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(source, y * rowBytes, result, (height - 1 - y) * rowBytes, rowBytes);
            return result;
        }

        private struct RawAvatar { internal ulong SteamId; internal int Width; internal int Height; internal byte[] Data; }
    }
}
