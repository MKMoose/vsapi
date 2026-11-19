using Cairo;
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Client;

public class GuiElementServerCell : GuiElementMainMenuCell
{
    static readonly double[] fadedTextColor = ColorUtil.Hex2Doubles("#8F8C8A", 1);

    static readonly CairoFont titleFont        = CairoFont.WhiteSmallishText().WithWeight(FontWeight.Bold);
    static readonly CairoFont titleFontError   = titleFont.Clone().WithColor(fadedTextColor);
    static readonly CairoFont detailFontNormalFaded = CairoFont.WhiteSmallText().WithColor(fadedTextColor);
    static readonly CairoFont detailFontNormal = CairoFont.WhiteSmallText();

    static string? connectionErrorText;
    static string? probingConnectionText;
    private static string versionMismatchColor = "#d15a3d";
    private static string versionFadedColor = "#B5AFA5";

    IconComponent? icon;
    GuiElementRichtext? detailTextElem;
    GuiElementHoverText? hoverTextElem;
    ServerCellEntry serverEntry;

    readonly static string iconPathConnectionUnavailable = "icons/noconnection.svg";
    readonly static string[] iconPathsConnectionStrength = [ "icons/connection1.svg", "icons/connection2.svg", "icons/connection3.svg", "icons/connection4.svg", "icons/connection5.svg" ];
    readonly static string[] iconPathsProbing = [ "icons/probing1.svg", "icons/probing2.svg", "icons/probing3.svg", "icons/probing4.svg", "icons/probing5.svg" ];

    bool composed;

    /// <summary>
    /// Creates a new Element Cell.
    /// </summary>
    /// <param name="capi">The Client API</param>
    /// <param name="cell">The base cell</param>
    /// <param name="bounds">The bounds of the TableCell</param>
    public GuiElementServerCell(ICoreClientAPI capi, ServerCellEntry cell, ElementBounds bounds) : base(capi, cell, bounds)
    {
        serverEntry = cell;

        probingConnectionText ??= Lang.Get("server-connection-test");
        connectionErrorText   ??= Lang.Get("error-connection-test");
    }

    public override void Compose()
    {
        if (!composed) Recompose();
    }

    private void Recompose()
    {
        Bounds.CalcWorldBounds();

        ImageSurface surface = new ImageSurface(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        Context ctx = new Context(surface);

        ComposeButton(ctx, surface, false);
        generateTexture(surface, ref releasedButtonTexture);

        ctx.Dispose();
        surface.Dispose();

        ComposeHover(true, ref leftHighlightTexture);
        if (ShowModifyIcons)
        {
            ComposeHover(false, ref rightHighlightTexture);
        }
        composed = true;
        serverEntry.IsDirty = false;
    }

    public override void ComposeButton(Context ctx, ImageSurface surface, bool pressed) {

        double rightBoxWidth = ShowModifyIcons ? scaled(unscaledRightBoxWidth) : 0;
        pressedYOffset = 0;

        if (serverEntry.DrawAsButton)
        {
            RoundRectangle(ctx, 0, 0, Bounds.OuterWidthInt, Bounds.OuterHeightInt, 1);
            ctx.SetSourceRGB(GuiStyle.DialogDefaultBgColor[0], GuiStyle.DialogDefaultBgColor[1], GuiStyle.DialogDefaultBgColor[2]);
            ctx.Fill();

            if (pressed)
            {
                pressedYOffset = scaled(unscaledDepth) / 2;
            }

            EmbossRoundRectangleElement(ctx, 0, 0, Bounds.OuterWidthInt, Bounds.OuterHeightInt, pressed, (int)scaled(unscaledDepth));
        }

        Font = serverEntry.LastProbeState == ServerCellEntry.ProbeState.Errored ? titleFontError : titleFont;
        var mainTextYPos = Bounds.absPaddingY + Bounds.absPaddingY + scaled(serverEntry.LeftOffY) + pressedYOffset;
        titleTextheight = textUtil.AutobreakAndDrawMultilineTextAt(ctx, Font, serverEntry.Title, Bounds.absPaddingX * 2, mainTextYPos, Bounds.InnerWidth - rightBoxWidth - MainTextWidthSub);

        Font = detailFontNormalFaded;

        serverEntry.RightTopText = serverEntry.LastProbeState == ServerCellEntry.ProbeState.Success ? $"{serverEntry.PlayerCountCurrent}/{serverEntry.PlayerCountMax}" : null;
        if (serverEntry.RightTopText != null)
        {
            if (serverEntry.PlayerCountCurrent > 0)
            {
                Font = detailFontNormal;
            }
            TextExtents extents = Font.GetTextExtents(serverEntry.RightTopText);
            textUtil.AutobreakAndDrawMultilineTextAt(ctx, Font, serverEntry.RightTopText, Bounds.absPaddingX + Bounds.InnerWidth - extents.Width - rightBoxWidth - scaled(30), mainTextYPos, extents.Width + scaled(10), EnumTextOrientation.Right);
        }

        if (icon == null)
        {
            var color = (double[])GuiStyle.DialogDefaultBgColor.Clone();
            color[3] = 0.4; // so the hover texture still looks ok, but it does not match it perfectly just makes the difference less visible
            icon = new IconComponent(api, "connection", iconPathConnectionUnavailable, detailFontNormal, true, color) { sizeMulSvg = 1, PaddingRight = 5 };
        }

        icon.iconPath = (serverEntry.Ping) switch {
            _ when serverEntry.ProbingProgress >= 0 => iconPathsProbing[Math.Abs(serverEntry.ProbingProgress - 4)], //  remap 0 - 8 to abs(-4 - 4) for a bounce effect.
            >= 500 => iconPathsConnectionStrength[0],
            >= 250 => iconPathsConnectionStrength[1],
            >= 150 => iconPathsConnectionStrength[2],
            >=  50 => iconPathsConnectionStrength[3],
            >=   0 => iconPathsConnectionStrength[4],
            _      => iconPathConnectionUnavailable,
        };


        string? hoverText = null;
        string detailText;
        switch (serverEntry.LastProbeState)
        {
            case ServerCellEntry.ProbeState.InitialProbe:
                detailText = probingConnectionText!;
                break;

            case ServerCellEntry.ProbeState.Errored:
                detailText = connectionErrorText!;
                break;

            case ServerCellEntry.ProbeState.Success:
                if (serverEntry.NetworkVersion != GameVersion.NetworkVersion)
                {
                    detailText = Lang.Get("server-connection-details", serverEntry.Name, $"<font color=\"{versionMismatchColor}\">{serverEntry.ServerVersion}</font>", serverEntry.Ping);
                    hoverText = Lang.Get("server-connection-details-mismatch", versionMismatchColor, versionFadedColor, serverEntry.ServerVersion, GameVersion.ShortGameVersion);
                }
                else
                {
                    detailText = Lang.Get("server-connection-details", serverEntry.Name, serverEntry.ServerVersion, serverEntry.Ping);
                }
                break;

            default:
                detailText = "";
                break;
        }

        var components = VtmlUtil.Richtextify(api, " " + detailText, detailFontNormalFaded)
            .Prepend(icon).ToArray();
        foreach (var component in components) component.VerticalAlign = EnumVerticalAlign.Middle;
        if (detailTextElem == null)
        {
            var entryDetailTextYPos = Bounds.absPaddingY + serverEntry.DetailTextOffY + titleTextheight + (34 - titleTextheight);
            var richTextBounds = ElementBounds.Fixed(Bounds.absPaddingX, entryDetailTextYPos, Bounds.InnerWidth, 25).WithParent(Bounds);
            detailTextElem = new GuiElementRichtext(api, components, richTextBounds);
        }
        else
        {
            detailTextElem.SetNewText(components);
        }
        detailTextElem.BeforeCalcBounds();
        detailTextElem.Compose();

        if (hoverText != null)
        {
            if (hoverTextElem != null)
            {
                hoverTextElem.SetNewText(hoverText);
            }
            else
            {
                var bounds = ElementBounds.Fixed(-Bounds.absPaddingX, -Bounds.absPaddingY, Bounds.InnerWidth - Bounds.absPaddingX, Bounds.InnerHeight - Bounds.absPaddingY).WithParent(Bounds);
                Bounds.ChildBounds.Add(bounds);
                var textBackground = new TextBackground()
                {
                    HorPadding = (int)scaled(12),
                    FillColor = GuiStyle.DialogStrongBgColor,
                    BorderWidth = 4,
                    Shade = true,
                };
                hoverTextElem = new GuiElementHoverText(api, hoverText, CairoFont.WhiteSmallText(),350, bounds, textBackground);
                hoverTextElem.InsideClipBounds = InsideClipBounds;
                hoverTextElem.Bounds.CalcWorldBounds();
            }
        }
        else
        {
            hoverTextElem?.SetNewText(null);
        }

        if (ShowModifyIcons)
        {
            ctx.LineWidth = scaled(1);

            double crossSize = scaled(20);
            double crossWidth = scaled(5);

            ctx.SetSourceRGBA(0, 0, 0, 0.4);
            ctx.NewPath();
            ctx.MoveTo(Bounds.InnerWidth - rightBoxWidth, scaled(1));
            ctx.LineTo(Bounds.InnerWidth - rightBoxWidth, Bounds.OuterHeight - scaled(2));
            ctx.ClosePath();
            ctx.Stroke();

            ctx.SetSourceRGBA(1, 1, 1, 0.3);
            ctx.NewPath();
            ctx.MoveTo(Bounds.InnerWidth - rightBoxWidth + scaled(1), scaled(1));
            ctx.LineTo(Bounds.InnerWidth - rightBoxWidth + scaled(1), Bounds.OuterHeight - scaled(2));
            ctx.ClosePath();
            ctx.Stroke();

            double crossX = Bounds.absPaddingX + Bounds.InnerWidth - rightBoxWidth + scaled(5);
            double crossY = Bounds.absPaddingY;

            ctx.Operator = Operator.Source;

            ctx.SetSourceRGBA(0, 0, 0, 0.8);
            api.Gui.Icons.DrawPen(ctx, crossX - 1, crossY - 1 + scaled(5), crossWidth, crossSize);
            ctx.SetSourceRGBA(1, 1, 1, 0.5);
            api.Gui.Icons.DrawPen(ctx, crossX + 1, crossY + 1 + scaled(5), crossWidth, crossSize);
            ctx.SetSourceRGBA(0, 0, 0, 0.4);
            api.Gui.Icons.DrawPen(ctx, crossX, crossY + scaled(5), crossWidth, crossSize);

            ctx.Operator = Operator.Over;
        }

        if (serverEntry.DrawAsButton && pressed)
        {
            RoundRectangle(ctx, 0, 0, Bounds.OuterWidthInt, Bounds.OuterHeightInt, 1);
            ctx.SetSourceRGBA(0, 0, 0, 0.15);
            ctx.Fill();
        }
    }

    public override void OnRenderInteractiveElements(ICoreClientAPI api, float deltaTime)
    {
        base.OnRenderInteractiveElements(api, deltaTime);
        if (!composed || serverEntry.IsDirty) Recompose();

        detailTextElem?.RenderInteractiveElements(deltaTime);
        hoverTextElem?.RenderInteractiveElements(deltaTime);
    }

    public override void Dispose()
    {
        base.Dispose();
        detailTextElem?.Dispose();
        hoverTextElem?.Dispose();
    }
}
