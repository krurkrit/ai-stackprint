// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

namespace AiStackprint.Services;

public sealed partial class FontRuleSet
{
    /// <summary>
    /// Takes out of the saved form of the font settings what is implied: an empty parent, an empty field, and a mode that is the default one
    /// (own value without a parent, linked with one). A mode stays when the built-in default has a different one, since leaving it out would bring that one back.
    /// </summary>
    internal static void Compact(JsonObject fonts, JsonObject? defaults)
    {
        foreach (var (key, node) in fonts)
        {
            if (node is not JsonObject rule)
            {
                continue;
            }

            var def = defaults?[key] as JsonObject;
            var parent = (string?)rule["parent"] ?? "";
            if (parent.Length == 0 && string.IsNullOrEmpty((string?)def?["parent"]))
            {
                rule.Remove("parent");
            }

            var implied = parent.Length > 0 ? "link" : "set";
            foreach (var field in new[] { "family", "size", "weight" })
            {
                if (rule[field] is not JsonObject f)
                {
                    continue;
                }

                if ((string?)f["mode"] == implied && def?[field]?["mode"] is null)
                {
                    f.Remove("mode");
                }

                if (f.Count == 0)
                {
                    rule.Remove(field);
                }
            }
        }
    }
}
