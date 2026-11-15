// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>Defaults with your changes on top, and back: only what differs from the defaults is saved.</summary>
public static class JsonLayers
{
    /// <summary>A copy of <paramref name="defaults"/> with every value of <paramref name="changes"/> on top; objects merge key by key, anything else replaces.</summary>
    public static JsonObject Merge(JsonObject defaults, JsonObject changes)
    {
        var result = (JsonObject)defaults.DeepClone();
        foreach (var (k, v) in changes)
        {
            result[k] = result[k] is JsonObject d && v is JsonObject c ? Merge(d, c) : v?.DeepClone();
        }
        return result;
    }

    /// <summary>The keys of <paramref name="value"/> that differ from <paramref name="defaults"/> (objects compared key by key); keys the defaults do not have are kept.</summary>
    public static JsonObject Diff(JsonObject value, JsonObject defaults)
    {
        var result = new JsonObject();
        foreach (var (k, v) in value)
        {
            if (!defaults.TryGetPropertyValue(k, out var d))
            {
                result[k] = v?.DeepClone();
            }
            else if (v is JsonObject vo && d is JsonObject dob)
            {
                if (Diff(vo, dob) is { Count: > 0 } inner)
                {
                    result[k] = inner;
                }
            }
            else if (!JsonNode.DeepEquals(v, d))
            {
                result[k] = v?.DeepClone();
            }
        }
        return result;
    }
}
