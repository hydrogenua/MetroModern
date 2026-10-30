using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MetroCore;

namespace MetroModern.Uwp.Services
{
    /// <summary>
    /// Loads a city scheme: T&Cs of the public yandex.ru/metro web API,
    /// prioritizes local cache > bundled .json.gz asset > live fetch.
    /// </summary>
    public sealed class MetroTaskbar
    {
        private const string CacheDir = "metro-cache";
        private readonly HttpClient _http;

        public MetroTaskbar()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "MetroModernW10M/1.0 (WindowsPhone10)");
            _http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            _http.DefaultRequestHeaders.Add("Referer", "https://yandex.ru/metro/moscow");
        }

        private static string SchemeIdFor(string cityAlias)
        {
            switch (cityAlias)
            {
                case "saint-petersburg": return "sc60983525";
                case "moscow": default: return "sc34974011";
            }
        }

        public async Task<Tuple<MetroNetwork, SvgScheme>> GetSchemeAsync(string cityAlias)
        {
            string json = await GetSchemeJsonAsync(cityAlias);
            var env = SchemeLoader.Parse(json);
            var net = SchemeLoader.BuildNetwork(env);

            double w = env.Data.Meta?.Int?.Width ?? 1800;
            double h = env.Data.Meta?.Int?.Height ?? 1800;
            var svg = SvgSchemeLoader.FromRaw(env.Data._raw, w, h);
            return Tuple.Create(net, svg);
        }

        public async void InvalidateCacheAsync()
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync(CacheDir);
                foreach (var f in await folder.GetFilesAsync())
                    await f.DeleteAsync();
            }
            catch { /* fresh install: no cache to drop */ }
        }

        private static async Task<string> GetSchemeJsonAsync(string city)
        {
            string cached = await ReadCacheAsync(city);
            if (cached != null) return cached;

            string bundled = await ReadBundledAsync(city);
            if (bundled != null)
            {
                await WriteCacheAsync(city, bundled);
                return bundled;
            }

            try
            {
                string live = await FetchFromApiAsync(SchemeIdFor(city));
                await WriteCacheAsync(city, live);
                return live;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не удалось найти кеш/ассет и получить схему онлайна.", ex);
            }
        }

        private static async Task<string> ReadCacheAsync(string city)
        {
            try
            {
                var dir = await ApplicationData.Current.LocalFolder.GetFolderAsync(CacheDir);
                var file = await dir.GetFileAsync(city + ".json");
                return await FileIO.ReadTextAsync(file);
            }
            catch { return null; }
        }

        private static async Task WriteCacheAsync(string city, string json)
        {
            try
            {
                StorageFolder dir;
                try { dir = await ApplicationData.Current.LocalFolder.GetFolderAsync(CacheDir); }
                catch { dir = await ApplicationData.Current.LocalFolder.CreateFolderAsync(CacheDir); }
                var file = await dir.CreateFileAsync(city + ".json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch { }
        }

        private static async Task<string> ReadBundledAsync(string city)
        {
            try
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/schemes/" + city + ".json.gz"));
                using (var stream = await file.OpenStreamForReadAsync())
                using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                using (var reader = new StreamReader(gz))
                    return reader.ReadToEnd();
            }
            catch { return null; }
        }

        /// <summary>
        /// Reverse-engineered public web flow:
        ///   GET /metro/api/scheme/getMetroSchemeBySchemeId?ajax=1&id=X&lang=ru
        ///       -> { "csrfToken": "..." }            (bootstrap)
        ///   GET same URL + csrfToken + sessionId + s (cache buster)
        ///       -> { "data": { "schemeMeta": {...}, "schemeData": {...} } }
        /// </summary>
        private static async Task<string> FetchFromApiAsync(string schemeId)
        {
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Add("User-Agent", "MetroModernW10M/1.0");
                http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                http.DefaultRequestHeaders.Add("Referer", "https://yandex.ru/metro/moscow");

                string bootUrl = $"https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId?ajax=1&id={schemeId}&lang=ru";
                string bootText = await http.GetStringAsync(bootUrl);
                string csrf = ExtractCsrf(bootText);
                if (csrf == null) throw new InvalidDataException("bootstrап: csrf не найти в " + bootText);

                string sid = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 100 + "-" +
                             (DateTime.Now.Ticks % 10000000000000000) + "-zbdsax5h2v5ebfy5-BAL";
                int s = new Random(unchecked((int)DateTime.Now.Ticks)).Next(300000000, 1800000000);

                string dataUrl = "https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId?ajax=1" +
                    "&csrfToken=" + Uri.EscapeDataString(csrf) +
                    "&id=" + schemeId + "&lang=ru&s=" + s + "&sessionId=" + sid;
                string data = await http.GetStringAsync(dataUrl);
                if (!data.Contains("\"schemeMeta\"")) throw new InvalidDataException("API вернул не схему: " + data.Substring(0, Math.Min(200, data.Length)));
                return data;
            }
        }

        private static string ExtractCsrf(string text)
        {
            var m = Regex.Match(text, "\"csrfToken\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
