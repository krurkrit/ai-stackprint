// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Ui;

// Design tokens: the only place sizes, spacing, radii and type sizes are written as numbers (docs/design-system.md).
// Views use these names, never literals; a test fails on a literal in a view. Colors are in Theme.
// Type sizes are for a 14 px base; W.Txt/W.Title/W.Size scale them with Settings › Appearance.
// The scales follow Fluent UI 2 (spacing ramp, corner radii, type ramp, icon sizes, control heights); docs/design-system.md lists them side by side.

/// <summary>Spacing scale for gaps, margins and padding (the Fluent UI 2 ramp: 2, 4, 6, 8, 12, 16, 20, 24, 32; 8 is the default gap), and 1 for a hairline.</summary>
internal static class Space
{
    internal const double Hair = 1;    // between segments of a stacked bar
    internal const double XXS = 2;
    internal const double XS = 4;
    internal const double S = 6;
    internal const double M = 8;       // default gap between items in a row or list
    internal const double L = 12;      // between a label and its value; card header to body
    internal const double XL = 16;     // between cards
    internal const double XXL = 20;    // card padding; between page sections
    internal const double XXXL = 24;
    internal const double Page = 32;   // page side margins
}

/// <summary>Corner radii.</summary>
internal static class Radius
{
    internal const double XS = 2;      // chart columns (Fluent: small)
    internal const double S = 4;       // bars, small chips, code blocks (Fluent: medium)
    internal const double M = 4;       // buttons, list rows, inputs (Fluent: medium, the radius of every control)
    internal const double L = 8;       // dialogs, pop-ups (Fluent: xLarge)
    internal const double Tile = Sizes.IconTile * Sizes.TileCorner;   // the card-header icon tile (8)
    internal const double Card = 12;   // cards (Fluent: 2xLarge)
    internal const double XL = 12;     // overlays (command palette, toasts)
    internal const double Pill = 999;  // dots, pills, round buttons
}

/// <summary>Type scale (px at the 14 px base).</summary>
internal static class TextSize
{
    internal const double Tiny = 10;      // dense labels: axis ticks, badges (Fluent: caption 2)
    internal const double Caption = 12;   // captions, chips, hints (Fluent: caption 1)
    internal const double Small = 12;     // secondary text, table cells, legends (Fluent has no 13: secondary text is caption 1)
    internal const double Body = 14;      // default (Fluent: body 1)
    internal const double Subtitle = 16;  // card titles (Fluent: subtitle 2)
    internal const double Title = 20;     // section titles inside a page (Fluent: subtitle 1)
    internal const double Page = 24;      // page titles (H1) (Fluent: title 3)
    internal const double Display = 32;   // big numbers on cards (Fluent: title 1)
}

/// <summary>Icon glyph sizes.</summary>
internal static class IconSize
{
    internal const double XS = 12;  // beside caption text
    internal const double S = 16;   // inline with text, buttons, inputs
    internal const double M = 20;   // card headers (inside an icon tile)
    internal const double L = 20;   // navigation (Fluent icon sizes: 12, 16, 20, 24)
}

/// <summary>Line widths.</summary>
internal static class Stroke
{
    internal const double Thin = 1;     // borders, dividers
    internal const double Medium = 2;   // rings, chart lines, focus marks
}

/// <summary>Sizes of shared pieces. Input height is measured from the font (<see cref="Theme.InputHeight"/>).</summary>
internal static class Sizes
{
    internal const double Dot = 8;           // legend and status dots
    internal const double GraphNodeY = 16;   // where a card's dot sits: level with its first line
    internal const double GraphNode = 4;     // radius of a step's dot in the sub-agent graph
    internal const double RadioDot = 8;      // the dot inside a selected radio button (the control default, 12, is large and bright in a 20 px ring)
    internal const double Bar = 10;          // bar charts and progress bars
    internal const double BarThin = 8;       // progress bars inside dense rows
    internal const double Loader = 56;       // the big loading icon (DotsLoader) of a page that is still loading
    internal const double StarMin = 48;      // the least a star column counts for when a table works out its narrowest width
    internal const double IconTile = 32;     // tinted square behind a card or row icon
    internal const double TileGlyph = 0.625; // icon size as a share of its tile (20 in 32)
    internal const double LogoGlyph = 0.6;   // logo size as a share of its tile
    internal const double TileCorner = 0.25; // corner radius of an icon or logo tile as a share of its side
    internal const double LogoS = 24;        // app logo in dense rows and charts
    internal const double LogoM = 28;        // app logo in tables
    internal const double LogoL = 32;        // app logo in headers
    internal const double CompactControl = 24;   // pills, toolbar and inline buttons (Fluent: small control)
    internal const double PillCorner = CompactControl / 2;   // a pill button: fully round, the radius is half its height
    internal const double LargeControl = 40;     // the command palette search box (Fluent: large control)
    internal const double NavItem = 40;          // side menu entries
    internal const double LogoXL = 56;           // app or project logo beside a page title
    internal const double IconBox = 24;          // coordinate box icon paths are drawn in
    internal const double Swatch = 28;           // color swatch buttons
    internal const double ShadowRoom = 24;       // transparent room around a dialog card for its shadow
    internal const double Splitter = 6;          // drag handle between resizable panes
    internal const double LabelColumn = 170;     // name column of dashboard bars
    internal const double NumberColumn = 48;     // count column of dashboard bars
}

/// <summary>Widths of dialogs and side panels.</summary>
internal static class Widths
{
    internal const double LogTime = 64;      // time column of a conversation line
    internal const double LogWho = 130;      // who column of a conversation line
    internal const double GraphLane = 18;    // one lane of the sub-agent graph
    internal const double DialogS = 380;
    internal const double DialogM = 520;
    internal const double DialogL = 640;
    internal const double DialogXL = 1100;   // the Compare sessions dialog
    internal const double DialogMargin = 120; // room left around a wide dialog
    internal const double FontParent = 128;  // font setting control: parent drop-down (with its chain icon)
    internal const double FontFamily = 156; // family drop-down
    internal const double FontSize = 68;     // size number
    internal const double FontWeight = 160;  // weight drop-down, "Normal (400)"
    internal const double Field = 220;       // a drop-down or text box in a settings row
    internal const double FieldM = 160;      // a drop-down in a filter row
    internal const double FieldS = 120;      // a number box
    internal const double LogLevel = 52;     // level column of a text-log row (ERROR, WARN)
    internal const double LabelXS = 80;      // label column of a filter row
    internal const double LabelS = 100;      // label column of a key-value list
    internal const double Label = 160;       // label column of a settings form
    internal const double LabelL = 220;      // label column with an app logo and name
    internal const double SideList = 260;    // list beside a viewer (log files)
    internal const double SidePanel = 340;   // details beside a list
    internal const double Popup = 300;       // search results and small pop-ups
    internal const double Legend = 180;      // chart legend
    internal const double TitleSearch = 280; // the search box in the middle of the title bar
    internal const double Text = 460;        // longest line of a message
    internal const double ColApp = 150;      // table columns that hold the same kind of value are the same width on every page (see Cols)
    internal const double ColLevel = 110;
    internal const double ColType = 120;
    internal const double ColVersion = 90;
    internal const double ColStatus = 92;    // "Installed" in its pill / "Not found"
    internal const double ColCount = 80;
    internal const double ColAction = 40;    // one icon button
    internal const double ColActions = 112;  // three icon buttons
}

/// <summary>Heights of scrolling viewers (code, text, lists).</summary>
internal static class Heights
{
    internal const double LogRow = 30;       // one line of a conversation (and where the graph's dot sits)
    internal const double List = 260;        // a short list inside a card
    internal const double ViewerS = 320;     // command output, small editors
    internal const double Viewer = 420;      // code and text viewers inside a card
    internal const double ViewerL = 520;     // the log viewer, the command palette, images
}
