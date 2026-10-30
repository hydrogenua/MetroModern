using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MetroCore
{
    public static class SchemeLoader
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false
        };

        public static SchemeEnvelope Parse(string json)
        {
            var env = JsonSerializer.Deserialize<SchemeEnvelope>(json, Options);
            // capture raw graphics JsonElement for renderers (cheap re-read)
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("schemeData", out var sd) && sd.TryGetProperty("graphics", out var g))
                        env.Data._raw = g.Clone();
                }
            }
            catch { /* graphics are optional for routing */ }
            return env;
        }

        public static MetroNetwork BuildNetwork(SchemeEnvelope env)
        {
            return new NetworkBuilder().Build(env);
        }
    }
}
